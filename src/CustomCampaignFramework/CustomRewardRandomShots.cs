using System;
using System.Collections.Generic;
using System.Linq;
using CampaignCore;
using HarmonyLib;
using Rogue;
using Rogue.Powerups.Controllers.Talents;
using Skaters;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EndlessMode;

// Preserve native pre-shot roll timing and downstream attribute calculations.
// A scoped capability selects only the authored half of Gambler's bundle.
internal static class CustomRewardRandomShots
{
    [ThreadStatic] internal static string Stat;
    private static readonly Dictionary<IntPtr, GamblerController> controllers = new();
    internal static CustomRewardDefinition Rule(Skater player) => CustomRewardRuntime.ActiveConditions(player)
        .Where(d => d.Effect == CustomRewardEffects.RandomShotPower && (Stat == null || Stat == "Power")
            || d.Effect == CustomRewardEffects.RandomShotAccuracy && (Stat == null || Stat == "Accuracy"))
        .OrderByDescending(d => d.Amount).FirstOrDefault();
    internal static GamblerController Controller(Forward player)
    {
        if (!controllers.TryGetValue(player.Pointer, out var controller)) {
            var payload = ScriptableObject.CreateInstance<Gambler>(); payload.name = "CCF_RandomShot_" + player._forwardData.id;
            controller = new GamblerController { skater = player, Talent = payload };
            controllers.Add(player.Pointer, controller);
        }
        var effects = CustomRewardRuntime.ActiveConditions(player).ToArray();
        var asset = controller.Talent.Cast<Gambler>();
        asset.minShotPower = effects.Where(d => d.Effect == CustomRewardEffects.RandomShotPower).Select(d => d.Amount).DefaultIfEmpty(0).Max();
        asset.minAccuracy = effects.Where(d => d.Effect == CustomRewardEffects.RandomShotAccuracy).Select(d => d.Amount).DefaultIfEmpty(0).Max();
        return controller;
    }
    internal static bool Roll(GamblerController controller, string effect) => !controllers.Values.Any(c => c.Pointer == controller.Pointer)
        || CustomRewardCapabilities.Active(controller.skater, effect);
    internal static void Clear()
    {
        foreach (var controller in controllers.Values) if (controller.Talent != null) Object.Destroy(controller.Talent);
        controllers.Clear(); Stat = null;
    }
}
// Keep scopes on these scalar-signature callers. Native GetOverall receives
// Nullable<BasicStats> packed by value; the generated Harmony bridge treats it
// as a pointer and crashes in Memmove. Native GetOverall already excludes
// checking/speed from Gambler queries, so it does not need its own scope hook.
[HarmonyPatch(typeof(ForwardStats), "GetAccuracy")]
internal static class CustomRewardRandomAccuracyScope
{
    [HarmonyPrefix] private static void Prefix(out string __state) { __state = CustomRewardRandomShots.Stat; CustomRewardRandomShots.Stat = "Accuracy"; }
    [HarmonyFinalizer] private static void Finalizer(string __state) => CustomRewardRandomShots.Stat = __state;
}
[HarmonyPatch(typeof(ForwardStats), "GetTotalShotPowerStats")]
internal static class CustomRewardRandomPowerScope
{
    [HarmonyPrefix] private static void Prefix(out string __state) { __state = CustomRewardRandomShots.Stat; CustomRewardRandomShots.Stat = "Power"; }
    [HarmonyFinalizer] private static void Finalizer(string __state) => CustomRewardRandomShots.Stat = __state;
}
[HarmonyPatch(typeof(GamblerController), "GenerateAccuracy")]
internal static class CustomRewardRandomAccuracyRoll
{
    [HarmonyPrefix] private static bool Prefix(GamblerController __instance) => CustomRewardRandomShots.Roll(__instance, CustomRewardEffects.RandomShotAccuracy);
}
[HarmonyPatch(typeof(GamblerController), "GenerateShotPower")]
internal static class CustomRewardRandomPowerRoll
{
    [HarmonyPrefix] private static bool Prefix(GamblerController __instance) => CustomRewardRandomShots.Roll(__instance, CustomRewardEffects.RandomShotPower);
}
