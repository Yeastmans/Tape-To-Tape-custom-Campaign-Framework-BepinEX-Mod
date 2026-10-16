using System;
using System.Collections.Generic;
using System.Linq;
using CampaignCore;
using HarmonyLib;
using Skaters;

namespace EndlessMode;

internal static class CustomRewardScaling
{
    private sealed class Binding { internal Forward Player; internal string Stat; }
    private static readonly Dictionary<IntPtr, Binding> bindings = new();
    internal static void Bind(Forward player, string stat, Il2CppSystem.Collections.Generic.List<Il2CppSystem.Func<int, int>> modifiers)
        => bindings[modifiers.Pointer] = new Binding { Player = player, Stat = stat };
    internal static void Clear() => bindings.Clear();
    internal static double Multiplier(Skater player, string effect, string stat = null)
    {
        double multiplier = 1;
        foreach (var d in CustomRewardRuntime.ActiveConditions(player))
            if (d.Effect == effect && (stat == null || d.Stat == stat)) multiplier *= d.Amount / 100d;
        return multiplier;
    }
    internal static int Scale(int initial, int final, Il2CppSystem.Collections.Generic.List<Il2CppSystem.Func<int, int>> modifiers)
    {
        if (modifiers == null || !bindings.TryGetValue(modifiers.Pointer, out var binding) || binding.Player == null || binding.Player.IsReferee()) return final;
        double multiplier = Multiplier(binding.Player, CustomRewardEffects.BonusMultiplier, binding.Stat);
        // GetStats has already evaluated every native/custom additive modifier.
        // Native injury/fatigue and Original equipment scaling run afterwards.
        // Matching native rounding truncates the extra bonus toward zero.
        return (int)Math.Clamp(final + Math.Truncate(((double)final - initial) * (multiplier - 1)), 0, int.MaxValue);
    }
}

// AddOriginalRelicModifier is inlined by IL2CPP. GetStats is a verified direct
// call in each attribute path and gives us the initial value and exact list.
[HarmonyPatch(typeof(ForwardStats), "GetStats")]
internal static class CustomRewardBonusScaling
{
    [HarmonyPostfix] private static void Postfix(int __0, Il2CppSystem.Collections.Generic.List<Il2CppSystem.Func<int, int>> __1, ref int __result)
        => __result = CustomRewardScaling.Scale(__0, __result, __1);
}
