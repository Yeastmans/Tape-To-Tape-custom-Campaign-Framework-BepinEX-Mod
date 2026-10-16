using System;
using System.Collections.Generic;
using System.Linq;
using CampaignCore;
using HarmonyLib;
using Rogue;
using Rogue.Powerups.AI;
using Rogue.Powerups.Components;
using Rogue.Relics.Components;
using Skaters;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EndlessMode;

internal static class CustomRewardFartTimers
{
    private sealed class Timer
    {
        internal Forward Owner;
        internal CustomRewardDefinition Rule;
        internal GameObject Child;
        internal DiarrheaAI Native;
    }
    private static readonly Dictionary<string, Timer> timers = new();
    internal static Relic CreateRelic(CustomRewardDefinition d)
    {
        var template = Resources.FindObjectsOfTypeAll<FartingPuck>().FirstOrDefault(t => t != null && !CustomRewardRuntime.IsCustom(t.id));
        if (template == null) return null;
        var relic = Object.Instantiate(template);
        relic.fartInterval = d.Seconds; relic.forceFactor = d.Amount / 100f;
        relic.maxRangeFactor = d.Radius / (d.Effect == CustomRewardEffects.PushSkaters ? 4.5f : 7f);
        return relic;
    }
    internal static void Refresh(Forward owner)
    {
        var active = CustomRewardRuntime.Definitions.Values.Where(d => !d.NeedsRedesign && !d.IsRelic
            && d.Trigger == "Random fart timer" && CustomRewardRuntime.InMatch && owner.ArePowerUpsEnabled() && !owner.IsBenched()
            && CustomRewardRuntime.Has(owner._skaterData, d.Id)
            && owner._skaterData.powerups.HasMatch(t => t != null && t.id == d.Id && !t.PayoffSuppressed)).ToArray();
        foreach (var pair in timers.Where(p => p.Value.Owner == owner && !active.Any(d => d.Id == p.Value.Rule.Id)).ToArray()) Remove(pair.Key);
        foreach (var d in active) {
            string key = owner.Pointer + "|" + d.Id;
            if (timers.ContainsKey(key)) continue;
            var template = Resources.FindObjectsOfTypeAll<Diarrhea>().FirstOrDefault();
            if (template?.fartParticle == null) continue;
            // A child avoids occupying the native talent's GetComponent slot.
            // Each custom item owns its timer, wave, event subscriptions and cleanup.
            var child = new GameObject("CCF_FartTimer_" + d.Id);
            try {
            child.transform.SetParent(owner.transform, false);
            var wave = child.AddComponent<FartObject>();
            wave.magnetoPlayer = owner;
            wave.Initialize(new FartConfiguration {
                ShouldPushForwards = d.Effect == CustomRewardEffects.PushSkaters,
                ShouldPushPucks = d.Effect == CustomRewardEffects.PushPuck,
                ShouldPushSticks = d.Effect == CustomRewardEffects.PushSticks,
                ShouldPropulse = false, ForceFactor = d.Amount / 100f,
                MaxRangeFactor = d.Radius / (d.Effect == CustomRewardEffects.PushSkaters ? 4.5f : 7f)
            }, template.fartParticle, owner);
            var native = child.AddComponent<DiarrheaAI>(); native.Initialize(wave);
            var timer = new Timer { Owner = owner, Rule = d, Child = child, Native = native };
            SetInterval(timer); timers.Add(key, timer);
            } catch { child.SetActive(false); Object.Destroy(child); throw; }
        }
    }
    private static void SetInterval(Timer timer)
        => timer.Native._timeBetweenFart = UnityEngine.Random.Range(timer.Rule.Seconds, timer.Rule.IntervalMaxSeconds);
    internal static void ResetInterval(DiarrheaAI native)
    {
        var timer = timers.Values.FirstOrDefault(t => t.Native?.Pointer == native.Pointer);
        if (timer != null) SetInterval(timer);
    }
    private static void Remove(string key)
    {
        if (timers.TryGetValue(key, out var timer) && timer.Child != null) {
            timer.Child.SetActive(false); Object.Destroy(timer.Child);
        }
        timers.Remove(key);
    }
    internal static void Clear() { foreach (string key in timers.Keys.ToArray()) Remove(key); }
}

[HarmonyPatch(typeof(DiarrheaAI), "Fart")]
internal static class CustomRewardFartInterval
{
    // Native Fart inlines its random reset. Override the resulting value here,
    // after native activation and dispatch, instead of patching the unused helper.
    [HarmonyPostfix] private static void Postfix(DiarrheaAI __instance) => CustomRewardFartTimers.ResetInterval(__instance);
}

[HarmonyPatch(typeof(FartEffectUtility), nameof(FartEffectUtility.Execute))]
internal static class CustomRewardPuckFartComponents
{
    [HarmonyPrefix] private static void Prefix(MonoBehaviour owner, ref FartConfiguration fartConfiguration)
    {
        var component = owner?.TryCast<FartingPuckComponent>();
        if (component?._relic == null || !CustomRewardRuntime.Definitions.TryGetValue(component._relic.id, out var d)
            || d.Trigger != "Puck fart timer") return;
        fartConfiguration.ShouldPushForwards = d.Effect == CustomRewardEffects.PushSkaters;
        fartConfiguration.ShouldPushSticks = d.Effect == CustomRewardEffects.PushSticks;
        fartConfiguration.ShouldPushPucks = false; fartConfiguration.ShouldPropulse = false;
    }
}
