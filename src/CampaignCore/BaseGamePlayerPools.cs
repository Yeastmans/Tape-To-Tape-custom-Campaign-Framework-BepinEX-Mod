using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace CampaignCore;

public sealed class BaseGamePoolSelection
{
    public bool Enabled { get; set; }
    public Dictionary<string, string[]> Pools { get; } = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["draft_pool"] = Array.Empty<string>(),
        ["free_agents"] = Array.Empty<string>(),
        ["superstars"] = Array.Empty<string>()
    };
}

/// <summary>Portable Base Game overrides, separate from campaign and native game saves.</summary>
public static class BaseGamePlayerPools
{
    public static readonly string[] Kinds = { "draft_pool", "free_agents", "superstars" };
    public const string CustomIdPrefix = "CCF_BASE_POOL_";
    public static string Folder(string root) => Path.Combine(root, "base_game_pools");
    public static string PoolDirectory(string root, string kind)
    {
        ValidateKind(kind);
        return Path.Combine(Folder(root), "player_teams", kind.ToLowerInvariant());
    }
    public static BaseGamePoolSelection Load(string root)
    {
        string file = Path.Combine(Folder(root), "settings.txt");
        var selection = new BaseGamePoolSelection();
        if (!File.Exists(file)) return selection;
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in File.ReadAllLines(file))
            if (CampaignDocument.TryReadField(line, out string key, out string value))
            {
                if (fields.ContainsKey(key)) throw new InvalidDataException("Duplicate Base Game pool setting: " + key);
                fields.Add(key, value);
            }
        if (!fields.TryGetValue("schema", out string schema) || schema != "1") throw new InvalidDataException("Unsupported Base Game pool settings schema.");
        if (!fields.TryGetValue("enabled", out string enabled) || !(enabled.Equals("yes", StringComparison.OrdinalIgnoreCase) || enabled.Equals("no", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Base Game pool enabled setting must be yes or no.");
        selection.Enabled = enabled.Equals("yes", StringComparison.OrdinalIgnoreCase);
        foreach (string kind in Kinds)
        {
            if (!fields.TryGetValue(kind, out string values)) throw new InvalidDataException("Missing Base Game pool: " + kind);
            selection.Pools[kind] = values.Split(',').Select(v => v.Trim()).ToArray();
            SelectedFiles(root, kind, selection);
        }
        return selection;
    }
    public static void Save(string root, BaseGamePoolSelection selection)
    {
        if (selection == null) throw new ArgumentNullException(nameof(selection));
        // Validate every consumer before creating or changing the profile manifest.
        foreach (string kind in Kinds) SelectedFiles(root, kind, selection);
        var doc = new CampaignDocument("# Base Game player candidate pools\n");
        doc.Set("schema", "1"); doc.Set("enabled", selection.Enabled ? "yes" : "no");
        foreach (string kind in Kinds) doc.Set(kind, string.Join(",", selection.Pools[kind]));
        Directory.CreateDirectory(Folder(root));
        doc.Save(Path.Combine(Folder(root), "settings.txt"));
    }
    public static string[] SelectedFiles(string root, string kind, BaseGamePoolSelection selection)
    {
        string directory = PoolDirectory(root, kind);
        if (!selection.Pools.TryGetValue(kind, out string[] entries) || entries == null || entries.Length == 0)
            throw new ArgumentException("Select at least one candidate in " + kind.Replace('_', ' ') + ".");
        if (entries.Distinct(StringComparer.OrdinalIgnoreCase).Count() != entries.Length) throw new ArgumentException("A candidate cannot be selected twice in " + kind + ".");
        foreach (string entry in entries) ValidateFileName(entry);
        var files = entries.Select(e => Path.Combine(directory, e)).ToArray();
        foreach (string file in files) if (!File.Exists(file)) throw new FileNotFoundException("A selected Base Game candidate is missing.", file);
        return files;
    }
    public static string CustomPlayerId(string kind, string fileName)
    {
        ValidateKind(kind); ValidateFileName(fileName);
        using var sha = SHA256.Create();
        string identity = kind.ToLowerInvariant() + "/" + fileName.ToLowerInvariant();
        return CustomIdPrefix + BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(identity))).Replace("-", "");
    }
    private static void ValidateFileName(string fileName)
    {
        if (fileName == null || !Regex.IsMatch(fileName, "\\A[a-zA-Z0-9][a-zA-Z0-9._-]*\\.txt\\z", RegexOptions.IgnoreCase)
            || fileName.Contains("..") || Regex.IsMatch(fileName, "^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])\\.", RegexOptions.IgnoreCase))
            throw new ArgumentException("Invalid Base Game candidate filename.");
    }
    private static void ValidateKind(string kind)
    {
        if (!Kinds.Contains(kind, StringComparer.OrdinalIgnoreCase)) throw new ArgumentException("Unknown Base Game pool.", nameof(kind));
    }
}
