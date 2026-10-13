using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace CampaignCore;

public static class SelectedPlayerPool
{
    public const string Manifest = "selected.list";
    public static string PlayerId(string directory, string entry)
    {
        Validate(new[] { entry }, 1);
        string identity = Path.GetFullPath(Path.Combine(directory, entry)).ToUpperInvariant();
        using var hash = SHA256.Create();
        return "CCF_BENCH_" + BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(identity))).Replace("-", "");
    }
    public static string[] Read(string directory, int maximum = int.MaxValue)
    {
        string path = Path.Combine(directory, Manifest);
        if (!File.Exists(path)) return null; // Legacy/unconfigured: retain native flow.
        string[] entries = File.ReadAllLines(path).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
        Validate(entries, maximum);
        return entries.Select(e => Path.Combine(directory, e)).ToArray();
    }
    public static void Validate(IEnumerable<string> values, int maximum)
    {
        var entries = values.ToArray();
        if (entries.Length > maximum) throw new ArgumentException("Choose at most " + maximum + " bench players across Base game and Custom.");
        if (entries.Distinct(StringComparer.OrdinalIgnoreCase).Count() != entries.Length) throw new ArgumentException("A player cannot be selected twice.");
        foreach (var entry in entries)
            if (entry != Path.GetFileName(entry) || entry.IndexOfAny(new[] { '/', '\\', ':', '\r', '\n' }) >= 0 || !entry.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Invalid selected player file.");
    }
    public static void Save(string directory, IEnumerable<string> values, int maximum = int.MaxValue)
    {
        var entries = values.ToArray(); Validate(entries, maximum);
        if (entries.Any(e => !File.Exists(Path.Combine(directory, e)))) throw new FileNotFoundException("A selected player file is missing.");
        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, Manifest), temp = file + ".tmp";
        try { File.WriteAllLines(temp, entries); if (File.Exists(file)) File.Replace(temp, file, null); else File.Move(temp, file); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
