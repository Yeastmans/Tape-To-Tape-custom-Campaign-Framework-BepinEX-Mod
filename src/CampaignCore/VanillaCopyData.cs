using System;
using System.IO;
using System.Linq;

namespace CampaignCore;

/// <summary>Copied native values remain editable while unchanged copies retain native selection.</summary>
public static class VanillaCopyData
{
    public const string OpponentId = "Vanilla Opponent Id";
    public const string SquadId = "Vanilla Squad Copy";
    public const string PoolManifest = "native-pool.list";
    public const string PlayerId = "Studio Native Player Id";
    public const string PlayerAsset = "Studio Native Player Asset";
    public static bool IsPool(string directory) => File.Exists(Path.Combine(directory, PoolManifest));
    public static bool SameDocument(string file)
    {
        if (!File.Exists(file) || !File.Exists(file + ".native")) return false;
        var current = new CampaignDocument(File.ReadAllText(file));
        var original = new CampaignDocument(File.ReadAllText(file + ".native"));
        // Opening an extracted player migrates obsolete color/skate fields to
        // the Studio appearance format. That cleanup is not a player edit.
        if (original.Get(PlayerId).Length > 0)
        {
            PlayerColorModel.Migrate(original); PlayerColorModel.Migrate(current);
            AppearanceContract.PinSkates(original); AppearanceContract.PinSkates(current);
        }
        string Value(string key,string value) => key.StartsWith("Squad Behavior ",StringComparison.OrdinalIgnoreCase)
            && value.Equals("Inherit", StringComparison.OrdinalIgnoreCase) ? "" : value;
        return current.Fields.Keys.Union(original.Fields.Keys, StringComparer.OrdinalIgnoreCase)
            .All(key => Value(key,current.Get(key)).Equals(Value(key,original.Get(key)), StringComparison.Ordinal));
    }
    public static bool PoolInherited(string directory)
    {
        if (!IsPool(directory)) return false;
        var baseline = File.ReadAllLines(Path.Combine(directory, PoolManifest)).Where(s => s.Length > 0).ToArray();
        SelectedPlayerPool.Validate(baseline, int.MaxValue);
        var selected = SelectedPlayerPool.Read(directory);
        return selected != null && selected.Select(Path.GetFileName).OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .SequenceEqual(baseline.OrderBy(s => s, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase)
            && selected.All(SameDocument);
    }
    public static bool TeamInherited(string directory)
    {
        if (!Directory.Exists(directory) || !File.Exists(Path.Combine(directory,"team.txt.native"))) return false;
        var baseline = Directory.GetFiles(directory,"*.native",SearchOption.AllDirectories)
            .Select(f => f.Substring(0,f.Length-7)).ToArray();
        var current = Directory.GetFiles(directory,"*",SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".txt",StringComparison.OrdinalIgnoreCase) || f.EndsWith(".cfg",StringComparison.OrdinalIgnoreCase)).ToArray();
        return baseline.Length == current.Length && baseline.All(SameDocument);
    }
}
