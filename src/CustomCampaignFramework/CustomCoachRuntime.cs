using System;
using System.Collections.Generic;
using System.Linq;
using CampaignCore;
using HarmonyLib;
using Rogue.NPC.CoachNPC;
using STS.Map;
using Tape2Tape;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EndlessMode;

internal static class CustomCoachRuntime
{
    private sealed class Visit
    {
        internal MapNode Owner;
        internal MapObject Map;
        internal IntPtr Run;
        internal IntPtr Graph;
        internal string Folder;
        internal CoachEvent Event;
        internal CustomCoachDefinition Coach;
        internal bool NativeSkip;
    }
    private static readonly Dictionary<IntPtr, Visit> visits = new();
    internal static CustomCoachDefinition For(MapNode node)
    {
        if (Plugin.IsDefaultMode || node?.Node == null) return null;
        var map = node.GetComponentInParent<MapObject>()?.mapManager?.CurrentMap;
        var layout = map == null ? null : CustomMapLayout.LayoutFor(map);
        var data = layout?.InLayer(node.Node.LayerIndex).ElementAtOrDefault(node.Node.NodeIndex);
        return data?.Type == "Coach" ? data.CustomCoach : null;
    }
    internal static void Bind(CoachMapNode node, ref EventNode source)
    {
        var coach = For(node); if (coach == null) return;
        // Keep the prefab's event untouched: the native menu can initialize it
        // asynchronously, reroll it, and ask it for bonus choices after launch.
        var old = visits.Values.FirstOrDefault(v => v.Owner != null && v.Owner.Pointer == node.Pointer);
        if (old != null) { source = old.Event; return; }
        var clone = Object.Instantiate(source).TryCast<CoachEvent>();
        if (clone == null) throw new InvalidOperationException("Custom Coach requires a native Coach event.");
        clone.name = "CCF_CustomCoach_" + coach.Id;
        visits[clone.Pointer] = new Visit { Owner = node, Map = node.GetComponentInParent<MapObject>(),
            Graph = node.GetComponentInParent<MapObject>()?.mapManager?.CurrentMap?.Pointer ?? IntPtr.Zero,
            Run = CampaignUtil.CampaignState?.runData?.Pointer ?? IntPtr.Zero, Folder = Plugin.ModFolder,
            Event = clone, Coach = coach.Copy(), NativeSkip = clone.canSkip };
        source = clone;
    }
    private static Visit Current(EventNode source)
    {
        if (Plugin.IsDefaultMode || source == null || !visits.TryGetValue(source.Pointer, out var visit)
            || visit.Owner == null || visit.Map == null || visit.Folder != Plugin.ModFolder
            || visit.Graph != visit.Map.mapManager?.CurrentMap?.Pointer
            || visit.Run != CampaignUtil.CampaignState?.runData?.Pointer) return null;
        return visit;
    }
    internal static bool Empty(EventNode source) => Current(source) != null && (source.eventChoices == null || source.eventChoices.Count == 0);
    internal static bool EmptyFor(MapNode owner) => visits.Values.Any(v => v.Owner != null && owner != null && v.Owner.Pointer == owner.Pointer && Empty(v.Event));
    internal static bool Begin(CoachEvent source, out CustomCoachScope scope)
    {
        var visit = Current(source);
        scope = new CustomCoachScope(visit?.Coach);
        if (visit == null) return true;
        source.canSkip = visit.NativeSkip;
        try { CustomRewardRuntime.RegisterCoach(visit.Coach); return true; }
        catch (Exception ex) {
            source.eventChoices = new Il2CppSystem.Collections.Generic.List<EventChoice>();
            source.canSkip = true;
            Plugin.Log.LogError("[CustomCoach] Cannot prepare this node's rewards; offering a safe exit: " + ex);
            return false;
        }
    }
    internal static void Finish(CoachEvent source)
    {
        if (Current(source) == null) return;
        if (source.eventChoices == null || source.eventChoices.Count == 0) source.canSkip = true;
    }
    internal static void Release(MapObject map)
    {
        foreach (var entry in visits.Where(p => p.Value.Map == null || p.Value.Map.Pointer == map.Pointer).ToArray()) {
            visits.Remove(entry.Key); if (entry.Value.Event != null) Object.Destroy(entry.Value.Event);
        }
    }
}

[HarmonyPatch(typeof(CoachBlueprint), nameof(CoachBlueprint.showEventPanel))]
internal static class BindCustomCoachEvent
{
    [HarmonyPrefix]
    private static bool Prefix(CoachMapNode __0, ref EventNode __1)
    {
        try { CustomCoachRuntime.Bind(__0, ref __1); return true; }
        catch (Exception ex) { Plugin.Log.LogError("[CustomCoach] Could not open custom Coach: " + ex); return false; }
    }
}
[HarmonyPatch(typeof(CoachEvent), nameof(CoachEvent.Initialize))]
internal static class InitializeCustomCoach
{
    [HarmonyPrefix] private static bool Prefix(CoachEvent __instance, out CustomCoachScope __state) => CustomCoachRuntime.Begin(__instance, out __state);
    [HarmonyPostfix] private static void Postfix(CoachEvent __instance) => CustomCoachRuntime.Finish(__instance);
    [HarmonyFinalizer] private static Exception Finalizer(CustomCoachScope __state, Exception __exception) { __state?.Dispose(); return __exception; }
}
[HarmonyPatch(typeof(MapObject), "OnDestroy")]
internal static class ReleaseCustomCoaches
{
    [HarmonyPrefix] private static void Prefix(MapObject __instance) { CustomCoachRuntime.Release(__instance); CustomCoachPresentation.Release(__instance); }
}
