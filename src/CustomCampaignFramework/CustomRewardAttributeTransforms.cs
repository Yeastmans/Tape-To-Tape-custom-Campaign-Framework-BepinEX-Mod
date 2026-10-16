using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CampaignCore;
using Data;
using HarmonyLib;
using Rogue;
using Skaters;
using UnityEngine;

namespace EndlessMode;

// Native averaging is a separate component from permanent gain conversion.
// All four getters have verified native callers. Scope the native flag around
// their own calculations so nested reads and exceptions restore its prior value.
[HarmonyPatch]
internal static class CustomRewardAverageAttributes
{
    private static IEnumerable<MethodBase> TargetMethods() {
        foreach (string method in new[] { "GetChecking", "GetAccuracy", "GetSpeed", "GetTotalShotPowerStats" }) yield return AccessTools.Method(typeof(ForwardStats), method);
    }
    [HarmonyPrefix] private static void Prefix(ForwardStats __instance, out bool __state) {
        __state = __instance._isBalanced;
        if (!__state && CustomRewardRuntime.ActiveConditions(__instance._forward).Any(d => d.Effect == CustomRewardEffects.BalanceAttributes)) __instance._isBalanced = true;
    }
    [HarmonyFinalizer] private static void Finalizer(ForwardStats __instance, bool __state) => __instance._isBalanced = __state;
}

[HarmonyPatch(typeof(ForwardData), "IncrementStats")]
internal static class CustomRewardSpreadAttributeGains
{
    [HarmonyPrefix] private static bool Prefix(ForwardData __instance, int __1, bool __2) {
        // Native Balanced already performs this conversion. Its recursive
        // IncrementStats calls set shouldCheckForBalance=false, as do ours.
        if (!__2 || __instance.HaveTalentCached<BalancedTalent>()) return true;
        CustomRewardRuntime.EnsureAssets();
        bool owns = __instance.powerups.HasMatch(t => t != null && !t.PayoffSuppressed
            && CustomRewardRuntime.Definitions.TryGetValue(t.id, out var rule) && !rule.NeedsRedesign
            && rule.Effect == CustomRewardEffects.SpreadAttributeGains);
        if (!owns) return true;
        __instance.IncrementOverallStats(Mathf.RoundToInt(__1 * .25f));
        return false;
    }
}
