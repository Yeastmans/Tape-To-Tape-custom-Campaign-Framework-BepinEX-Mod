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
    // Each extra goal re-runs native per-goal bookkeeping and assist events.
    // The repeat is proportional to the amount, so one goal adds at most this
    // many extra goals; it also keeps the int team score far from overflow.
    internal const int MaxExtraGoals = 9999;
    internal sealed class Scope
    {
        internal Skater Scorer; internal CustomRewardDefinition Rule;
        // One extra goal shares Hidden Ace's single native goal; Extra goals
        // rules add their amounts on top. Units is fixed at the scoring query
        // so the denial reverses exactly what was added.
        internal bool Shared; internal int Extra; internal int Units = -1;
        internal IntPtr Stats; internal bool Denial;
        internal int Query(bool nativeOwned) {
            if (Units < 0) Units = (nativeOwned || Shared ? 1 : 0) + Extra;
            return Units;
        }
    }
    [ThreadStatic] internal static Scope Current;
    private static readonly Dictionary<IntPtr, Scope> receipts = new();
    // The next native per-goal call in the Hidden Ace branch repeats this
    // many times in total; set by the capability query, consumed once.
    [ThreadStatic] private static IntPtr armedStats;
    [ThreadStatic] private static int armedUnits;

    internal static CustomRewardDefinition RuleFor(Skater player) => player != null && Current?.Scorer?.Pointer == player.Pointer ? Current.Rule : null;
    internal static Scope ScopeFor(Skater player) => player != null && Current?.Scorer?.Pointer == player.Pointer && Current.Rule != null ? Current : null;
    internal static void Arm(Scope scope, int units) { armedStats = scope?.Stats ?? IntPtr.Zero; armedUnits = units; }
    internal static int TakeArmed(IntPtr stats)
    {
        if (armedUnits <= 0 || stats != armedStats) return 0;
        int units = armedUnits; armedUnits = 0; armedStats = IntPtr.Zero; return units;
    }
    internal static void Disarm() { armedUnits = 0; armedStats = IntPtr.Zero; }
    internal static Scope BeforeScore(IntPtr stats, GoalScoredArg goal)
    {
        Scope previous = Current;
        Current = new Scope { Scorer = goal?.scorer, Stats = stats };
        Disarm();
        if (CustomRewardRuntime.InMatch && goal?.goalType == GoalType.GOOD && goal.scorer?.Team?.otherTeam != null) {
            var scorer = goal.scorer;
            bool Owned(Skater owner, CustomRewardDefinition rule) => owner != null && owner.ArePowerUpsEnabled()
                && CustomRewardRuntime.Has(owner._skaterData, rule.Id)
                && owner._skaterData.powerups.HasMatch(t => t != null && t.id == rule.Id && !t.PayoffSuppressed);
            long extra = 0;
            foreach (var rule in CustomRewardRuntime.Definitions.Values) {
                if (rule.NeedsRedesign || rule.Effect != CustomRewardEffects.ExtraGoal && rule.Effect != CustomRewardEffects.ExtraGoals || !Matches(rule.Trigger, goal)) continue;
                // Team and Actor rules count the owner's goals; Opposing team
                // rules count goals scored against the owner. An opponent that
                // owns the item through its team relics counts the same way.
                var owningTeam = rule.Target == CustomRewardEffects.OpposingTeam ? scorer.Team.otherTeam : scorer.Team;
                bool applies = rule.IsRelic ? CustomRewardRuntime.Has(owningTeam._teamData, rule.Id)
                    : rule.Target == CustomRewardEffects.Actor ? Owned(scorer, rule)
                    : owningTeam.PlayingForwards.HasMatch(f => f != null && !f.IsBenched() && Owned(f, rule));
                if (!applies) continue;
                if (rule.Effect == CustomRewardEffects.ExtraGoal) Current.Shared = true;
                else extra += rule.Amount;
                if (Current.Rule == null || rule.Effect == CustomRewardEffects.ExtraGoals && Current.Rule.Effect == CustomRewardEffects.ExtraGoal) Current.Rule = rule;
            }
            Current.Extra = (int)Math.Clamp(extra, 0, MaxExtraGoals);
            if (Current.Rule != null) receipts[stats] = Current; else receipts.Remove(stats);
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
        "Final 30 seconds goal value calculation" => RemainingWithin(30),
        "Final 60 seconds goal value calculation" => RemainingWithin(60),
        "Team first goal value calculation" => goal.scorer.Team.Score == 0,
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
        if (receipt != null) receipt.Denial = true;
        Disarm();
        return previous;
    }
    internal static void AfterDenial(IntPtr stats) { receipts.Remove(stats); Disarm(); }
    internal static void Clear() { receipts.Clear(); Current = null; Disarm(); }
}

[HarmonyPatch(typeof(GameStats), "OnGoalScored")]
internal static class CustomRewardGoalValueScope
{
    [HarmonyPrefix] private static void Prefix(GameStats __instance, GoalScoredArg __0, out CustomRewardGoalValues.Scope __state)
        => __state = CustomRewardGoalValues.BeforeScore(__instance.Pointer, __0);
    [HarmonyFinalizer] private static Exception Finalizer(Exception __exception, CustomRewardGoalValues.Scope __state)
    { CustomRewardGoalValues.Current = __state; CustomRewardGoalValues.Disarm(); return __exception; }
}
[HarmonyPatch(typeof(GameStats), "OnDeniedGoal")]
internal static class CustomRewardDeniedGoalValueScope
{
    [HarmonyPrefix] private static void Prefix(GameStats __instance, out CustomRewardGoalValues.Scope __state)
        => __state = CustomRewardGoalValues.BeforeDenial(__instance.Pointer);
    // Native denial has cancelled the base goal and one Hidden Ace unit;
    // cancel the remaining extra goals this goal added.
    [HarmonyPostfix] private static void Postfix(GameStats __instance)
        => CustomRewardRuntime.Safe(() => CustomRewardCapabilities.CancelExtraGoals(__instance.Pointer, __instance.lastScorer != null));
    [HarmonyFinalizer] private static Exception Finalizer(GameStats __instance, Exception __exception, CustomRewardGoalValues.Scope __state)
    { CustomRewardGoalValues.Current = __state; CustomRewardGoalValues.AfterDenial(__instance.Pointer); return __exception; }
}
