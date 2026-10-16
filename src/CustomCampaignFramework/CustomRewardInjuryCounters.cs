using System;
using System.Collections.Generic;
using CampaignCore;
using HarmonyLib;
using Rogue.Relics.Controllers;
using Skaters;

namespace EndlessMode;

// The native knockout path asks only the first controller of each type. Keep
// that call and its eligibility gates, then evaluate other custom-owned IDs.
// In particular every Egg Timer must advance even when an earlier one fires.
internal static class CustomRewardInjuryCounters
{
    [ThreadStatic] internal static bool Evaluating;
    internal static IEnumerable<T> Others<T>(RelicControllerBase source, string effect) where T : RelicControllerBase
    {
        if (Evaluating || !CustomRewardRuntime.InMatch || source?.Team?.relicControllers == null) yield break;
        foreach (var controller in source.Team.relicControllers) {
            if (controller == null || controller.Pointer == source.Pointer || !CustomRewardLifecycle.Rule(controller.Relic?.id, effect, out _)) continue;
            var typed = controller.TryCast<T>(); if (typed != null) yield return typed;
        }
    }
}
[HarmonyPatch(typeof(EggTimerController), nameof(EggTimerController.ShouldForceInjury))]
internal static class CustomRewardCyclicInjuryCounters
{
    [HarmonyPostfix] private static void Postfix(EggTimerController __instance, ref bool __result)
    {
        foreach (var other in CustomRewardInjuryCounters.Others<EggTimerController>(__instance, CustomRewardEffects.CyclicInjuries)) {
            try { CustomRewardInjuryCounters.Evaluating = true; __result |= other.ShouldForceInjury(); }
            finally { CustomRewardInjuryCounters.Evaluating = false; }
        }
    }
}
[HarmonyPatch(typeof(NutJobController), nameof(NutJobController.ShouldInjure))]
internal static class CustomRewardOpeningInjuryCounters
{
    [HarmonyPostfix] private static void Postfix(NutJobController __instance, Forward __0, Skater __1, ref bool __result)
    {
        foreach (var other in CustomRewardInjuryCounters.Others<NutJobController>(__instance, CustomRewardEffects.OpeningInjuries)) {
            try { CustomRewardInjuryCounters.Evaluating = true; __result |= other.ShouldInjure(__0, __1); }
            finally { CustomRewardInjuryCounters.Evaluating = false; }
        }
    }
}
[HarmonyPatch(typeof(HardBoiledEggController), nameof(HardBoiledEggController.ShouldForceInjury))]
internal static class CustomRewardPuckInjuryCounters
{
    [HarmonyPostfix] private static void Postfix(HardBoiledEggController __instance, ref bool __result)
    {
        if (__result) return;
        foreach (var other in CustomRewardInjuryCounters.Others<HardBoiledEggController>(__instance, CustomRewardEffects.PuckInjuries)) {
            try { CustomRewardInjuryCounters.Evaluating = true; __result |= other.ShouldForceInjury(); }
            finally { CustomRewardInjuryCounters.Evaluating = false; }
            if (__result) break;
        }
    }
}
