using System;
using System.Collections.Generic;
using System.Linq;
using CampaignCore;
using HarmonyLib;
using Movements;
using Rogue;
using Skaters;
using Tape2Tape;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EndlessMode;

internal static class CustomRewardEquipmentEffects
{
    private sealed class State
    {
        internal Forward Skater;
        internal bool StickForbidden;
        internal readonly Dictionary<string, int> Reserves = new();
        internal PuckRepellentComponent Repellent;
        internal bool HandMode, PreviousHandMode, Offside;
        internal readonly Il2CppSystem.Object OffsideSource = new();
    }
    private const string Source = "CCF_CustomReward_";
    private static readonly Dictionary<IntPtr, State> states = new();
    internal static void Refresh(Forward forward)
    {
        var rules = CustomRewardRuntime.ActiveConditions(forward)
            .Where(d => CustomRewardEffects.ForwardConditionEffects.Contains(d.Effect)).ToArray();
        if (!states.TryGetValue(forward.Pointer, out var state)) {
            if (rules.Length == 0) return;
            states.Add(forward.Pointer, state = new State { Skater = forward });
        }
        bool forbid = rules.Any(d => d.Effect == CustomRewardEffects.ForbidStick);
        bool hands = rules.Any(d => d.Effect == CustomRewardEffects.HandPuck) && !forward.HasStick;
        if (hands != state.HandMode) {
            if (hands) { state.PreviousHandMode = forward.actAsReferee; forward.actAsReferee = true; }
            else RestoreHands(state);
            state.HandMode = hands;
        }
        bool offside = !forward.IsBenched() && rules.Any(d => d.Effect == CustomRewardEffects.ForceOffside);
        if (offside != state.Offside) {
            state.Offside = offside;
            if (offside) OffsideController.AddForceSource(state.OffsideSource);
            else OffsideController.RemoveForceSource(state.OffsideSource);
        }
        if (forbid != state.StickForbidden) {
            state.StickForbidden = forbid;
            if (forbid) forward.AddForbidStickSource(Source + "NoStick");
            else forward.RemoveForbidStickSource(Source + "NoStick");
        }
        var reserves = new Dictionary<string, int>();
        foreach (var rule in rules) if (rule.Effect == CustomRewardEffects.ExtraSticks) {
            reserves.TryGetValue(rule.Id, out int count); reserves[rule.Id] = count + rule.Amount;
        }
        foreach (var id in state.Reserves.Keys.Where(id => !reserves.ContainsKey(id)).ToArray()) {
            forward.RemoveStickCountContributor(Source + id); state.Reserves.Remove(id);
        }
        foreach (var pair in reserves) if (!state.Reserves.TryGetValue(pair.Key, out int count) || count != pair.Value) {
            forward.AddStickCountContributor(pair.Value, Source + pair.Key); state.Reserves[pair.Key] = pair.Value;
        }
        bool repel = rules.Any(d => d.Effect == CustomRewardEffects.RepelPuck) && !forward.HaveTalentCached<PuckRepellent>();
        if (repel && state.Repellent == null) state.Repellent = forward.gameObject.AddComponent<PuckRepellentComponent>();
        else if (!repel && state.Repellent != null) {
            state.Repellent.enabled = false; Object.Destroy(state.Repellent); state.Repellent = null;
        }
    }
    internal static void Clear()
    {
        foreach (var state in states.Values) if (state.Skater != null) {
            if (state.HandMode) RestoreHands(state);
            if (state.StickForbidden) state.Skater.RemoveForbidStickSource(Source + "NoStick");
            foreach (string id in state.Reserves.Keys) state.Skater.RemoveStickCountContributor(Source + id);
            if (state.Repellent != null) { state.Repellent.enabled = false; Object.Destroy(state.Repellent); }
        }
        foreach (var state in states.Values) if (state.Offside) OffsideController.RemoveForceSource(state.OffsideSource);
        states.Clear();
    }
    private static void RestoreHands(State state)
    {
        // A native RefHands grant received later still owns this flag.
        if (state.Skater.actAsReferee && !state.Skater.HaveTalentCached<RefHands>()) state.Skater.actAsReferee = state.PreviousHandMode;
    }
}

[HarmonyPatch(typeof(MovementManager), "CanBodycheck")]
internal static class CustomRewardNoBodycheck
{
    [HarmonyPostfix] private static void Postfix(MovementManager __instance, ref bool __result)
    {
        if (__result && CustomRewardRuntime.ActiveConditions(__instance?._skater).Any(d => d.Effect == CustomRewardEffects.ForbidCheck)) __result = false;
    }
}
