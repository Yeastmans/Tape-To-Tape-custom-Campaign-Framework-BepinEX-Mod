using System;
using Arena;
using HarmonyLib;
using UnityEngine;

namespace EndlessMode;

[HarmonyPatch(typeof(GolfIceOverlayRenderer), nameof(GolfIceOverlayRenderer.Initialize))]
internal static class GolfOverlayBake
{
    internal sealed class BakeScope
    {
        internal Transform Canvas, CanvasParent, Camera, CameraParent;
    }

    [HarmonyPrefix]
    internal static void Prefix(GolfIceOverlayRenderer __instance, out BakeScope __state)
    {
        __state = null;
        if (Plugin.IsDefaultMode || OpponentMatch.Surface != "Golf ice") return;
        var canvas = __instance.bakeCanvasRoot;
        var camera = __instance.textureBakerHelper?.renderCamera;
        if (canvas == null || camera == null) return;
        if (canvas.transform == __instance.transform || camera.transform == __instance.transform ||
            !canvas.transform.IsChildOf(__instance.transform) || !camera.transform.IsChildOf(__instance.transform)) return;
        __state = new BakeScope { Canvas = canvas.transform, CanvasParent = canvas.transform.parent,
            Camera = camera.transform, CameraParent = camera.transform.parent };
        // Native Initialize scales the overlay root to the ICE mesh before
        // baking. Keep the canvas/camera at their authored scale while only
        // the decal follows that rink transform. Native code destroys this
        // temporary canvas after the synchronous bake.
        canvas.transform.SetParent(null, true);
        camera.transform.SetParent(null, true);
        Plugin.Log.LogInfo("[GolfOverlay] Preserving canvas and camera scale during the native overlay bake.");
    }

    [HarmonyFinalizer]
    internal static Exception Finalizer(BakeScope __state, Exception __exception)
    {
        if (__state != null)
        {
            // Keep scene ownership even if the native bake failed. On success
            // the canvas is destroyed by native code and the camera is inactive.
            try
            {
                if (__state.Canvas != null) __state.Canvas.SetParent(__state.CanvasParent, true);
                if (__state.Camera != null) __state.Camera.SetParent(__state.CameraParent, true);
            }
            catch (Exception ex) { Plugin.Log.LogWarning("[GolfOverlay] Could not restore bake hierarchy: " + ex); }
        }
        return __exception;
    }
}
