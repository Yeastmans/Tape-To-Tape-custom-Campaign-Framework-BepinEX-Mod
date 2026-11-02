using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace CampaignCore;

// Native run models retain talent IDs on players and queued offers. Keep their
// complete rules independent of the current Base Game pool selection.
public static class BaseGamePoolRunRewards
{
    private const string Format = "Format", Run = "Run ID", Count = "Reward Count", Integrity = "Snapshot SHA256";

    private static string Hash(string text)
    {
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "");
    }
    private static string PathFor(string root, string runId)
    {
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(runId) || runId.Any(char.IsControl))
            throw new ArgumentException("Base Game saved rewards require a content root and a valid run identity.");
        return Path.Combine(Path.GetFullPath(root), "base_game_pool_run_rewards", Hash(runId) + ".txt");
    }
    private static string Digest(CampaignDocument document) => Hash(string.Join("\n", document.Fields
        .Where(p => p.Key != Integrity).OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + "=" + p.Value)));

    public static Dictionary<string, CustomRewardDefinition> Load(string root, string runId)
    {
        string path = PathFor(root, runId);
        var result = new Dictionary<string, CustomRewardDefinition>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path)) return result;
        string text = File.ReadAllText(path);
        var fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(raw) || raw.TrimStart().StartsWith("#", StringComparison.Ordinal)) continue;
            if (!CampaignDocument.TryReadField(raw, out string key, out _) || !fields.Add(key)
                || key != Format && key != Run && key != Count && key != Integrity && !key.StartsWith(CustomRewardExport.Prefix, StringComparison.Ordinal))
                throw new InvalidDataException("Base Game saved reward data has malformed, duplicate or unknown fields.");
        }
        var document = new CampaignDocument(text);
        if (document.Get(Format) != "1" || document.Get(Run) != runId
            || !int.TryParse(document.Get(Count), NumberStyles.None, CultureInfo.InvariantCulture, out int count) || count < 0)
            throw new InvalidDataException("Base Game saved reward data has an unsupported format or belongs to another run.");
        if (document.Get(Integrity) != Digest(document))
            throw new InvalidDataException("Base Game saved reward data is incomplete or has changed since it was saved.");
        foreach (var definition in CustomRewardExport.Read(document)) CustomRewardExport.AddSnapshot(result, definition);
        if (result.Count != count) throw new InvalidDataException("Base Game saved reward data has an incomplete definition set.");
        return result;
    }

    public static void Save(string root, string runId, IEnumerable<CustomRewardDefinition> definitions)
    {
        if (definitions == null) throw new ArgumentNullException(nameof(definitions));
        string path = PathFor(root, runId);
        var snapshot = Load(root, runId);
        // Validate the complete merge before opening any output file. Previously
        // saved offers remain valid even when their source pool is later removed.
        foreach (var definition in definitions)
        {
            if (definition == null) throw new ArgumentException("A saved reward definition cannot be null.");
            definition.Validate(); CustomRewardExport.AddSnapshot(snapshot, definition.Copy());
        }
        var document = new CampaignDocument("# Base Game run reward definitions\nFormat = 1\n");
        document.Set(Run, runId); document.Set(Count, snapshot.Count.ToString(CultureInfo.InvariantCulture));
        CustomRewardExport.Embed(document, snapshot.Values.OrderBy(d => d.Id, StringComparer.Ordinal));
        document.Set(Integrity, Digest(document));
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        document.Save(path); // Unique same-directory staging and atomic replacement.
    }
}
