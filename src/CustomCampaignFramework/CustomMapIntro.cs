using System;
using System.Globalization;
using HarmonyLib;
using I2.Loc;
using STS.Map;
using Tape2Tape.Hockey.UI;
using UnityEngine;

namespace EndlessMode;

// Play evaluates time zero again after loading the map and before revealing the
// title. Use that point so asynchronous loading cannot leave the previous map's
// title in place. Keep the native animation, skip input and HUD lifecycle.
[HarmonyPatch(typeof(WorldMapActIntro), "Evaluate")]
internal static class CustomMapIntro
{
    [HarmonyPostfix]
    internal static void After(WorldMapActIntro __instance, float __0)
    {
        if (__0 != 0 || Plugin.IsDefaultMode || !WorldMapActIntro.IsPlaying || __instance._title == null) return;
        try
        {
            var manager = UnityEngine.Object.FindObjectOfType<MapManager>();
            var map = manager?.CurrentMap;
            if (map == null) return;
            var layout = CustomMapLayout.LayoutFor(map);
            int stage = CustomMapLayout.StageFor(map);
            string name = layout?.Name.Trim() ?? "";
            if (name.Length == 0)
                name = LocalizationManager.GetTermTranslation($"worldmap/act{Math.Clamp(map.act, 1, 3)}_name");
            string format = LocalizationManager.GetTermTranslation("worldmap/act_title");
            if (string.IsNullOrEmpty(format)) throw new InvalidOperationException("Native act-title translation is unavailable.");
            __instance._title.text = string.Format(CultureInfo.CurrentCulture, format, stage, name);
            Plugin.Log.LogInfo($"[MapIntro] Campaign map {stage}: {name}");
        }
        catch (Exception ex) { Plugin.Log.LogError("[MapIntro] Could not apply map title: " + ex.Message); }
    }
}

[HarmonyPatch(typeof(MapObject), "OnDestroy")]
internal static class RestoreCustomMapIntroAnchor
{
    [HarmonyPostfix]
    internal static void After(MapObject __instance) => CustomMapScenery.Release(__instance);
}
