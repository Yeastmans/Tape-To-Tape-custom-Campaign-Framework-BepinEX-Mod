using System;
using Data;
using HarmonyLib;
using Tape2Tape.Hockey.UI;

namespace EndlessMode;

// TeamRosterData limits its preview bench to TeamData.benchSize. A custom
// two-line squad has no reserve slots, but its inactive line still has five
// players. Initialize both editable and locked menus from that inactive line,
// without adding reserves or changing native card locks / gameplay data.
[HarmonyPatch(typeof(LineupMenu), "InitData")]
internal static class LineupInitialRoster
{
    [HarmonyPostfix]
    internal static void Postfix(LineupMenu __instance) => LineupPreviewRoster.FillInactiveLine(__instance);
}

[HarmonyPatch(typeof(LineupMenu), "LoadCurrentTeam")]
internal static class LineupPreviewRoster
{
    [HarmonyPrefix]
    internal static void Prefix(LineupMenu __instance)
    {
        // A refresh of an editable menu must retain its pending swaps. InitData
        // fills its newly constructed model once; only locked views resync here.
        if (__instance == null || !__instance.m_AreSkatersLocked) return;
        FillInactiveLine(__instance);
    }

    internal static void FillInactiveLine(LineupMenu menu)
    {
        if (Plugin.IsDefaultMode || menu == null) return;
        try
        {
            var roster = menu.Team;
            var team = roster?.OriginalTeamData;
            if (team == null || (!CustomTeamRules.IsCustom(team) && !VanillaCampaignRuntime.Owns(team)) ||
                !team.isMultiLineup || roster.m_BenchForwards == null) return;
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
