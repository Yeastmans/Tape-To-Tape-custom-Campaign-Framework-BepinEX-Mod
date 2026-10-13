using System;
using Data;
using HarmonyLib;
using Tape2Tape;
using Tape2Tape.Customization.UI;
using Tape2Tape.Hockey.UI;

namespace EndlessMode;

// The lineup's IsHomeTeam describes the roster's side, not its selected kit.
// Both synchronous and awaited previews enter this void method after setting
// m_IsHome. Correct that render input without changing roster/team metadata.
[HarmonyPatch(typeof(SkaterPreviewInUI), "Refresh_Internal")]
internal static class LineupPreviewKit
{

    [HarmonyPrefix]
    internal static void Prefix(SkaterPreviewInUI __instance, TeamData team)
    {
        if (Plugin.IsDefaultMode || __instance == null || team == null) return;
        var menu = __instance.GetComponentInParent<LineupMenu>(true);
        if (menu == null || GameManager._instance == null) return;
        var original = menu.Team?.OriginalTeamData ?? team;
        if (!TryResolve(original, GameManager.HomeTeam, GameManager.VisitorTeam, out bool home)) return;
        __instance.m_IsHome = home;
    }

    internal static bool TryResolve(TeamData original, Team homeTeam, Team visitorTeam, out bool homeKit)
    {
        homeKit = true;
        if (original == null) return false;
        bool Live(Team candidate) => candidate != null && candidate.gameObject.activeInHierarchy
            && candidate.gameObject.scene.isLoaded && candidate._teamData != null;
        bool SamePointer(Team candidate) => Live(candidate) && candidate._teamData.Pointer == original.Pointer;
        bool SameId(Team candidate) => Live(candidate) && !string.IsNullOrEmpty(original.id) && candidate._teamData.id == original.id;
        // Identity first: mirrored teams may have the same ID on both sides.
        Team match = SamePointer(homeTeam) ? homeTeam : SamePointer(visitorTeam) ? visitorTeam : null;
        if (match == null)
        {
            bool h = SameId(homeTeam), v = SameId(visitorTeam);
            if (h == v) return false; // no match, or an ambiguous cloned roster
            match = h ? homeTeam : visitorTeam;
        }
        homeKit = match.UsesHomeUniform;
        return true;
    }
}
