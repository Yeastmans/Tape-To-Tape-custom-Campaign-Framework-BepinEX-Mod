using System;
using CampaignCore;
using DG.Tweening;
using HarmonyLib;
using UnityEngine;

namespace EndlessMode;

// BackToMap creates its own camera tween to literal 4.5 instead of using the
// world-map menu cache. Keep that native sequence, timing and completion flow;
// change only its endpoint while this exact call is constructing that tween.
[HarmonyPatch(typeof(MatchPreviewMenu), "BackToMap")]
internal static class CustomMapPreviewReturn
{
    internal sealed class Scope
    {
        internal readonly Camera Camera;
        internal readonly float HalfHeight;
        internal bool Used;
        internal Scope(Camera camera, float halfHeight) { Camera = camera; HalfHeight = halfHeight; }
    }

    [ThreadStatic] private static Scope current;

    [HarmonyPrefix]
    internal static void Prefix(MatchPreviewMenu __instance, out Scope __state)
    {
        __state = current;
        current = null;
        try
        {
            if (__instance != null && CustomMapScenery.TryPreviewFrame(__instance.currentMapNode,
                    __instance._camera, out float halfHeight))
                current = new Scope(__instance._camera, halfHeight);
        }
        catch (Exception ex) { Plugin.Log.LogWarning("[MapFraming] Preview return kept the native camera: " + ex.Message); }
    }

    // A finalizer runs on both normal and exceptional exits. It restores a
    // nested caller's scope, and never suppresses a native exception.
    [HarmonyFinalizer]
    internal static Exception Finalizer(Exception __exception, Scope __state)
    {
        current = __state;
        return __exception;
    }

    [HarmonyPatch(typeof(ShortcutExtensions), nameof(ShortcutExtensions.DOOrthoSize),
        new[] { typeof(Camera), typeof(float), typeof(float) })]
    private static class ReturnSize
    {
        [HarmonyPrefix]
        internal static void Prefix(Camera target, ref float endValue, float duration)
        {
            var scope = current;
            if (scope == null || scope.Used || target == null || scope.Camera == null || Plugin.IsDefaultMode
                || target.Pointer != scope.Camera.Pointer
                || !float.IsFinite(endValue) || !float.IsFinite(duration)
                || Math.Abs(endValue - MapVisualFit.CameraHalfHeight) > .0001f
                || Math.Abs(duration - .45f) > .0001f) return;
            endValue = scope.HalfHeight;
            scope.Used = true;
            Plugin.Log.LogInfo($"[MapFraming] Native preview return keeps custom camera half-height {endValue:0.00}.");
        }
    }
}
