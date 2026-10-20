using System;
using System.Collections.Generic;
using System.Reflection;
using CampaignCore;
using HarmonyLib;
using Skaters;

namespace EndlessMode;

internal static class CustomRewardShotCalculations
{
    [ThreadStatic] internal static string Current;
    [ThreadStatic] internal static Forward Shooter;
    internal static int Bonus(Skater recipient, string stat) => recipient != null && Shooter?.Pointer == recipient.Pointer && stat == "Shot Power"
        ? Amount(recipient, Current) : 0;
    internal static int ForCrit(Forward recipient, ShotType type) => Amount(recipient, type switch {
        ShotType.Slapshot => "Slapshot crit calculation", ShotType.Wristshot => "Wrist-shot crit calculation",
        ShotType.OneTimer => "One-timer crit calculation", _ => ""
    });
    private static int Amount(Skater recipient, string trigger)
    {
        if (!CustomRewardRuntime.InMatch || recipient?.Team == null || string.IsNullOrEmpty(trigger)) return 0;
        long total = 0;
        // Each on-ice owner whose rule reaches this shooter contributes once:
        // the owner itself, its teammates, or opposing shooters (all of them,
        // nearby, closest, the carrier or a per-match random selection).
        foreach (var rule in CustomRewardRuntime.Definitions.Values) {
            if (rule.NeedsRedesign || rule.Trigger != trigger || rule.Effect != CustomRewardEffects.StatChange) continue;
            foreach (var _ in CustomRewardRuntime.ContinuousSources(rule, recipient, _ => true)) total += rule.Amount;
        }
        return (int)Math.Clamp(total, int.MinValue, int.MaxValue);
    }
}

[HarmonyPatch]
internal static class CustomRewardShotPowerScope
{
    private static IEnumerable<MethodBase> TargetMethods() {
        yield return AccessTools.Method(typeof(ForwardStats), "GetSlapShotPower");
        yield return AccessTools.Method(typeof(ForwardStats), "GetWristShotPower");
    }
    private sealed class Scope { internal string Trigger; internal Forward Shooter; }
    [HarmonyPrefix] private static void Prefix(ForwardStats __instance, MethodBase __originalMethod, out Scope __state) {
        __state = new Scope { Trigger = CustomRewardShotCalculations.Current, Shooter = CustomRewardShotCalculations.Shooter };
        CustomRewardShotCalculations.Current = __originalMethod.Name == "GetSlapShotPower" ? "Slapshot power calculation" : "Wrist-shot power calculation";
        CustomRewardShotCalculations.Shooter = __instance._forward;
    }
    [HarmonyFinalizer] private static void Finalizer(Scope __state) {
        if (__state == null) return;
        CustomRewardShotCalculations.Current = __state.Trigger; CustomRewardShotCalculations.Shooter = __state.Shooter;
    }
}
