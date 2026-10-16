using System;
using System.Collections.Generic;
using CampaignCore;
using HarmonyLib;
using Rogue;
using Skaters;

namespace EndlessMode;

[HarmonyPatch(typeof(CriticalShot), nameof(CriticalShot.IsCriticalShot))]
internal static class CustomRewardOpeningShots
{
    // Native IsCriticalShot consumes only GetRelic<DryNoodle>(), the first
    // matching asset. Distinct custom IDs each own a counter. Leave the first
    // native consumption intact and consume the remaining custom counters once.
    [HarmonyPrefix] private static void Prefix(Forward forward, out List<(DryNoodle Relic, int Before)> __state)
    {
        __state = new();
        if (Plugin.IsDefaultMode || !CustomRewardRuntime.InMatch || forward?.Team?._teamData?.relics == null) return;
        foreach (var relic in forward.Team._teamData.relics) {
            var opening = relic?.TryCast<DryNoodle>();
            if (opening != null && opening.shotsLeft > 0 && CustomRewardLifecycle.Rule(opening.id, CustomRewardEffects.OpeningCriticals, out _))
                __state.Add((opening, opening.shotsLeft));
        }
    }
    [HarmonyPostfix] private static void Postfix(List<(DryNoodle Relic, int Before)> __state)
    {
        foreach (var item in __state)
            if (item.Relic != null && item.Relic.shotsLeft == item.Before) item.Relic.ReduceShotCount();
    }
}
