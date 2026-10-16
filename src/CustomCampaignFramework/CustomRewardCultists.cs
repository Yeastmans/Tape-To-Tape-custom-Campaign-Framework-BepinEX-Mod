using System;
using System.Collections.Generic;
using System.Linq;
using AI;
using CampaignCore;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Rogue;
using Skaters;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EndlessMode;

// One native host owns the spell cycle. Rebuilding its available spell list
// avoids multiple hosts reacting to the same possession/knockout events.
internal static class CustomRewardCultists
{
    private sealed class State
    {
        internal CultistAI Host;
        internal bool Created;
        internal string Signature = "";
        internal string NativeSignature = "";
        internal BossAI[] Original = Array.Empty<BossAI>();
        internal readonly List<BossAI> Added = new();
    }
    private static readonly Dictionary<IntPtr, State> states = new();
    internal static void Refresh(Forward forward)
    {
        if (forward?._forwardData == null) return;
        states.TryGetValue(forward.Pointer, out var state);
        CustomRewardRuntime.ObserveOwnership(forward.Team?._teamData);
        var owned = forward._forwardData.powerups?.ToArray() ?? Array.Empty<Talent>();
        var rules = owned.Where(t => t != null && !t.PayoffSuppressed)
            .Select(t => CustomRewardRuntime.Definitions.TryGetValue(t.id, out var d) ? d : null)
            .Where(d => d != null && !d.NeedsRedesign && CustomRewardEffects.IsCultist(d.Effect))
            .OrderByDescending(d => CustomRewardRuntime.Acquired(forward._skaterData, d)).ToArray();
        if (forward.IsBenched() || !forward.ArePowerUpsEnabled()) { rules = Array.Empty<CustomRewardDefinition>(); owned = Array.Empty<Talent>(); }
        if (rules.Length == 0 && state == null) return;
        // Frog and freeze are complementary native spells. Conflicting custom
        // forms use the last acquired rule, like the other custom AI choices.
        var selected = rules.Where(d => d.Effect == CustomRewardEffects.CultistFrog || d.Effect == CustomRewardEffects.CultistFreeze)
            .Concat(rules.Where(d => d.Effect != CustomRewardEffects.CultistFrog && d.Effect != CustomRewardEffects.CultistFreeze).Take(1))
            .Select(d => CustomRewardEffects.CultistAsset(d.Effect)).Distinct().ToArray();
        string signature = string.Join("|", selected);
        var host = forward.gameObject.GetComponent<CultistAI>();
        if (state != null && state.Host != host) { CleanupAdded(state); states.Remove(forward.Pointer); state = null; }
        if (state == null) {
            state = new State { Host = host, Created = host == null };
            if (host == null) host = state.Host = forward.gameObject.AddComponent<CultistAI>();
            state.Original = host._availableAIs == null ? Array.Empty<BossAI>() : host._availableAIs.ToArray().Take(host._availableAIsCount).Where(p => p != null).ToArray();
            states[forward.Pointer] = state;
            state.NativeSignature = state.Created ? "<initialize>" : NativeSignature(owned);
        }
        string nativeSignature = NativeSignature(owned);
        if (state.NativeSignature != nativeSignature) {
            foreach (var power in state.Original) ResetPower(state, power);
            state.Original = NativePowers(owned).Select(p => p.Generate(forward)).Where(p => p != null).ToArray();
            state.NativeSignature = nativeSignature; state.Signature = "<refresh>";
        }
        if (rules.Length == 0) {
            if (state.Signature.Length > 0) { CleanupAdded(state); SetPowers(host, state.Original); state.Signature = ""; }
            if (state.Original.Length == 0) Restore(forward.Pointer, state);
            return;
        }
        if (state.Signature == signature) return;
        CleanupAdded(state);
        var templates = Resources.FindObjectsOfTypeAll<CultistPowerup>();
        foreach (var id in selected) {
            var template = templates.FirstOrDefault(p => p != null && p.id == id);
            if (template == null) { Plugin.Log.LogWarning("[CustomRewards] Missing native incantation " + id); continue; }
            var power = template.Generate(forward); if (power != null) state.Added.Add(power);
        }
        SetPowers(host, state.Original.Concat(state.Added).ToArray());
        var frog = state.Added.Select(p => p.TryCast<FroggingCultistAI>()).FirstOrDefault(p => p != null);
        var freeze = state.Added.Select(p => p.TryCast<FrozingCultistAI>()).FirstOrDefault(p => p != null);
        if (frog != null && freeze != null) { frog.SetFreezePartner(freeze); freeze.SetFrogPartner(frog); }
        state.Signature = signature;
    }
    private static void SetPowers(CultistAI host, BossAI[] powers)
    {
        if (host == null) return;
        host._availableAIs = new Il2CppReferenceArray<BossAI>(Math.Max(8, powers.Length));
        host._eligibleAIs = new Il2CppReferenceArray<BossAI>(Math.Max(8, powers.Length));
        host._availableAIsCount = host._eligibleAIsCount = 0;
        foreach (var power in powers) host.AddPower(power);
        var frog = powers.Select(p => p.TryCast<FroggingCultistAI>()).FirstOrDefault(p => p != null);
        var freeze = powers.Select(p => p.TryCast<FrozingCultistAI>()).FirstOrDefault(p => p != null);
        if (frog != null && freeze != null) { frog.SetFreezePartner(freeze); freeze.SetFrogPartner(frog); }
    }
    private static void CleanupAdded(State state)
    {
        foreach (var power in state.Added) ResetPower(state, power);
        state.Added.Clear();
    }
    private static List<CultistPowerup> NativePowers(Talent[] owned)
    {
        var result = new List<CultistPowerup>(); var ids = new HashSet<string>();
        foreach (var talent in owned) {
            if (talent == null || talent.PayoffSuppressed) continue;
            var cultist = talent.TryCast<Cultist>();
            if (cultist?.cultistPowerups == null) continue;
            foreach (var power in cultist.cultistPowerups)
                if (power != null && ids.Add(power.id)) result.Add(power);
        }
        return result;
    }
    private static string NativeSignature(Talent[] owned) => string.Join("|", NativePowers(owned).Select(p => p.id).OrderBy(id => id));
    private static void ResetPower(State state, BossAI power)
    {
            if (state.Host != null && state.Host.currentAI?.Pointer == power.Pointer) {
                state.Host.CancelIncantation(); state.Host.currentAI = null;
            }
            power.TryCast<TransformationCultistAI>()?.ForceRevertNow();
            power.OnReset();
    }
    private static void Restore(IntPtr key, State state)
    {
        CleanupAdded(state);
        if (state.Host != null) {
            if (state.Created) { state.Host.enabled = false; Object.Destroy(state.Host); }
            else SetPowers(state.Host, state.Original);
        }
        states.Remove(key);
    }
    internal static void Clear()
    {
        foreach (var pair in states.ToArray()) Restore(pair.Key, pair.Value);
    }
}
