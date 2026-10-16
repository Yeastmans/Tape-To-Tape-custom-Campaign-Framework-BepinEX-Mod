using System;
using System.Collections.Generic;
using System.Linq;
using CampaignCore;
using HarmonyLib;
using Skaters;
using UnityEngine;

namespace EndlessMode;

internal static class CustomRewardMovementEffects
{
    private sealed class Pending
    {
        internal CustomRewardDefinition Rule;
        internal Skater Source;
        internal Forward Recipient;
        internal float FixedTime;
    }
    private static readonly Dictionary<string, Pending> pending = new();
    internal static bool Apply(CustomRewardDefinition rule, Skater source, Forward recipient)
    {
        if (recipient?.movementManager?.MovementStateManager == null || !recipient.IsVulnerable()) return false;
        if (!CustomRewardTriggers.HasShotFlow(rule.Trigger)) return Set(rule, recipient);
        // Puck.Shot returns inside the shooter's movement-state transition.
        // Native IceSlapshot waits for physics before freezing, otherwise that
        // transition immediately overwrites the frozen state. Finish this same
        // shot's transition; never retain a rule for some later action/match.
        pending[rule.Id + "|" + source._skaterData.id + "|" + recipient._skaterData.id] = new Pending {
            Rule = rule, Source = source, Recipient = recipient, FixedTime = Time.fixedTime
        };
        return true;
    }
    private static bool Set(CustomRewardDefinition rule, Forward recipient)
    {
        if (recipient?.movementManager?.MovementStateManager is not { } movement || !recipient.IsVulnerable()) return false;
        if (rule.Effect == CustomRewardEffects.Dance) movement.SetDancingFreezeState(rule.Seconds);
        else {
            movement.SetFreezingState();
            // The inspected native state thaws at timeFrozen == 10 and advances
            // this timer only while play runs, preserving native pause behavior.
            movement._freezingState.timeFrozen = 10 - rule.Seconds;
        }
        return true;
    }
    internal static void AfterPhysics(Forward recipient)
    {
        foreach (var pair in pending.Where(p => p.Value.Recipient == recipient && p.Value.FixedTime < Time.fixedTime).ToArray()) {
            pending.Remove(pair.Key);
            var effect = pair.Value; var source = effect.Source;
            if (!CustomRewardRuntime.InMatch || source == null || recipient == null || !recipient.gameObject.activeInHierarchy) continue;
            bool owned = effect.Rule.IsRelic ? CustomRewardRuntime.Has(source.Team?._teamData, effect.Rule.Id)
                : source.ArePowerUpsEnabled() && CustomRewardRuntime.Has(source._skaterData, effect.Rule.Id)
                    && source._skaterData.powerups.HasMatch(t => t != null && t.id == effect.Rule.Id && !t.PayoffSuppressed);
            if (owned) Set(effect.Rule, recipient);
        }
    }
    internal static void Clear() => pending.Clear();
}

[HarmonyPatch(typeof(Skater), "FixedUpdate")]
internal static class CustomRewardMovementAfterShot
{
    [HarmonyPostfix] private static void Postfix(Skater __instance) {
        if (__instance?.TryCast<Forward>() is { } forward) CustomRewardRuntime.Safe(() => CustomRewardMovementEffects.AfterPhysics(forward));
    }
}
