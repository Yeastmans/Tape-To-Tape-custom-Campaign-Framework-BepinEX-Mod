using System;
using System.Linq;
using CampaignCore;
using HarmonyLib;
using Skaters;

namespace EndlessMode;

// These scopes describe the two separate methods in the audited game build.
// Bat selects aim; its coroutine queries Slugger twice, first for vertical
// speed and then for the radial contact loop. Restore across nested coroutines.
internal static class CustomRewardBatting
{
    internal sealed class Scope
    {
        internal bool Aim;
        internal int Queries;
        internal CustomRewardDefinition Contact;
    }
    [ThreadStatic] internal static Scope Current;
    internal static CustomRewardDefinition Rule(Skater player, bool valueQuery)
    {
        var scope = Current;
        if (scope == null || player == null || !player.ArePowerUpsEnabled()) return null;
        if (valueQuery) return scope.Contact;
        string effect = scope.Aim ? CustomRewardEffects.BatAim : ++scope.Queries switch {
            1 => CustomRewardEffects.BatLift,
            2 => CustomRewardEffects.BatKnockout,
            _ => null
        };
        var rule = effect == null ? null : CustomRewardRuntime.ActiveConditions(player).Where(d => d.Effect == effect).OrderByDescending(d => d.Radius).FirstOrDefault();
        if (effect == CustomRewardEffects.BatKnockout) scope.Contact = rule;
        return rule;
    }
}

[HarmonyPatch(typeof(SkaterAbility), "Bat")]
internal static class CustomRewardBatAimScope
{
    [HarmonyPrefix] private static void Prefix(out CustomRewardBatting.Scope __state)
    { __state = CustomRewardBatting.Current; CustomRewardBatting.Current = new CustomRewardBatting.Scope { Aim = true }; }
    [HarmonyFinalizer] private static void Finalizer(CustomRewardBatting.Scope __state) => CustomRewardBatting.Current = __state;
}

[HarmonyPatch(typeof(SkaterAbility.__Bat_d__74), "MoveNext")]
internal static class CustomRewardBatContactScope
{
    [HarmonyPrefix] private static void Prefix(out CustomRewardBatting.Scope __state)
    { __state = CustomRewardBatting.Current; CustomRewardBatting.Current = new CustomRewardBatting.Scope(); }
    [HarmonyFinalizer] private static void Finalizer(CustomRewardBatting.Scope __state) => CustomRewardBatting.Current = __state;
}
