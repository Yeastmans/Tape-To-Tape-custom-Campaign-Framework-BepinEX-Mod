using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace EndlessMode;

// Full identity-preserving inventory, independent of mode-specific reward pools.
internal static class RewardInventory
{
    private static string Cell(string value) => (value ?? "").Replace("\t", " ").Replace("\r", "").Replace("\n", "\\n");
    private static string Text(Func<string> read)
    {
        try { return Regex.Replace(read() ?? "", "<[^>]+>", "").Trim(); }
        catch (Exception ex) { return "[Native text unavailable: " + ex.GetType().Name + "]"; }
    }
    internal static void Write(string folder)
    {
        var catalog = new StringBuilder("# kind\tkey\tid\tlevel\tasset\tname\tdescription\n");
        int count = 0;
        void Add(string kind, string id, int level, string asset, string name, string description)
        {
            catalog.AppendLine(string.Join("\t", new[] { kind, id, id, level.ToString(), Cell(asset), Cell(name), Cell(description) }));
            count++;
        }
        foreach (var t in Resources.FindObjectsOfTypeAll<Rogue.Talent>().Where(t => t != null && !string.IsNullOrEmpty(t.id) && !t.name.EndsWith("(Clone)")).GroupBy(t => t.id).Select(g => g.First()).OrderBy(t => t.id))
        {
            string kind = (t.category & Rogue.Powerups.Repository.TalentCategory.Goalie) != 0 ? "Goalie Talents" : "Talents";
            Add(kind, t.id, t.level, t.name, Text(() => t.localizedPowerupName), Text(() => t.GetDescription(false, true, "1FFF75")));
        }
        foreach (var a in Resources.FindObjectsOfTypeAll<Rogue.Ability>().Where(a => a != null && !string.IsNullOrEmpty(a.id) && !a.name.EndsWith("(Clone)")).GroupBy(a => a.id).Select(g => g.First()).OrderBy(a => a.id))
        {
            string type = a.GetIl2CppType().Name;
            string kind = type.Contains("Goalie") || type.Contains("Goaltender") ? "Goalie Abilities" : "Abilities";
            Add(kind, a.id, a.level, a.name, Text(() => a.localizedPowerupName), Text(() => a.GetDescription(false, true, "1FFF75")));
        }
        foreach (var r in Resources.FindObjectsOfTypeAll<Rogue.Relic>().Where(r => r != null && !string.IsNullOrEmpty(r.id) && !r.name.EndsWith("(Clone)")).GroupBy(r => r.id).Select(g => g.First()).OrderBy(r => r.id))
            Add("Relics", r.id, r.level, r.name, Text(() => r.localizedRelicName), Text(() => r.GetDescription(false, true, "1FFF75")));
        File.WriteAllText(Path.Combine(folder, "_reward_catalog.tsv"), catalog.ToString());
        Plugin.Log.LogInfo($"[RewardInventory] Wrote catalog of {count} unique assets");
    }
}
