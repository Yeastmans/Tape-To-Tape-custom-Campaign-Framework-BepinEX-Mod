using System;
using System.Collections.Generic;
using HarmonyLib;
using Movements.MovementStates;

namespace EndlessMode;

internal static class CustomRewardSkatingEvents
{
    private static readonly HashSet<IntPtr> straightTriggered = new();
    internal static void Enter(SkatingState state) => straightTriggered.Remove(state.Pointer);
    internal static void Straight(SkatingState state)
    {
        if (!CustomRewardRuntime.InMatch || state?.forward == null || state.movementStateManager.TimeInState <= 1.75f || !straightTriggered.Add(state.Pointer)) return;
        CustomRewardRuntime.Signal("Straight skating for 1.75 seconds", state.forward);
    }
    internal static void Clear() => straightTriggered.Clear();
}
[HarmonyPatch(typeof(SkatingState), "OnEnter")]
internal static class CustomRewardStraightStart
{
    [HarmonyPostfix] private static void Postfix(SkatingState __instance) => CustomRewardSkatingEvents.Enter(__instance);
}
[HarmonyPatch(typeof(SkatingState), "SuperNovaHandler")]
internal static class CustomRewardStraightTrigger
{
    [HarmonyPostfix] private static void Postfix(SkatingState __instance) => CustomRewardRuntime.Safe(() => CustomRewardSkatingEvents.Straight(__instance));
}
[HarmonyPatch(typeof(BackwardSkating), "OnEnter")]
internal static class CustomRewardBackwardTrigger
{
    [HarmonyPostfix] private static void Postfix(BackwardSkating __instance) => CustomRewardRuntime.Safe(() => CustomRewardRuntime.Signal("Skating backward started", __instance.forward));
}
[HarmonyPatch(typeof(DefenseBackwardSkating), "OnEnter")]
internal static class CustomRewardDefenseBackwardTrigger
{
    [HarmonyPostfix] private static void Postfix(DefenseBackwardSkating __instance) => CustomRewardRuntime.Safe(() => CustomRewardRuntime.Signal("Skating backward started", __instance.forward));
}
