using System;
using System.Collections.Generic;
using CampaignCore;
using HarmonyLib;
using Tape2Tape;

namespace EndlessMode;

internal static class CustomRewardClock
{
    internal sealed class Scope
    {
        internal TimeRemaining Clock;
        internal Il2CppSystem.Func<float> Original, Wrapped;
        internal bool Active;
        internal double Rate;
    }
    private static readonly Dictionary<IntPtr, Scope> scopes = new();
    internal static double Multiplier()
    {
        if (!CustomRewardRuntime.InMatch) return 1;
        double result = 1;
        foreach (var team in new[] { GameManager.HomeTeam, GameManager.VisitorTeam }) {
            if (team?._teamData == null) continue;
            foreach (var rule in CustomRewardRuntime.InPlay) {
                if (rule.NeedsRedesign || !rule.IsRelic || rule.Effect != CustomRewardEffects.ClockSpeed || !CustomRewardRuntime.Has(team._teamData, rule.Id)) continue;
                bool active = rule.Trigger switch {
                    "While owned" => true,
                    "While leading" => team.otherTeam != null && team.Score > team.otherTeam.Score,
                    "While trailing" => team.otherTeam != null && team.Score < team.otherTeam.Score,
                    "While score tied" => team.otherTeam != null && team.Score == team.otherTeam.Score,
                    "While in period 1" => GameManager.Instance?.currentPeriod == 1,
                    "While in period 2" => GameManager.Instance?.currentPeriod == 2,
                    "While in period 3" => GameManager.Instance?.currentPeriod == 3,
                    _ => false
                };
                if (active) result *= rule.Amount / 100d;
            }
        }
        return result;
    }
    internal static Scope Enter(TimeRemaining clock)
    {
        if (clock == null || clock.increaseTime || clock.TimeModifier == null) return null;
        if (scopes.TryGetValue(clock.Pointer, out var scope) && scope.Active) return null;
        double rate = Multiplier();
        if (rate == 1) return null;
        if (scope == null || scope.Original.Pointer != clock.TimeModifier.Pointer) {
            scope = new Scope { Clock = clock, Original = clock.TimeModifier };
            // Original is immutable. A native modifier installed during this
            // scope can retain Wrapped safely; outside the scope its rate is 1.
            var captured = scope;
            scope.Wrapped = (Il2CppSystem.Func<float>)(Func<float>)(() => (float)(captured.Original.Invoke() * (captured.Active ? captured.Rate : 1)));
            scopes[clock.Pointer] = scope;
        }
        scope.Rate = rate; scope.Active = true; clock.TimeModifier = scope.Wrapped;
        return scope;
    }
    internal static void Exit(Scope scope)
    {
        if (scope == null) return;
        scope.Active = false;
        if (scope.Clock != null && scope.Clock.TimeModifier?.Pointer == scope.Wrapped.Pointer) scope.Clock.TimeModifier = scope.Original;
    }
    internal static void Clear() { foreach (var scope in scopes.Values) Exit(scope); scopes.Clear(); }
}

// DecreaseTime is inlined in Update in the audited native binary. Supplying
// the native TimeModifier lets its own pause gates, thresholds, events and
// end-of-period transition observe the scaled tick in the correct order.
[HarmonyPatch(typeof(TimeRemaining), "Update")]
internal static class CustomRewardClockTick
{
    [HarmonyPrefix] private static void Prefix(TimeRemaining __instance, out CustomRewardClock.Scope __state) => __state = CustomRewardClock.Enter(__instance);
    [HarmonyFinalizer] private static void Finalizer(CustomRewardClock.Scope __state) => CustomRewardClock.Exit(__state);
}
