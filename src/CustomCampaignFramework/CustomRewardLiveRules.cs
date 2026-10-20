using System;
using System.Collections.Generic;
using System.Linq;
using AI.AIStates;
using CampaignCore;
using Data;
using HarmonyLib;
using Skaters;
using State;
using Tape2Tape;
using UnityEngine;

namespace EndlessMode;

// These are individual native capabilities, never talents inserted into an
// owner's loadout. Their ownership and condition are evaluated on every query.
internal static class CustomRewardLiveRules
{
    private sealed class SizeState { internal Forward Forward; internal Il2CppSystem.Object Source = new(); internal int Amount; }
    private static readonly Dictionary<IntPtr, SizeState> sizes = new();
    private static readonly Dictionary<IntPtr, string> ai = new();

    internal static void Refresh(Forward f)
    {
        // Six native sizes: clamp summed steps so native size arithmetic
        // never receives a value beyond its range.
        int amount = Math.Clamp(CustomRewardRuntime.Bonus(f, "Size"), -5, 5);
        if (amount != 0 || sizes.ContainsKey(f.Pointer)) {
            if (!sizes.TryGetValue(f.Pointer, out var state)) sizes[f.Pointer] = state = new SizeState { Forward = f };
            if (state.Amount != amount) {
                state.Amount = amount;
                if (amount == 0) f._forwardData.RemoveSizeEffect(state.Source); else f._forwardData.SetSizeEffect(state.Source, amount);
                f.SetSize();
            }
        }
        string behavior = Behavior(f);
        if (!ai.TryGetValue(f.Pointer, out string previous)) previous = "";
        if (previous != behavior) { ai[f.Pointer] = behavior; f.AIState?.ResetToDefaultState(); }
    }
    internal static string Behavior(Forward f)
    {
        if (!CustomRewardRuntime.InMatch || f?.Team == null || f.isControlled || f.IsBenched()) return "";
        if (GameManager.GameState is not (GameState.TeamHasPuck or GameState.DuringShot or GameState.DuringPass or GameState.PuckForGrabs)) return "";
        CustomRewardRuntime.ObserveOwnership(f.Team._teamData);
        var choices = CustomRewardRuntime.ActiveConditions(f).Where(d => CustomRewardEffects.AiEffects.Contains(d.Effect))
            .OrderByDescending(d => CustomRewardRuntime.Acquired(f, d))
            .Select(d => d.Effect).Distinct().Where(effect => effect switch {
                CustomRewardEffects.AiKarlson or CustomRewardEffects.AiPrince => f.HasPuck(),
                _ when f.HasPuck() => false,
                CustomRewardEffects.AiKnight => DifficultyManager.IsVeteranOrHigher() && PrinceCarrier(f),
                CustomRewardEffects.AiChase or CustomRewardEffects.AiBerserk => true,
                CustomRewardEffects.AiCherry => f.Team.HasPossession() || GameManager.GameState == GameState.PuckForGrabs,
                CustomRewardEffects.AiHome or CustomRewardEffects.AiMaraud => f.Team.HasPossession() && GameManager.GameState != GameState.PuckForGrabs,
                CustomRewardEffects.AiSafety => f.Team.HasPossession(),
                _ => f.Team.otherTeam?.HasPossession() == true && GameManager.GameState != GameState.PuckForGrabs
            }).Take(2).ToArray();
        return choices.FirstOrDefault() ?? "";
    }
    private static bool PrinceCarrier(Forward supporter)
    {
        var carrier = Puck.Instance?.skaterPossession?.TryCast<Forward>();
        if (carrier == null || carrier.Pointer == supporter.Pointer || carrier.Team != supporter.Team) return false;
        // Do not recursively query the native capability detour from its own
        // callback. An actual Prince talent retains native precedence.
        return carrier._skaterData.powerups.HasMatch(t => t != null && !t.PayoffSuppressed && t.TryCast<Rogue.PrinceAI>() != null)
            || Behavior(carrier) == CustomRewardEffects.AiPrince;
    }
    internal static void ApplyAI(AIState state)
    {
        var manager = state?.AIStateManager; var f = manager?.skater;
        if (f == null) return;
        switch (Behavior(f)) {
            case CustomRewardEffects.AiKnight: manager.SetKnightAI(); break;
            case CustomRewardEffects.AiChase: manager.SetBallChasingState(); break;
            case CustomRewardEffects.AiBerserk: manager.SetBerserkState(); break;
            case CustomRewardEffects.AiCherry: manager.SetCherryPickState(); break;
            case CustomRewardEffects.AiSafety: manager.SetSafetyFirstState(null); break;
            case CustomRewardEffects.AiBlock: manager.SetBlockShotState(); break;
            case CustomRewardEffects.AiRedirect: manager.SetRedirectorState(); break;
            case CustomRewardEffects.AiHome: manager.SetHomeWreckerState(); break;
            case CustomRewardEffects.AiMaraud: manager.SetMarauderState(); break;
        }
    }
    internal static void Clear()
    {
        foreach (var state in sizes.Values) if (state.Forward != null) {
            state.Forward._forwardData.RemoveSizeEffect(state.Source); state.Forward.SetSize();
        }
        sizes.Clear(); ai.Clear();
    }
}

[HarmonyPatch(typeof(AIState), "ResetToDefaultState")]
internal static class CustomRewardAI { [HarmonyPostfix] private static void Postfix(AIState __instance) => CustomRewardRuntime.Safe(() => CustomRewardLiveRules.ApplyAI(__instance)); }
[HarmonyPatch(typeof(CriticalShot), "GetCriticalOdds")]
internal static class CustomRewardCritChance
{
    [HarmonyPostfix] private static void Postfix(Forward __0, ShotType __1, ref CritOddsResult __result) {
        __result.conditional += CustomRewardRuntime.Bonus(__0, "Critical chance") + CustomRewardShotCalculations.ForCrit(__0, __1);
        double multiplier = CustomRewardScaling.Multiplier(__0, CustomRewardEffects.CritMultiplier);
        if (multiplier != 1) __result.conditional = (int)Math.Clamp(Math.Truncate((__result.permanent + (double)__result.conditional) * multiplier) - __result.permanent, -int.MaxValue, int.MaxValue);
    }
}
[HarmonyPatch(typeof(CriticalShot), "HasCritModifierSource")]
internal static class CustomRewardCritSource { [HarmonyPostfix] private static void Postfix(Forward __0, ref bool __result) => __result |= CustomRewardRuntime.Bonus(__0, "Critical chance") != 0; }
[HarmonyPatch(typeof(CriticalShot), "GetPowerValue")]
internal static class CustomRewardCritPower { [HarmonyPostfix] private static void Postfix(Forward __0, ref int __result) => __result = Math.Max(0, __result + CustomRewardRuntime.Bonus(__0, "Critical shot power")); }
[HarmonyPatch(typeof(Skater), "GetPassPower")]
internal static class CustomRewardPassPower { [HarmonyPostfix] private static void Postfix(Skater __instance, ref float __result) => __result *= Math.Max(0, 1 + CustomRewardRuntime.Bonus(__instance, "Pass speed") / 100f); }
