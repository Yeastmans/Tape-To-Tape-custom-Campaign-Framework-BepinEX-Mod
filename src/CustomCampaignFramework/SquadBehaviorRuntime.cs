using System;
using CampaignCore;

namespace EndlessMode;

internal static class SquadBehaviorRuntime
{
    internal static bool TryEnabled(string squadId, string behavior, out bool enabled)
    {
        enabled = false;
        if (Plugin.IsDefaultMode || squadId == null || !squadId.StartsWith("Custom_", StringComparison.Ordinal)
            || !Plugin.PlayerTeamConfigs.TryGetValue(squadId.Substring(7), out var team)) return false;
        enabled = SquadBehaviors.Enabled(team.SquadTemplate, behavior, key =>
            team.SquadBehaviors.TryGetValue(key, out string value) ? value : "");
        return true;
    }
    internal static bool Enabled(string squadId, string behavior, bool nativeDefault)
        => TryEnabled(squadId, behavior, out bool enabled) ? enabled : nativeDefault;
    internal static void Validate(TeamConfig team)
    {
        // Custom roster/menu support does not depend on the native comparison
        // bridge. An unreviewed game update leaves those address-based hooks
        // disabled, while authored squads remain available through managed hooks.
        var errors = SquadBehaviors.Conflicts(team.SquadTemplate, key =>
            key == "Bench Size" ? team.BenchSize.ToString() : key == "Starting Bench Positions" ? team.StartingBenchPositions
                : team.SquadBehaviors.TryGetValue(key, out string value) ? value : "");
        if (errors.Count > 0) throw new InvalidOperationException(string.Join(" ", errors));
        if(VanillaCampaignRuntime.InheritedSquad(team))return;
        bool On(string behavior) => SquadBehaviors.Enabled(team.SquadTemplate, behavior, k => team.SquadBehaviors.TryGetValue(k, out var v) ? v : "");
        int maximum = On("three_skaters") ? 3 : On("four_skaters") ? 4 : 5;
        string[] positions = { "Left Wing", "Right Wing", "Center", "Left Defense", "Right Defense" };
        var players = new[] { team.LW, team.RW, team.C, team.LD, team.RD };
        var bench = team.StartingBenchPositions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        int active = 0;
        for (int i = 0; i < players.Length; i++) if (PatchChooseMetaUI.SlotIsConfigured(players[i]) && Array.FindIndex(bench, p => p.Equals(positions[i], StringComparison.OrdinalIgnoreCase)) < 0) active++;
        if (active > maximum) throw new InvalidOperationException($"Choose at most {maximum} authored active starters in Studio before starting this squad.");
    }
}
