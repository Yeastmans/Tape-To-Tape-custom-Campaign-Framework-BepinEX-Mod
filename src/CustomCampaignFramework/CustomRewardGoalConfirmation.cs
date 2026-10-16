using System;
using System.Collections.Generic;
using Events;
using HarmonyLib;
using Rogue.Relics.Components;
using UnityEngine;

namespace EndlessMode;

// GoalScored is provisional when No Goal opens a denial window. Keep custom
// goal/assist/hat-trick actions inside that scoring transaction: irreversible
// effects must not run and then try to undo themselves after a denied goal.
internal static class CustomRewardGoalConfirmation
{
    private static GoalScoredArg goal;
    internal static Skaters.Skater CurrentScorer => goal?.scorer;
    private static readonly List<Action> actions = new();
    private static readonly HashSet<IntPtr> windows = new();
    private static int changedFrame;
    private static bool processed;
    internal static void Begin(GoalScoredArg value)
    {
        if (!CustomRewardRuntime.InMatch || value?.goalType != NHL94.GoalType.GOOD) return;
        if (goal?.Pointer == value.Pointer) return;
        Clear(); goal = value; changedFrame = Time.frameCount;
    }
    internal static void Defer(Action action)
    {
        if (goal == null) { action(); return; }
        actions.Add(action);
    }
    internal static void Processed(GoalScoredArg value)
    {
        if (goal?.Pointer == value?.Pointer) { processed = true; changedFrame = Time.frameCount; }
    }
    internal static void Open(GoalDenierComponent component, GoalScoredArg value)
    {
        if (goal?.Pointer != value?.Pointer || component?._team != value?.scorer?.Team?.otherTeam
            || component._disableActionCoroutine == null) return;
        windows.Add(component.Pointer); changedFrame = Time.frameCount;
    }
    internal static void Close(GoalDenierComponent component)
    {
        if (component != null && windows.Remove(component.Pointer)) changedFrame = Time.frameCount;
    }
    internal static void Tick()
    {
        if (goal != null && processed && windows.Count == 0 && Time.frameCount > changedFrame) Flush();
    }
    internal static void Flush()
    {
        if (goal == null || !processed || windows.Count > 0) return;
        var ready = actions.ToArray(); Clear();
        foreach (var action in ready) CustomRewardRuntime.Safe(action);
    }
    internal static void Clear() { actions.Clear(); windows.Clear(); goal = null; processed = false; }
}

[HarmonyPatch(typeof(GoalDenierComponent), "OnGoalScored")]
internal static class CustomRewardGoalDenialWindow
{
    [HarmonyPrefix] private static void Prefix(GoalScoredArg __0) => CustomRewardGoalConfirmation.Begin(__0);
    [HarmonyPostfix] private static void Postfix(GoalDenierComponent __instance, GoalScoredArg __0) => CustomRewardGoalConfirmation.Open(__instance, __0);
}
[HarmonyPatch(typeof(GoalDenierComponent), "DisableInput")]
internal static class CustomRewardGoalDenialClosed
{
    [HarmonyPostfix] private static void Postfix(GoalDenierComponent __instance) => CustomRewardGoalConfirmation.Close(__instance);
}
[HarmonyPatch(typeof(GoalDenierComponent), "OnDestroy")]
internal static class CustomRewardGoalDenierRemoved
{
    [HarmonyPrefix] private static void Prefix(GoalDenierComponent __instance) => CustomRewardGoalConfirmation.Close(__instance);
}
[HarmonyPatch(typeof(GameStats), "OnDeniedGoal")]
internal static class CustomRewardGoalDenied
{
    [HarmonyPrefix] private static void Prefix(out Tape2Tape.Team __state) {
        __state = CustomRewardGoalConfirmation.CurrentScorer?.Team?.otherTeam;
        CustomRewardGoalConfirmation.Clear();
    }
    [HarmonyPostfix] private static void Postfix(Tape2Tape.Team __state) {
        if (__state?._teamData != null) CustomRewardRuntime.Safe(() => CustomRewardRuntime.SignalAll("Goal reversed", __state._teamData.id));
    }
}
