using System;
using System.Collections.Generic;
using System.Linq;
using CampaignCore;
using HarmonyLib;
using Movements.MovementStates;
using Skaters;
using UnityEngine;

namespace EndlessMode;

// Native ReleaseShot is reached only after the input/state eligibility checks.
// Capture the condition before possession changes; use native charge truncation
// and the native half-second attribute window, without a bundled controller.
internal static class CustomRewardChargedShots
{
    private sealed class Released
    {
        internal float Until;
        internal readonly Dictionary<string, (string Stat, int Amount)> Bonuses = new();
        internal readonly List<CustomRewardDefinition> Rules = new();
    }
    private static readonly Dictionary<IntPtr, Released> released = new();
    internal static CustomRewardDefinition Rule(Skater player)
    {
        var rules = CustomRewardRuntime.ConditionRules(CustomRewardEffects.IsCharge);
        if (player == null || (rules.Length == 0 && !released.ContainsKey(player.Pointer)) || !player.ArePowerUpsEnabled()) return null;
        return CustomRewardRuntime.ActiveConditions(player, rules).Concat(CapturedRules(player))
            .OrderBy(d => d.Effect == CustomRewardEffects.ChargeCritical ? 0 : 1).ThenBy(d => d.Seconds).FirstOrDefault();
    }
    internal static void Release(Forward player, float held)
    {
        if (player == null || !CustomRewardRuntime.InMatch || !player.ArePowerUpsEnabled() || !float.IsFinite(held)) return;
        var rules = CustomRewardRuntime.ConditionRules(CustomRewardEffects.IsCharge);
        if (rules.Length == 0) { released.Remove(player.Pointer); return; }
        var state = new Released { Until = Time.time + .5f };
        foreach (var rule in CustomRewardRuntime.ActiveConditions(player, rules)) {
            state.Rules.Add(rule);
            if (rule.Effect != CustomRewardEffects.ChargeCritical)
                state.Bonuses[rule.Id] = (rule.Effect == CustomRewardEffects.ChargePower ? "Shot Power" : "Accuracy", (int)(rule.Amount * Math.Clamp(held, 0, rule.Seconds)));
        }
        released[player.Pointer] = state;
    }
    private static IEnumerable<CustomRewardDefinition> CapturedRules(Skater player)
    {
        if (!released.TryGetValue(player.Pointer, out var state) || Time.time >= state.Until) yield break;
        foreach (var rule in state.Rules)
            if (player._skaterData?.powerups?.HasMatch(t => t != null && t.id == rule.Id && !t.PayoffSuppressed) == true) yield return rule;
    }
    internal static int Bonus(Skater player, string stat)
    {
        if (player == null || !released.TryGetValue(player.Pointer, out var state) || !player.ArePowerUpsEnabled()) return 0;
        if (Time.time >= state.Until) { released.Remove(player.Pointer); return 0; }
        long total = 0;
        foreach (var entry in state.Bonuses)
            if (entry.Value.Stat == stat && player._skaterData?.powerups?.HasMatch(t => t != null && t.id == entry.Key && !t.PayoffSuppressed) == true)
                total += entry.Value.Amount;
        return (int)Math.Clamp(total, 0, int.MaxValue);
    }
    internal static void Clear() => released.Clear();
}
