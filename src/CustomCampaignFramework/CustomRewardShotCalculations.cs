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
        int total = 0;
        foreach (var rule in CustomRewardRuntime.Definitions.Values) {
            if (rule.NeedsRedesign || rule.Trigger != trigger || rule.Effect != CustomRewardEffects.StatChange) continue;
            bool Owns(Skater source) => source != null && source.ArePowerUpsEnabled() && CustomRewardRuntime.Has(source._skaterData, rule.Id)
                && source._skaterData.powerups.HasMatch(t => t != null && t.id == rule.Id && !t.PayoffSuppressed);
            if (rule.IsRelic) { if (CustomRewardRuntime.Has(recipient.Team._teamData, rule.Id)) total += rule.Amount; }
            else if (rule.Target == CustomRewardEffects.Actor) { if (Owns(recipient)) total += rule.Amount; }
            else {
                var team = recipient.Team.PlayingForwards;
                for (int i = 0; team != null && i < team.Count; i++) if (Owns(team[i])) total += rule.Amount;
            }
        }
        return total;
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
