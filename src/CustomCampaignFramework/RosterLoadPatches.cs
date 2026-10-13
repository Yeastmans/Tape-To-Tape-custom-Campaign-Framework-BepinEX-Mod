using System;
using System.Collections.Generic;
using Data;
using HarmonyLib;
using State;

namespace EndlessMode;

[HarmonyPatch(typeof(DeserializerV2), "InitializeTeamDataFromModel")]
internal static class RosterLoadPatches
{
    [HarmonyPrefix]
    internal static void Prefix() => CustomStartingRoster.BeginLoad();
    [HarmonyPostfix]
    internal static void Postfix(TeamData __1) => CampaignAppearanceRuntime.RestoreSavedTeam(__1);
    [HarmonyFinalizer]
    internal static void Finalizer() => CustomStartingRoster.EndLoad();
}

// Native single-line GetBench does GetRange(5, Count - 5). A short old
// save must still be inspectable in Edit Lineup, even when it cannot be recovered.
[HarmonyPatch(typeof(TeamData), "GetBench")]
internal static class ShortRosterBenchPatch
{
    private static readonly HashSet<IntPtr> reported = new();
    [HarmonyPrefix]
    internal static bool Prefix(TeamData __instance, ref Il2CppSystem.Collections.Generic.List<ForwardData> __result)
    {
        if (__instance == null || __instance.lines?.Count > 1 || (__instance.forwards?.Count ?? 0) >= 5) return true;
        __result = new Il2CppSystem.Collections.Generic.List<ForwardData>();
        if (reported.Add(__instance.Pointer))
        {
            string detail = $"forwards={__instance.forwards?.Count ?? 0}; returned empty bench instead of negative range";
            Plugin.Log.LogWarning("[RosterLoad] " + __instance.teamName + ": " + detail);
        }
        return false;
    }
}
