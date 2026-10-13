using System;
using System.IO;
using CampaignCore;
using HarmonyLib;
using Rogue;
using Tape2Tape.Hockey.UI;

namespace EndlessMode;

// Build 25717070: LoadCurrentTeam uses the normal BenchConfig pool when
// CustomSwapPool.Count == 0. An explicitly saved empty Studio pool must stay empty.
[HarmonyPatch(typeof(BenchwarmerMenu), "LoadCurrentTeam")]
internal static class StartingBenchRuntime
{
    [ThreadStatic] internal static bool SuppressNativePool;

    [HarmonyPrefix]
    private static void Prefix(BenchwarmerMenu __instance, out bool __state)
    {
        __state = SuppressNativePool;
        SuppressNativePool = false;
        if (Plugin.IsDefaultMode) return;
        try
        {
            var squad = __instance.m_CampaignState?.GetCurrentSquad();
            if (squad == null || !(squad.id ?? "").StartsWith("Custom_", StringComparison.Ordinal)) return;
            CampaignSquadSetup.ApplyBench(squad);
            var selected = SelectedPlayerPool.Read(Path.Combine(Plugin.ModFolder, "player_teams", "draft_pool"), 9);
            SuppressNativePool = selected != null && selected.Length == 0;
        }
        catch (Exception ex) { Plugin.Log.LogError("[CustomSquad] Could not refresh starting bench: " + ex.Message); }
    }

    [HarmonyFinalizer]
    private static Exception Finalizer(bool __state, Exception __exception)
    {
        SuppressNativePool = __state;
        return __exception;
    }
}

[HarmonyPatch(typeof(BenchConfig), "GetAllConfigs")]
internal static class EmptyStartingBench
{
    [HarmonyPrefix]
    private static bool Prefix(ref Il2CppSystem.Collections.Generic.IEnumerable<BenchConfig.ConfigData> __result)
    {
        if (!StartingBenchRuntime.SuppressNativePool) return true;
        __result = new Il2CppSystem.Collections.Generic.List<BenchConfig.ConfigData>()
            .Cast<Il2CppSystem.Collections.Generic.IEnumerable<BenchConfig.ConfigData>>();
        return false;
    }
}
