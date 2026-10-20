using System.Collections.Generic;
using System.Linq;
using CampaignCore;
using HarmonyLib;
using Rogue.Powerups.Helpers;
using Skaters;

namespace EndlessMode;

internal static class CustomRewardAttraction
{
    private sealed class State
    {
        internal Forward Skater;
        internal Skater Owner;
        internal CustomRewardDefinition Rule;
        internal PuckAttractor Attractor;
    }
    private static readonly Dictionary<string, State> active = new();
    // The puck is steered toward the recipient; ownership is checked on the
    // owning skater, which differs when an opposing skater is the target.
    internal static bool Apply(CustomRewardDefinition rule, Skater owner, Skater recipient)
    {
        var forward = recipient?.TryCast<Forward>();
        if (forward == null || owner?._skaterData == null || !CustomRewardRuntime.InMatch) return false;
        string key = rule.Id + "|" + owner.Pointer + "|" + forward.Pointer;
        if (!active.TryGetValue(key, out var state)) {
            var attractor = new PuckAttractor(); attractor.Initialize();
            state = new State { Skater = forward, Owner = owner, Rule = rule, Attractor = attractor };
            active.Add(key, state);
        }
        if (!state.Attractor.CanAttract(forward, rule.Radius)) return false;
        state.Attractor.StartAttracting(forward, rule.Radius, rule.Seconds, rule.Amount / 100f);
        return state.Attractor._isAttracting;
    }
    internal static void FixedUpdate(Skater skater)
    {
        CustomRewardLooseAttraction.Tick(skater?.TryCast<Forward>());
        foreach (var pair in active.Where(p => p.Value.Skater == skater).ToArray()) {
            var state = pair.Value; var source = state.Owner;
            bool owned = source != null && source.ArePowerUpsEnabled() && CustomRewardRuntime.Has(source._skaterData, state.Rule.Id)
                && source._skaterData.powerups.HasMatch(t => t != null && t.id == state.Rule.Id && !t.PayoffSuppressed);
            if (CustomRewardRuntime.InMatch && owned) state.Attractor.Update();
            else state.Attractor.StopAttracting();
            if (!state.Attractor._isAttracting) { state.Attractor.OnDestroy(); active.Remove(pair.Key); }
        }
    }
    internal static void Clear()
    {
        foreach (var state in active.Values) state.Attractor.OnDestroy();
        active.Clear();
    }
}

[HarmonyPatch(typeof(Skater), "FixedUpdate")]
internal static class CustomRewardAttractionUpdate
{
    [HarmonyPostfix] private static void Postfix(Skater __instance) => CustomRewardRuntime.Safe(() => CustomRewardAttraction.FixedUpdate(__instance));
}
