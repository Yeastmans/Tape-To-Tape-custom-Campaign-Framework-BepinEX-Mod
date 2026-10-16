using CampaignCore;
using System;
using Data;
using HarmonyLib;
using Rogue;

namespace EndlessMode;

// Both the native reward selector and CampaignUtil's event predicate call this
// concrete method. Retain their filtering, minimum-choice fallback and priority
// order instead of replacing the chosen players after the selection is made.
[HarmonyPatch(typeof(SkaterData), "HasTalentType")]
internal static class CustomRewardSelectionEligibility
{
    [HarmonyPostfix] private static void Postfix(SkaterData __instance, Il2CppSystem.Type __0, ref bool __result) {
        if (__result || __0 == null) return;
        string effect = __0.Pointer == RogueUtils.PICK_ME_TALENT_TYPE?.Pointer ? CustomRewardEffects.PreferRewards
            : __0.Pointer == RogueUtils.NEVER_ME_TALENT_TYPE?.Pointer ? CustomRewardEffects.ExcludeRewards : null;
        if (effect != null) __result = CustomRewardRuntime.OwnsDataEffect(__instance, effect);
    }
}

[HarmonyPatch(typeof(HelmetStackOvrTalent), "GetHelmetPowerupMultiplier")]
internal static class CustomRewardHelmetMultiplier
{
    [HarmonyPostfix] private static void Postfix(SkaterData __0, ref int __result) {
        if (__0?.powerups == null) return;
        CustomRewardRuntime.EnsureAssets();
        double factor = __result;
        foreach (var talent in __0.powerups) if (talent != null && !talent.PayoffSuppressed
            && CustomRewardRuntime.Definitions.TryGetValue(talent.id, out var rule) && !rule.NeedsRedesign && rule.Effect == CustomRewardEffects.HelmetMultiplier)
            factor *= rule.Amount;
        __result = (int)Math.Clamp(factor, 1, int.MaxValue);
    }
}
