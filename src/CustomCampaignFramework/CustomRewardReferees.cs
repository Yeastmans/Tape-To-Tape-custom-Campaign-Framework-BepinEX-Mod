using System;
using System.Collections.Generic;
using System.Linq;
using CampaignCore;
using HarmonyLib;
using Skaters;
using State.GameModeControllers.NormalMatch;
using Tape2Tape;
using UnityEngine;

namespace EndlessMode;

internal static class CustomRewardReferees
{
    private sealed class State
    {
        internal Referee Referee;
        internal bool WasBlocking, ChangedBlocking;
        internal Team PreviousTeam, AppliedTeam;
        internal bool AppliedBlocking, WasPursuing, AppliedPursuing, ChangedPursuing, AddedTargetTimer;
        internal Team PreviousPursuitTeam, AppliedPursuitTeam;
        internal Forward PreviousTarget;
        internal Il2CppSystem.Object SizeSource = new();
        internal bool ChangedSize;
        internal bool ChangedRecruitment, WasOnTeam, WasControllable;
        internal Team PreviousOwner, AppliedOwner;
    }
    private static readonly Dictionary<IntPtr, State> states = new();
    private static string appliedRules;
    private static readonly HashSet<IntPtr> started = new();
    private static (Team Team, CustomRewardDefinition Rule) Latest(string effect)
    {
        (Team Team, CustomRewardDefinition Rule) result = default; long priority = long.MinValue;
        foreach (var team in new[] { GameManager.HomeTeam, GameManager.VisitorTeam }) if (team?._teamData != null)
            foreach (var rule in CustomRewardCampaignRules.Owned(team._teamData, "Match setup").Where(d => d.Effect == effect)) {
                long acquired = CustomRewardRuntime.Acquired(team._teamData, rule);
                if (acquired >= priority) { result = (team, rule); priority = acquired; }
            }
        return result;
    }
    internal static void Spawn(NormalMatchGameModeController controller)
    {
        var referees = GameManager.Instance?.referees;
        if (referees == null || referees.Count != 1 || Latest(CustomRewardEffects.ExtraReferee).Rule == null) return;
        var parent = GameObject.Find("Referee"); var first = referees[0]?._forward?._forwardData;
        if (parent == null || first == null || controller.teamSelectionState == null) return;
        var data = controller.teamSelectionState.GetSecondReferee(first);
        if (data == null) return;
        // Same initializer, parent, neutral team and referee index as the
        // native SecondReferee branch; native match cleanup owns this skater.
        var forward = controller.InitializeForward(parent.transform, data, SkaterType.Referee, null, true);
        var referee = forward?.GetComponent<Referee>();
        if (referee == null) throw new InvalidOperationException("Native second-referee initialization produced no Referee component.");
        referee.refereeIndex = 1; referees.Add(referee);
    }
    internal static void Setup(Referee referee)
    {
        CustomRewardRuntime.MatchStarted();
        if (referee != null) started.Add(referee.Pointer);
        if (referee?._forward == null || states.ContainsKey(referee.Pointer)) return;
        var block = Latest(CustomRewardEffects.RefereeBlock); var size = Latest(CustomRewardEffects.RefereeSize);
        var pursuit = Latest(CustomRewardEffects.RefereePursuit);
        var recruit = Latest(CustomRewardEffects.PlayableReferees);
        if (block.Rule == null && pursuit.Rule == null && size.Rule == null && recruit.Rule == null) return;
        var state = new State { Referee = referee, WasBlocking = referee.isBlockingShots, PreviousTeam = referee.teamBlocking,
            WasPursuing = referee.isPursuingPLayers, PreviousPursuitTeam = referee.teamPursuing, PreviousTarget = referee.forwardPursuing };
        states.Add(referee.Pointer, state);
        // Native recruitment has already run before Referee.Start. Do not
        // steal a referee assigned by it or by another independent system.
        if (recruit.Rule != null && !referee.isOnTeam) {
            state.ChangedRecruitment = true;
            state.WasOnTeam = referee.isOnTeam; state.WasControllable = referee.isPlayerControlled;
            state.PreviousOwner = referee._forward.Team; state.AppliedOwner = recruit.Team;
            referee._forward.Team = recruit.Team; referee.isOnTeam = referee.isPlayerControlled = true;
        }
        if (block.Rule != null || pursuit.Rule != null) {
            bool usePursuit = pursuit.Rule != null && (block.Rule == null ||
                CustomRewardRuntime.Acquired(pursuit.Team._teamData, pursuit.Rule) > CustomRewardRuntime.Acquired(block.Team._teamData, block.Rule));
            state.ChangedBlocking = state.ChangedPursuing = true;
            state.AppliedBlocking = !usePursuit; state.AppliedPursuing = usePursuit;
            state.AppliedTeam = usePursuit ? referee.teamBlocking : block.Team;
            state.AppliedPursuitTeam = usePursuit ? pursuit.Team.otherTeam : referee.teamPursuing;
            referee.isBlockingShots = state.AppliedBlocking; referee.teamBlocking = state.AppliedTeam;
            referee.isPursuingPLayers = usePursuit; referee.teamPursuing = state.AppliedPursuitTeam;
            if (usePursuit && !referee.IsInvoking("SwitchTarget")) {
                // Referee.Start uses this exact native cadence. Only cancel
                // a timer we added, never an existing native/other-mod timer.
                state.AddedTargetTimer = true; referee.InvokeRepeating("SwitchTarget", 0, 6);
            }
        }
        if (size.Rule != null) {
            state.ChangedSize = true;
            referee._forward._forwardData.SetSizeEffect(state.SizeSource, Math.Clamp(size.Rule.Amount, -5, 5)); referee._forward.SetSize();
        }
    }
    internal static void Refresh()
    {
        var rules = string.Join("|", new[] { CustomRewardEffects.PlayableReferees, CustomRewardEffects.RefereeBlock, CustomRewardEffects.RefereePursuit, CustomRewardEffects.RefereeSize }
            .Select(effect => { var pair = Latest(effect); return pair.Team?._teamData?.id + ":" + pair.Rule?.Id; }));
        if (rules == appliedRules) return;
        Restore(); appliedRules = rules;
        var referees = GameManager.Instance?.referees;
        for (int i = 0; referees != null && i < referees.Count; i++)
            if (referees[i] != null && started.Contains(referees[i].Pointer)) Setup(referees[i]);
    }
    internal static void Clear()
    {
        Restore(); started.Clear(); appliedRules = null;
    }
    private static void Restore()
    {
        foreach (var state in states.Values) if (state.Referee?._forward != null) {
            var referee = state.Referee;
            if (state.ChangedRecruitment && referee._forward.Team == state.AppliedOwner && referee.isOnTeam && referee.isPlayerControlled) {
                // Release input ownership before removing the referee from the
                // custom team. Native switching can then select a roster skater.
                if (referee._forward.IsPlayerControlled(out var player)) player.RemoveControlOfSkater();
                referee._forward.Team = state.PreviousOwner;
                referee.isOnTeam = state.WasOnTeam; referee.isPlayerControlled = state.WasControllable;
            }
            if (state.ChangedBlocking && referee.isBlockingShots == state.AppliedBlocking && referee.teamBlocking == state.AppliedTeam) {
                referee.isBlockingShots = state.WasBlocking; referee.teamBlocking = state.PreviousTeam;
            }
            if (state.ChangedPursuing && referee.isPursuingPLayers == state.AppliedPursuing && referee.teamPursuing == state.AppliedPursuitTeam) {
                referee.isPursuingPLayers = state.WasPursuing; referee.teamPursuing = state.PreviousPursuitTeam; referee.forwardPursuing = state.PreviousTarget;
            }
            if (state.AddedTargetTimer) referee.CancelInvoke("SwitchTarget");
            if (state.ChangedSize) { referee._forward._forwardData.RemoveSizeEffect(state.SizeSource); referee._forward.SetSize(); }
        }
        states.Clear();
    }
}

[HarmonyPatch(typeof(NormalMatchGameModeController), "OnInitializeForwards")]
internal static class CustomRewardExtraReferee
{
    [HarmonyPostfix] private static void Postfix(NormalMatchGameModeController __instance) => CustomRewardRuntime.Safe(() => CustomRewardReferees.Spawn(__instance));
}
[HarmonyPatch(typeof(Referee), "Start")]
internal static class CustomRewardRefereeSetup
{
    [HarmonyPostfix] private static void Postfix(Referee __instance) => CustomRewardRuntime.Safe(() => CustomRewardReferees.Setup(__instance));
}
