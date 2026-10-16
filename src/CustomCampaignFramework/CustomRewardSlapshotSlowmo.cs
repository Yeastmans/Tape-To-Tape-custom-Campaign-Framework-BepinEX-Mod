using System;
using CampaignCore;
using HarmonyLib;
using Movements.MovementStates;
using Rogue;
using UnityEngine;

namespace EndlessMode;

internal static class CustomRewardSlapshotSlowmo
{
    private static SlapshotState owner;
    private static float previousScale;
    private static bool ending;
    [ThreadStatic] internal static bool Writing;
    internal static void BeforeMove(SlapshotState state)
    {
        Tick();
        if (owner != null || state?.forward == null || state._windupFinish || Time.timeScale < .01f
            || state.forward.HaveTalentCached<SlapshotSlowmo>()
            || !CustomRewardCapabilities.Active(state.forward, CustomRewardEffects.SlapshotSlowMotion)) return;
        previousScale = Time.timeScale; owner = state; ending = false;
        Writing = true;
        try { state.StartSlowmo(); }
        finally { Writing = false; }
    }
    // A native state has explicitly taken time-scale control. Its own cleanup
    // now owns the shared clock and sound snapshot; do not restore over it.
    internal static void NativeWrite() { if (!Writing) { owner = null; ending = false; } }
    internal static void End(SlapshotState state) { if (owner?.Pointer == state?.Pointer) Clear(); }
    internal static void Tick()
    {
        if (owner == null) return;
        if (ending) { if (Time.timeScale >= .01f) Clear(); return; }
        var player = owner.forward;
        if (!CustomRewardRuntime.InMatch || player == null || player.CurrentMovementState?.Pointer != owner.Pointer
            || player.HaveTalentCached<SlapshotSlowmo>() || !CustomRewardCapabilities.Active(player, CustomRewardEffects.SlapshotSlowMotion)) Clear();
    }
    internal static void Clear()
    {
        var state = owner;
        if (state == null) return;
        if (!ending) SoundManager.Instance?.StopSlowmoSnapshot();
        ending = true;
        // The pause controller may restore the pre-pause 40% value on resume.
        // Retain this pending cleanup until then instead of orphaning it.
        if (Time.timeScale < .01f) return;
        owner = null; ending = false;
        Writing = true;
        try {
            // Pause or another independent time effect may have changed the
            // clock since this contribution. Restore only our own value.
            if (Math.Abs(Time.timeScale - .4f) < .0001f) {
                Time.timeScale = previousScale;
                if (SoundManager.Instance != null) SoundManager.SetSfxTimeScale(previousScale);
            }
        } finally { Writing = false; }
    }
}
[HarmonyPatch(typeof(SlapshotState), "DoBeforeMove")]
internal static class CustomRewardSlapshotSlowmoStart
{
    [HarmonyPrefix] private static void Prefix(SlapshotState __instance) => CustomRewardRuntime.Safe(() => CustomRewardSlapshotSlowmo.BeforeMove(__instance));
}
[HarmonyPatch(typeof(SlapshotState), "OnExit")]
internal static class CustomRewardSlapshotSlowmoExit
{
    [HarmonyPostfix] private static void Postfix(SlapshotState __instance) => CustomRewardRuntime.Safe(() => CustomRewardSlapshotSlowmo.End(__instance));
}
[HarmonyPatch(typeof(SlapshotState), "Shot")]
internal static class CustomRewardSlapshotSlowmoShot
{
    [HarmonyPostfix] private static void Postfix(SlapshotState __instance) => CustomRewardRuntime.Safe(() => CustomRewardSlapshotSlowmo.End(__instance));
}
[HarmonyPatch(typeof(SlapshotState), "SetTimeScale")]
internal static class CustomRewardSlapshotNativeTime
{
    [HarmonyPrefix] private static void Prefix() => CustomRewardSlapshotSlowmo.NativeWrite();
}
