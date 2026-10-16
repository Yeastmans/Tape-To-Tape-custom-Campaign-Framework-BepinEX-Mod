using System;
using System.Collections.Generic;
using System.Linq;
using CampaignCore;
using Events;
using HarmonyLib;
using NHL94;
using Skaters;

namespace EndlessMode;

// Snapshot before native score mutation, and reuse the SAME decision when a
// provisional goal is denied. Re-evaluating "first goal" after the score has
// changed, or checking ownership again during denial, would corrupt rollback.
internal static class CustomRewardGoalValues
{
    internal sealed class Scope { internal Skater Scorer; internal CustomRewardDefinition Rule; }
    [ThreadStatic] internal static Scope Current;
    private static readonly Dictionary<IntPtr, Scope> receipts = new();

    internal static CustomRewardDefinition RuleFor(Skater player) => player != null && Current?.Scorer?.Pointer == player.Pointer ? Current.Rule : null;
    internal static Scope BeforeScore(IntPtr stats, GoalScoredArg goal)
    {
        Scope previous = Current;
        Current = new Scope { Scorer = goal?.scorer };
        if (CustomRewardRuntime.InMatch && goal?.goalType == GoalType.GOOD && goal.scorer?.Team?.otherTeam != null) {
            var scorer = goal.scorer;
            bool Owned(Skater owner, CustomRewardDefinition rule) => owner != null && owner.ArePowerUpsEnabled()
                && CustomRewardRuntime.Has(owner._skaterData, rule.Id)
                && owner._skaterData.powerups.HasMatch(t => t != null && t.id == rule.Id && !t.PayoffSuppressed);
            foreach (var rule in CustomRewardRuntime.Definitions.Values) {
                if (rule.NeedsRedesign || rule.Effect != CustomRewardEffects.ExtraGoal || !Matches(rule.Trigger, goal)) continue;
                bool applies = rule.IsRelic ? CustomRewardRuntime.Has(scorer.Team._teamData, rule.Id)
                    : rule.Target == CustomRewardEffects.Actor ? Owned(scorer, rule)
                    : scorer.Team.PlayingForwards.HasMatch(f => f != null && !f.IsBenched() && Owned(f, rule));
                if (applies) { Current.Rule = rule; break; }
            }
            receipts[stats] = Current;
        } else receipts.Remove(stats);
        return previous;
    }
    private static bool Matches(string trigger, GoalScoredArg goal) => trigger switch {
        "Goal value calculation" => true,
        "Critical goal value calculation" => goal.isCritical,
        "Enraged goal value calculation" => goal.scorerWasEnraged,
        "Injured goal value calculation" => goal.scorer.TryCast<Forward>()?.HasAnyInjury() == true,
        "First goal value calculation" => goal.scorer.Team.Score == 0 && goal.scorer.Team.otherTeam.Score == 0,
        "Final 20 seconds goal value calculation" => RemainingWithin(20),
        "Final 45 seconds goal value calculation" => RemainingWithin(45),
        _ => false
    };
    private static bool RemainingWithin(float seconds)
    {
        var clock = GameManager.Instance?.timeRemaining;
        if (clock == null || clock.increaseTime) return false;
        float remaining = clock.GetRemainingTime();
        return remaining >= 0 && remaining <= seconds;
    }
    internal static Scope BeforeDenial(IntPtr stats)
    {
        Scope previous = Current;
        Current = receipts.TryGetValue(stats, out var receipt) ? receipt : null;
        return previous;
    }
    internal static void AfterDenial(IntPtr stats) => receipts.Remove(stats);
    internal static void Clear() { receipts.Clear(); Current = null; }
}

[HarmonyPatch(typeof(GameStats), "OnGoalScored")]
internal static class CustomRewardGoalValueScope
{
    [HarmonyPrefix] private static void Prefix(GameStats __instance, GoalScoredArg __0, out CustomRewardGoalValues.Scope __state)
        => __state = CustomRewardGoalValues.BeforeScore(__instance.Pointer, __0);
    [HarmonyFinalizer] private static Exception Finalizer(Exception __exception, CustomRewardGoalValues.Scope __state)
    { CustomRewardGoalValues.Current = __state; return __exception; }
}
[HarmonyPatch(typeof(GameStats), "OnDeniedGoal")]
internal static class CustomRewardDeniedGoalValueScope
{
    [HarmonyPrefix] private static void Prefix(GameStats __instance, out CustomRewardGoalValues.Scope __state)
        => __state = CustomRewardGoalValues.BeforeDenial(__instance.Pointer);
    [HarmonyFinalizer] private static Exception Finalizer(GameStats __instance, Exception __exception, CustomRewardGoalValues.Scope __state)
    { CustomRewardGoalValues.Current = __state; CustomRewardGoalValues.AfterDenial(__instance.Pointer); return __exception; }
}
