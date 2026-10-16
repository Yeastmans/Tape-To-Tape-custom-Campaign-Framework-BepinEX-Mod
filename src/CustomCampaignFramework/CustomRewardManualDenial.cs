using System;
using System.Collections.Generic;
using System.Linq;
using CampaignCore;
using Events;
using HarmonyLib;
using Rogue;
using Rogue.Relics.Components;
using Rogue.Relics.Controllers;
using Tape2Tape;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EndlessMode;

internal static class CustomRewardManualDenial
{
    private sealed class Source { internal Team Team; internal GoalDenier Payload; internal GoalDenierController Controller; internal GoalDenierComponent Component; }
    private static readonly Dictionary<IntPtr, Source> sources = new();
    private static CustomRewardDefinition[] Rules(Team team) => CustomRewardCampaignRules.Owned(team?._teamData, "Opponent goal reversal window")
        .Where(r => r.Effect == CustomRewardEffects.ManualGoalDenial).ToArray();
    internal static void Refresh()
    {
        foreach (var key in sources.Where(p => p.Value.Team == null).Select(p => p.Key).ToArray()) Remove(key);
        foreach (var team in new[] { GameManager.HomeTeam, GameManager.VisitorTeam }) {
            if (team?._teamData == null) continue;
            var rules = Rules(team);
            if (team.HaveRelic<GoalDenier>() || rules.Length == 0) { Remove(team.Pointer); continue; }
            int remaining = (int)Math.Min(int.MaxValue, rules.Sum(r => (long)CustomRewardRuntime.RemainingCharges(team._teamData, r)));
            if (!sources.TryGetValue(team.Pointer, out var source)) {
                if (remaining == 0 || team.IsCpuControl()) continue;
                var original = Resources.FindObjectsOfTypeAll<GoalDenier>().FirstOrDefault(r => r.uiPrefab != null && !r.name.StartsWith("CCF_ManualDenial_", StringComparison.Ordinal));
                if (original == null) continue;
                var payload = Object.Instantiate(original); payload.name = "CCF_ManualDenial_" + team._teamData.id; payload.charges = remaining;
                source = new Source { Team = team, Payload = payload, Controller = new GoalDenierController() };
                sources.Add(team.Pointer, source);
                try {
                    source.Controller.Initialize(team, CampaignUtil.CampaignState, payload);
                    source.Component = source.Controller._goalDenierGameObject?.GetComponent<GoalDenierComponent>();
                } catch { Remove(team.Pointer); throw; }
            }
            source.Payload.charges = remaining;
        }
    }
    internal static bool Activate(GoalDenierComponent component)
    {
        var source = sources.Values.FirstOrDefault(s => s.Component?.Pointer == component.Pointer);
        if (source == null) return true;
        // Keep the native prompt/window, but do not execute its exhausted-item
        // replacement: that would remove unrelated native No Goal relics.
        if (!CustomRewardRuntime.InMatch || component._disableActionCoroutine == null
            || CustomRewardGoalConfirmation.CurrentScorer?.Team?.otherTeam != source.Team) return false;
        var rule = Rules(source.Team).FirstOrDefault(r => CustomRewardRuntime.RemainingCharges(source.Team._teamData, r) > 0);
        if (rule == null || !CustomRewardRuntime.SpendCharge(source.Team._teamData, rule)) return false;
        source.Payload.charges = Math.Max(0, source.Payload.charges - 1);
        component.DisableInput(); component.DisableUI();
        if (component._disableActionCoroutine != null) { component.StopCoroutine(component._disableActionCoroutine); component._disableActionCoroutine = null; }
        GameplayEvents.DenyGoal.Invoke();
        var playing = source.Team.PlayingForwards;
        var player = playing != null && playing.Count > 0 ? playing[0] : null;
        if (player != null) CustomRewardRuntime.Notify(rule, player);
        return false;
    }
    private static void Remove(IntPtr key)
    {
        if (!sources.TryGetValue(key, out var source)) return;
        sources.Remove(key);
        try {
            if (source.Component != null) { source.Component.Disarm(); CustomRewardGoalConfirmation.Close(source.Component); }
            source.Controller.Destroy();
        } finally {
            if (source.Controller._goalDenierGameObject != null) Object.Destroy(source.Controller._goalDenierGameObject);
            if (source.Payload != null) Object.Destroy(source.Payload);
        }
    }
    internal static void Clear() { foreach (var key in sources.Keys.ToArray()) Remove(key); }
}
[HarmonyPatch(typeof(GoalDenierComponent), "Activate")]
internal static class CustomRewardManualDenialInput
{
    [HarmonyPrefix] private static bool Prefix(GoalDenierComponent __instance) => CustomRewardManualDenial.Activate(__instance);
}
