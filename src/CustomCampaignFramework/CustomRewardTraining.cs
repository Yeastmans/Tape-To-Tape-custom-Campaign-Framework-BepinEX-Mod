using System;
using System.Linq;
using CampaignCore;
using HarmonyLib;
using Rogue;
using Rogue.NPC.CoachNPC;
using Rogue.NPC.ConditioningNPC;
using State;
using Tape2Tape;

namespace EndlessMode;

[HarmonyPatch(typeof(TrainingEvent), "GetModifiedStatBonus")]
internal static class CustomRewardTraining
{
    [HarmonyPostfix] private static void Postfix(CampaignState __0, int __1, ref int __result)
    {
        var team = __0?.runData?.teamData;
        var rules = CustomRewardCampaignRules.Owned(team, "Training attribute rewards")
            .Where(d => d.Effect == CustomRewardEffects.TrainingMultiplier).ToArray();
        if (rules.Length == 0) return;
        float multiplier = team.GetRelic<VanillaTraining>()?.TrainingMultiplier ?? 1f;
        foreach (var rule in rules) multiplier = Math.Min(1e9f, multiplier * (rule.Amount / 100f));
        // Use the unrounded original amount. Rounding each multiplier in turn
        // would disagree with the native training preview and award.
        double value = __1 * multiplier;
        __result = (int)Math.Clamp(Math.Round(value, MidpointRounding.ToEven), int.MinValue, int.MaxValue);
    }
}
[HarmonyPatch(typeof(CoachEvent), "NextChoices")]
internal static class CustomRewardCoachPicks
{
    [HarmonyPostfix] private static void Postfix(CoachEvent __instance, int __0, ref bool __result)
    {
        if (__result) return;
        var team = CampaignUtil.CampaignState?.runData?.teamData;
        if (team == null) return;
        int extra = CustomRewardCampaignRules.Amount(team, "Coach choices", CustomRewardEffects.ExtraCoachPicks);
        if (extra <= 0 || __0 >= 1L + (team.HaveRelicCached<DoubleCoaching>() ? 1 : 0) + extra) return;
        __instance.Initialize(); __result = true;
    }
}
