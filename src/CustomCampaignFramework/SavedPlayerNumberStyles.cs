using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CampaignCore;
using Data;

namespace EndlessMode;

// Native RunDataModelV2 omits numberFormat. Restore this authored appearance
// field only; applying a fresh player config here would erase earned stats.
internal static class SavedPlayerNumberStyles
{
    internal sealed class Entry
    {
        internal readonly string Id, Name;
        internal readonly int Style;
        internal Entry(string id, PlayerConfig config)
        { Id = id ?? ""; Name = config?.Name?.Trim() ?? ""; Style = config?.NumberFormat ?? -1; }
    }

    internal static List<Entry> Candidates(TeamConfig team)
    {
        var entries = new List<Entry>();
        void Add(PlayerConfig config, string id = "")
        {
            if (config == null) return;
            string native = config.Fields.Get(VanillaCopyData.PlayerId);
            entries.Add(new Entry(native.Length > 0 ? native : id, config));
        }
        foreach (var config in new[] { team.LW, team.RW, team.C, team.LD, team.RD,
            team.L2_LW, team.L2_RW, team.L2_C, team.L2_LD, team.L2_RD }) Add(config);
        var freeAgents = SquadPoolRuntime.FreeAgents(team);
        for (int i = 0; i < freeAgents.Count; i++)
        {
            var config = freeAgents[i];
            Add(config, CustomFreeAgentTemplates.PlayerId(team, i, config));
        }
        foreach (var config in SquadPoolRuntime.Superstars(team)) Add(config);
        string directory = SquadPoolRuntime.DirectoryPath(team, "draft_pool");
        var selected = SquadPoolRuntime.Selected(team, "draft_pool");
        if (selected == null) foreach (var config in Plugin.DraftPoolConfigs.Values) Add(config);
        else foreach (string file in selected)
        {
            var config = new PlayerConfig(); Plugin.LoadPlayerFile(file, config);
            // Base candidates retain native identity; without a recorded native
            // ID, only an unambiguous authored name may be used below.
            Add(config, config.Fields.Get("Studio Base Pool Player").Length > 0 ? ""
                : SelectedPlayerPool.PlayerId(directory, Path.GetFileName(file)));
        }
        return entries;
    }

    internal static int Resolve(IEnumerable<Entry> candidates, string id, string originalId, string name, bool uniqueName)
    {
        var all = candidates.ToArray();
        var exact = all.Where(c => c.Id.Length > 0 && (c.Id == id || c.Id == originalId)).ToArray();
        if (exact.Length > 0) return exact.Length == 1 ? exact[0].Style : -1;
        if (!uniqueName || string.IsNullOrWhiteSpace(name)) return -1;
        var named = all.Where(c => c.Name.Length > 0 && c.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
        return named.Length == 1 ? named[0].Style : -1;
    }

    internal static void Restore(TeamData team, TeamConfig config)
    {
        if (Plugin.IsDefaultMode || team?.forwards == null || config == null) return;
        try
        {
            var candidates = Candidates(config);
            var players = new List<ForwardData>();
            foreach (var player in team.forwards) if (player != null) players.Add(player);
            string Name(ForwardData p) => ((p.firstName ?? "") + " " + (p.lastName ?? "")).Trim();
            foreach (var player in players)
            {
                string name = Name(player);
                int style = Resolve(candidates, player.id, player.OriginalId, name,
                    players.Count(p => Name(p).Equals(name, StringComparison.OrdinalIgnoreCase)) == 1);
                if (style >= 0 && style < FieldSchema.NumberStyles.Length) player.numberFormat = (NumberFormat)style;
            }
        }
        catch (Exception ex) { Plugin.Log.LogWarning("[Appearance] Could not restore saved candidate number styles: " + ex.Message); }
    }
}
