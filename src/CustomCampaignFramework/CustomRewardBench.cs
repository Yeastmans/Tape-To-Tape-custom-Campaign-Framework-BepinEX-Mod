using System;
using CampaignCore;
using Data;
using HarmonyLib;
using Rogue;

namespace EndlessMode;

[HarmonyPatch(typeof(BenchBonusController), "GetBenchBonusMultiplier")]
internal static class CustomRewardBench
{
    [HarmonyPostfix] private static void Postfix(BenchBonusController __instance, ForwardData __0, ref float __result)
    {
        if (Plugin.IsDefaultMode || __0 == null || __instance.Team?._teamData == null) return;
        CustomRewardRuntime.EnsureAssets();
        double multiplier = 1;
        foreach (var d in CustomRewardRuntime.Definitions.Values) {
            if (d.NeedsRedesign || d.Trigger != "Bench bonus" || d.Effect != CustomRewardEffects.BenchMultiplier) continue;
            bool Owned(SkaterData data) => CustomRewardRuntime.Has(data, d.Id) && data.powerups.HasMatch(t => t != null && t.id == d.Id && !t.PayoffSuppressed);
            bool applies = d.IsRelic ? CustomRewardRuntime.Has(__instance.Team._teamData, d.Id)
                : d.Target == CustomRewardEffects.Actor ? Owned(__0)
                : __instance.Team.PlayingForwards.HasMatch(f => f != null && f.ArePowerUpsEnabled() && Owned(f._forwardData));
            if (applies) multiplier *= d.Amount / 100d;
        }
        __result = (float)Math.Min(float.MaxValue, __result * multiplier);
    }
}
