using System;
using STS.Map;
using Tape2Tape.Hockey.UI;
using UnityEngine;
using HarmonyLib;

namespace EndlessMode;

// The native intro captures a MapObject before awaiting the loading screen.
// Bind the destination to the loaded custom graph instead. Never change a
// progressed map, ordinary scrolling, the native curve, zoom, or intro timing.
[HarmonyPatch(typeof(WorldMapActIntro), "Evaluate")]
internal static class CustomMapIntroTarget
{
    private const float Arrival = 6.704165f, Duration = 7.204165f;
    private sealed class Scope
    {
        internal WorldMapActIntro Intro;
        internal MapManager Manager;
        internal MapObject View;
        internal STS.Map.Map Map;
        internal ScrollNonUI Scroller;
        internal Camera Camera;
        internal State.CampaignState State;
        internal IntPtr Run;
        internal string RunId, Folder, Campaign;
        internal float Start, End, NativeEnd;
        internal bool HasNativeEnd, Logged;
        internal int FinalFrame = -1;

        internal bool Valid() => !Plugin.IsDefaultMode && Intro != null && WorldMapActIntro.IsPlaying
            && View != null && Camera != null && Scroller != null && Manager != null
            && UnityEngine.Camera.main != null && UnityEngine.Camera.main.Pointer == Camera.Pointer
            && View.m_Scroller != null && View.m_Scroller.Pointer == Scroller.Pointer
            && Manager.CurrentMap != null && Manager.CurrentMap.Pointer == Map.Pointer
            && Map.path != null && Map.path.Count == 0
            && Scroller.mainCamera != null && Scroller.mainCamera.Pointer == Camera.Pointer
            && State?.runData != null && State.runData.Pointer == Run && State.runData.runID == RunId
            && State.runData.currentNodePoint.x < 0 && State.runData.currentNodePoint.y < 0
            && Plugin.ModFolder == Folder && Plugin.ActiveCampaign == Campaign;
    }
    private static Scope current;

    private static Scope Bind(WorldMapActIntro intro)
    {
        var manager = UnityEngine.Object.FindObjectOfType<MapManager>();
        var map = manager?.CurrentMap;
        if (map?.nodes == null || map.nodes.Count == 0 || map.path == null || map.path.Count != 0
            || CustomMapLayout.LayoutFor(map) == null) return null;
        MapObject view = null;
        MapNode entry = null;
        foreach (var candidate in UnityEngine.Object.FindObjectsOfType<MapObject>())
        {
            if (candidate == null || candidate.MapNodes == null || candidate.MapNodes.Count != map.nodes.Count) continue;
            bool belongs = true;
            MapNode first = null;
            foreach (var node in candidate.MapNodes)
            {
                bool found = false;
                if (node?.Node != null)
                    foreach (var graphNode in map.nodes)
                        if (graphNode != null && graphNode.Pointer == node.Node.Pointer) { found = true; break; }
                if (!found || node.CurrentState == NodeStates.Visited) { belongs = false; break; }
                if (first == null && node.CurrentState == NodeStates.Attainable) first = node;
            }
            if (!belongs || first?.Node == null || first.Node.LayerIndex != 0) continue;
            if (view != null) return null; // Ambiguous loaded views: keep native behavior.
            view = candidate; entry = first;
        }
        var scroller = view?.m_Scroller;
        var camera = Camera.main;
        var state = entry?.campaignState;
        if (view == null || scroller == null || camera == null || scroller.mainCamera == null
            || scroller.mainCamera.Pointer != camera.Pointer || state?.runData == null
            || string.IsNullOrEmpty(state.runData.runID)
            || state.runData.currentNodePoint.x >= 0 || state.runData.currentNodePoint.y >= 0) return null;
        var markers = UnityEngine.Object.FindObjectsOfType<WorldMapActIntroStart>();
        if (markers.Length > 1) return null;
        float start = markers.Length == 1 ? markers[0].transform.position.x : scroller.xConstraints.max;
        float end = Math.Clamp(entry.transform.position.x, scroller.xConstraints.min, scroller.xConstraints.max);
        if (!float.IsFinite(start) || !float.IsFinite(end)) return null;
        return new Scope { Intro = intro, Manager = manager, View = view, Map = map, Scroller = scroller,
            Camera = camera, State = state, Run = state.runData.Pointer, RunId = state.runData.runID,
            Folder = Plugin.ModFolder, Campaign = Plugin.ActiveCampaign, Start = start, End = end };
    }

    [HarmonyPostfix]
    internal static void After(WorldMapActIntro __instance, float __0)
    {
        try
        {
            if (__0 == 0 || !float.IsFinite(__0) || __instance == null || !WorldMapActIntro.IsPlaying || Plugin.IsDefaultMode)
            { current = null; return; }
            if (current != null && (current.Intro.Pointer != __instance.Pointer || !current.Valid())) current = null;
            if (current == null && __0 < Arrival) current = Bind(__instance);
            var scope = current;
            if (scope == null || !scope.Valid()) return;
            float pan = WorldMapActIntro.CameraPan.Evaluate(__0);
            if (!float.IsFinite(pan)) { current = null; return; }
            var position = scope.Camera.transform.position;
            // Read the endpoint from native's just-written interpolation, not
            // from an async value-type state-machine wrapper or a stale target.
            if (!scope.HasNativeEnd && pan > .05f)
            {
                float nativeEnd = scope.Start + (position.x - scope.Start) / pan;
                if (!float.IsFinite(nativeEnd) || nativeEnd < scope.Scroller.xConstraints.min - .02f
                    || nativeEnd > scope.Scroller.xConstraints.max + .02f) { current = null; return; }
                scope.NativeEnd = nativeEnd; scope.HasNativeEnd = true;
            }
            if (!scope.HasNativeEnd) return;
            position.x = scope.Start + (scope.End - scope.Start) * pan;
            scope.Camera.transform.position = position;
            if (__0 < Arrival) return;
            scope.Scroller.SetTargetX(scope.End);
            // Menus return to this cache. Preserve their Y/Z and zoom; only
            // update the exact current map/camera pair's horizontal destination.
            foreach (var menus in UnityEngine.Object.FindObjectsOfType<WorldMapMenusHandler>())
                if (menus != null && menus.m_Map != null && menus.m_Map.Pointer == scope.View.Pointer
                    && menus.m_Camera != null && menus.m_Camera.Pointer == scope.Camera.Pointer)
                { var saved = menus.m_CamInitialPos; saved.x = scope.End; menus.m_CamInitialPos = saved; }
            if (__0 >= Duration) scope.FinalFrame = Time.frameCount;
            if (!scope.Logged)
            {
                scope.Logged = true;
                Plugin.Log.LogInfo($"[MapIntroTarget] Fresh graph entry: run={scope.RunId}, native={scope.NativeEnd:0.00}, target={scope.End:0.00}; native pan/zoom retained.");
            }
        }
        catch (Exception ex)
        { current = null; Plugin.Log.LogWarning("[MapIntroTarget] Stopped intro correction after unresolved binding: " + ex.Message); }
    }

    // Play writes its captured endpoint once after Evaluate(Duration). Override
    // that one same-frame write only, then retire the permit synchronously.
    [HarmonyPatch(typeof(ScrollNonUI), nameof(ScrollNonUI.UpdatePosition))]
    private static class FinishPosition
    {
        [HarmonyPrefix]
        internal static void Prefix(ScrollNonUI __instance, ref float __0) => Finish(__instance, ref __0);
    }
    internal static void Finish(ScrollNonUI scroller, ref float x)
    {
        var scope = current;
        if (scope == null) return;
        try
        {
            if (!scope.Valid()) { current = null; return; }
            if (scope.FinalFrame != Time.frameCount || scroller == null || scroller.Pointer != scope.Scroller.Pointer
                || !scope.HasNativeEnd || !float.IsFinite(x) || Math.Abs(x - scope.NativeEnd) > .02f) return;
            scope.FinalFrame = -1;
            x = scope.End;
        }
        catch { current = null; }
    }

    [HarmonyPatch(typeof(MapObject), "OnDestroy")]
    private static class Destroyed
    {
        [HarmonyPostfix]
        internal static void After(MapObject __instance)
        { if (current?.View != null && __instance != null && current.View.Pointer == __instance.Pointer) current = null; }
    }
}
