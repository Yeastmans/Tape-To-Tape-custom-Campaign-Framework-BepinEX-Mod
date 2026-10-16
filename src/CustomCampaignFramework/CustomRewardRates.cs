using System;
using System.Collections.Generic;
using CampaignCore;
using HarmonyLib;
using Rogue;
using Rogue.Powerups.Controllers.Abilities;
using Skaters;

namespace EndlessMode;

[HarmonyPatch(typeof(EnragedStatusEffectController), "CheckForDurationMultiplier")]
internal static class CustomRewardEnragedDuration
{
    [HarmonyPostfix] private static void Postfix(EnragedStatusEffectController __instance) {
        var team = __instance.skater?.Team?._teamData;
        if (team == null) return;
        CustomRewardRuntime.EnsureAssets();
        double factor = 1;
        foreach (var rule in CustomRewardRuntime.Definitions.Values)
            if (!rule.NeedsRedesign && rule.IsRelic && rule.Effect == CustomRewardEffects.EnragedDuration && CustomRewardRuntime.Has(team, rule.Id))
                factor *= rule.Amount / 100d;
        __instance.timeRemaining = (float)Math.Min(float.MaxValue, __instance.timeRemaining * factor);
    }
}

[HarmonyPatch(typeof(SkaterStatusEffectsManager), "AddStatusEffect")]
internal static class CustomRewardStatusDuration
{
    [ThreadStatic] private static HashSet<IntPtr> active;
    private sealed class Scope { internal float Previous, Applied; }
    [HarmonyPrefix] private static void Prefix(SkaterStatusEffectsManager __instance, out Scope __state) {
        active ??= new HashSet<IntPtr>(); __state = null;
        if (!active.Add(__instance.Pointer)) return;
        __state = new Scope { Previous = __instance.statusEffectDurationMultiplier };
        __state.Applied = (float)Math.Min(float.MaxValue, __state.Previous * CustomRewardScaling.Multiplier(__instance._skater, CustomRewardEffects.StatusMultiplier));
        __instance.statusEffectDurationMultiplier = __state.Applied;
    }
    [HarmonyFinalizer] private static void Finalizer(SkaterStatusEffectsManager __instance, Scope __state) {
        if (__state == null) return;
        if (__instance.statusEffectDurationMultiplier == __state.Applied) __instance.statusEffectDurationMultiplier = __state.Previous;
        active.Remove(__instance.Pointer);
    }
}

[HarmonyPatch(typeof(AbilityControllerBase), "Update")]
internal static class CustomRewardAbilityRecharge
{
    private sealed class Snapshot { internal Ability Ability; internal float Time; internal int Charges; }
    [HarmonyPrefix] private static void Prefix(AbilityControllerBase __instance, out Snapshot __state) {
        var ability = __instance.AbilityDef;
        __state = ability == null ? null : new Snapshot { Ability = ability, Time = ability._timeInCooldown, Charges = ability._chargesLeft };
    }
    [HarmonyPostfix] private static void Postfix(AbilityControllerBase __instance, Snapshot __state) {
        var ability = __instance.AbilityDef;
        if (__state == null || ability == null || ability.Pointer != __state.Ability.Pointer || ability.baseCooldown < 0
            || ability._chargesLeft != __state.Charges || ability._chargesLeft >= ability.maxCharges) return;
        float advanced = ability._timeInCooldown - __state.Time;
        // Observe native progress instead of reproducing its pause, bench,
        // fighter, frog and state gates. No native advance means no bonus.
        if (advanced <= 0) return;
        double multiplier = CustomRewardScaling.Multiplier(__instance.skater, CustomRewardEffects.CooldownMultiplier);
        if (multiplier <= 1) return;
        ability._timeInCooldown = (float)Math.Min(float.MaxValue, __state.Time + advanced * multiplier);
        if (ability._timeInCooldown >= ability.baseCooldown) {
            // Same single-charge completion and overflow reset as native Update.
            ability._chargesLeft++; ability._timeInCooldown = 0; ability.MarkChanged();
        }
    }
}
