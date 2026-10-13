using System.Linq;
using CampaignCore;
using Data;

namespace EndlessMode;

internal static class GolfTerrain
{
    internal const string RelicId = "71c7036b-ba18-46e0-8fef-d25217d5ca17";

    internal static void Apply(TeamData team, TeamConfig cfg)
    {
        if (Plugin.IsDefaultMode || cfg == null || !VanillaCampaignRuntime.OverrideSetting(team,"Match Surface","Golf Fairway")
            || Plugin.PlayerTeamConfigs.Values.Any(p => ReferenceEquals(p, cfg))) return;

        bool enabled = OpponentOptions.GolfFairwayEnabled(cfg.MatchSurface, cfg.GolfFairway);
        // Work on a new list: imported opponents may share their donor's relic list.
        // The native team setup then creates GolfFairwayController and owns cleanup.
        var relics = new Il2CppSystem.Collections.Generic.List<Rogue.Relic>();
        Rogue.Relic fairway = null;
        if (team.relics != null)
            foreach (var relic in team.relics)
            {
                if (relic != null && (relic.id == RelicId || relic.TryCast<Rogue.GolfFairway>() != null))
                    fairway = relic;
                else relics.Add(relic);
            }
        if (enabled)
        {
            fairway ??= PatchBossLaunchMatch.FindRelic(RelicId);
            if (fairway != null) relics.Add(fairway);
            else Plugin.Log.LogError("[GolfFairway] Native terrain effect asset unavailable for '" + team.teamName + "'.");
        }
        team.relics = relics;
        team.ResetRelicCache();
        Plugin.Log.LogInfo("[GolfFairway] '" + team.teamName + "': " + (enabled && fairway != null ? "enabled (native terrain physics and period layouts)" : "disabled"));
    }
}
