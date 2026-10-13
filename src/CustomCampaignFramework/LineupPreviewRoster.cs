using System;
using Data;
using HarmonyLib;
using Tape2Tape.Hockey.UI;

namespace EndlessMode;

// TeamRosterData limits its preview bench to TeamData.benchSize. A custom
// two-line squad has no reserve slots, but its inactive line still has five
// players. Supply those players to the read-only UI without adding reserves
// or changing the native card locks / gameplay roster.
[HarmonyPatch(typeof(LineupMenu), "LoadCurrentTeam")]
internal static class LineupPreviewRoster
{
    [HarmonyPrefix]
    internal static void Prefix(LineupMenu __instance)
    {
        if (Plugin.IsDefaultMode || __instance == null || !__instance.m_AreSkatersLocked) return;
        try
        {
            var roster = __instance.Team;
            var team = roster?.OriginalTeamData;
            if (!CustomTeamRules.IsCustom(team) || !team.isMultiLineup || roster.m_BenchForwards == null) return;
            var bench = roster.m_BenchForwards;
            var inactive = team.benchPlayers;
            if (inactive == null || inactive.Count != 5) return;
            bool equal = bench.Count == inactive.Count;
            for (int i = 0; equal && i < inactive.Count; i++)
                equal = bench[i]?.Pointer == inactive[i]?.Pointer;
            if (equal) return;
            bench.Clear();
            for (int i = 0; i < inactive.Count; i++) bench.Add(inactive[i]);
        }
        catch (Exception ex) { Plugin.Log.LogWarning("[Lineup] Could not display inactive line: " + ex.Message); }
    }
}
