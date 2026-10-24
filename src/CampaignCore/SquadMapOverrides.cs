using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace CampaignCore;

/// <summary>Campaign-owned, full-map snapshots. Absence means live inheritance.</summary>
public static class SquadMapOverrides
{
    public const string ModeKey = "Use Own Maps";
    public const string Folder = "map_overrides";
    public static bool Own(CampaignDocument team) => team != null &&
        (team.Get(ModeKey).Equals("yes", StringComparison.OrdinalIgnoreCase)
        || team.Get(ModeKey).Equals("true", StringComparison.OrdinalIgnoreCase));

    public static string OverridePath(string squad, int stage)
    {
        if (stage < 1) throw new ArgumentOutOfRangeException(nameof(stage));
        return Path.Combine(squad, Folder, "map-" + stage + ".txt");
    }

    public static IEnumerable<string> Squads(string campaign)
    {
        string directory = Path.Combine(campaign, "player_teams");
        return Directory.Exists(directory) ? Directory.GetDirectories(directory)
            .Where(d => File.Exists(Path.Combine(d, "team.txt"))).OrderBy(d => d, StringComparer.OrdinalIgnoreCase) : Array.Empty<string>();
    }

    public static IEnumerable<string> Files(string squad)
    {
        string directory = Path.Combine(squad, Folder);
        return Directory.Exists(directory) ? Directory.GetFiles(directory, "map-*.txt").OrderBy(f => f, StringComparer.OrdinalIgnoreCase) : Array.Empty<string>();
    }

    public static string ResolvePath(string campaign, string squad, int stage)
    {
        if (stage < 1) throw new ArgumentOutOfRangeException(nameof(stage));
        if (!string.IsNullOrEmpty(squad))
        {
            RequireSquad(campaign, squad);
            string file = OverridePath(squad, stage);
            // Never parse here or silently replace an invalid existing override.
            // The adapter validates the selected file and reports that source.
            if (Own(new CampaignDocument(File.ReadAllText(Path.Combine(squad, "team.txt")))) && File.Exists(file)) return file;
        }
        return Path.Combine(campaign, "map_layouts", "map-" + stage + ".txt");
    }

    private static void RequireSquad(string campaign, string squad)
    {
        string parent = Path.GetDirectoryName(Path.GetFullPath(squad).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (!string.Equals(parent, Path.GetFullPath(Path.Combine(campaign, "player_teams")), StringComparison.OrdinalIgnoreCase)
            || !File.Exists(Path.Combine(squad, "team.txt")))
            throw new ArgumentException("Maps belong to a saved player squad in this campaign.");
    }

    public static string Revision(string campaign, string squad)
    {
        RequireSquad(campaign, squad);
        var bytes = new StringBuilder();
        void Add(string file) {
            string name = Path.GetRelativePath(campaign, file);
            string content = File.Exists(file) ? File.ReadAllText(file) : "<missing>";
            bytes.Append(name.Length).Append(':').Append(name).Append(content.Length).Append(':').Append(content);
        }
        Add(Path.Combine(campaign, "campaign.txt"));
        Add(Path.Combine(squad, "team.txt"));
        string shared = Path.Combine(campaign, "map_layouts");
        if (Directory.Exists(shared))
            foreach (string file in Directory.GetFiles(shared, "map-*.txt").OrderBy(f => f, StringComparer.OrdinalIgnoreCase)) Add(file);
        foreach (string file in Files(squad)) Add(file);
        using var hash = SHA256.Create();
        return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(bytes.ToString()))).Replace("-", "");
    }

    public static void Save(string campaign, string squad, string expectedRevision, IReadOnlyDictionary<int, MapLayout> changes)
    {
        RequireSquad(campaign, squad);
        if (Revision(campaign, squad) != expectedRevision)
            throw new InvalidOperationException("The campaign or squad maps changed while this editor was open. Your draft is still open; reopen team maps from the updated campaign before saving.");
        int count = CampaignActSequence.Read(new CampaignDocument(File.ReadAllText(Path.Combine(campaign, "campaign.txt")))).Length;
        var writes = new Dictionary<string, string>();
        foreach (var change in changes)
        {
            if (change.Key < 1 || change.Key > count) throw new ArgumentException("Team maps must keep the campaign's map count and order.");
            writes.Add(OverridePath(squad, change.Key), change.Value?.Serialize());
        }
        if (writes.Count == 0) return;
        string backup = Path.Combine(campaign, "map_backups", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff") + "-squad-" + Guid.NewGuid().ToString("N"));
        Apply(campaign, writes, backup);
    }

    /// <summary>Index of the original map at each new stage; zero denotes an added map.</summary>
    public static void Remap(string campaign, IReadOnlyList<int> originalStages, string backup)
    {
        int count = CampaignActSequence.Read(new CampaignDocument(File.ReadAllText(Path.Combine(campaign, "campaign.txt")))).Length;
        if (originalStages.Count == 0 || originalStages.Any(i => i < 0 || i > count)
            || originalStages.Where(i => i > 0).Distinct().Count() != originalStages.Count(i => i > 0))
            throw new ArgumentException("Invalid original campaign map order.");
        if (originalStages.SequenceEqual(Enumerable.Range(1, count))) return;
        var writes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string squad in Squads(campaign))
        {
            // Read every original before writing any destination (including swaps).
            var originals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string file in Files(squad)) originals.Add(file, File.ReadAllText(file));
            foreach (string file in originals.Keys) writes[file] = null;
            for (int i = 0; i < originalStages.Count; i++)
                if (originalStages[i] > 0 && originals.TryGetValue(OverridePath(squad, originalStages[i]), out var content))
                    writes[OverridePath(squad, i + 1)] = content;
        }
        if (writes.Count > 0) Apply(campaign, writes, backup);
    }

    private static void Apply(string campaign, Dictionary<string, string> writes, string backup)
    {
        var originals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string file in writes.Keys) originals.Add(file, File.Exists(file) ? File.ReadAllText(file) : null);
        // Finish all backups before the first mutation. Backups include disabled
        // overrides and removed maps, using their original squad/stage paths.
        foreach (var original in originals.Where(p => p.Value != null))
        {
            string copy = Path.Combine(backup, Path.GetRelativePath(campaign, original.Key));
            Directory.CreateDirectory(Path.GetDirectoryName(copy));
            File.Copy(original.Key, copy, false);
        }
        void Write(string path, string content) {
            if (content == null) { if (File.Exists(path)) File.Delete(path); }
            else { Directory.CreateDirectory(Path.GetDirectoryName(path)); new CampaignDocument(content).Save(path); }
        }
        try { foreach (var entry in writes) Write(entry.Key, entry.Value); }
        catch { foreach (var entry in originals) Write(entry.Key, entry.Value); throw; }
    }
}
