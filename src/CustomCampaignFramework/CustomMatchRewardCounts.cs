using System;
using System.Linq;
using CampaignCore;
using HarmonyLib;
using Rogue;
using Rogue.Rewards;
using Rogue.Relics.FinalSquadRelics;
using State;
using STS.Map;
using Tape2Tape;

namespace EndlessMode;

// Native Elite reads the authored counts; Boss and Challenge use constants.
// Adjust those constants after native bonuses have been calculated, without
// calling GetDoubleRewardCount a second time (it consumes a relic charge).
internal static class CustomMatchRewardCounts
{
    private sealed class SelectedMatch
    {
        internal IntPtr State, Run, Team, Blueprint;
        internal string RunId, TeamId, SquadId, Folder, Campaign;
        internal int Act, Layer, Index;
        internal NodeType Type;
        internal MapLayoutNode Counts;

        internal bool Matches(CampaignState state)
        {
            var run = state?.runData;
            return run?.teamData != null && state.currentNode != null
                && state.Pointer == State && run.Pointer == Run && run.teamData.Pointer == Team
                && state.currentNode.Pointer == Blueprint && state.currentNode.nodeType == Type
                && run.runID == RunId && run.teamData.id == TeamId && run.CurrentAct == Act
                && run.currentNodePoint.x == Index && run.currentNodePoint.y == Layer
                && state.GetCurrentSquad()?.id == SquadId
                && string.Equals(Plugin.ModFolder, Folder, StringComparison.OrdinalIgnoreCase)
                && string.Equals(Plugin.ActiveCampaign, Campaign, StringComparison.Ordinal);
        }
    }

    private static SelectedMatch selected;

    // SetCurrentNode runs while the authored map is still loaded. RewardCounter
    // initializes in standard_game, after the world-map scene has been removed;
    // looking for its MapManager at that point silently loses Boss/Challenge
    // overrides. Keep only copied counts and exact selection identities here.
    internal static void Selected(CampaignState state, MapNode mapNode)
    {
        selected = null; // Every selection, including events/null, retires the old match.
        if (Plugin.IsDefaultMode || mapNode?.Node == null || state?.runData?.teamData == null) return;
        var blueprint = state.currentNode;
        if (blueprint == null || blueprint.nodeType is not (NodeType.Boss or NodeType.Challenge)) return;
        try
        {
            MapNode bound = blueprint.TryCast<BossBlueprint>()?.mapNode
                ?? (MapNode)blueprint.TryCast<ChallengeBlueprint>()?.mapNode;
            if (bound == null || bound.Pointer != mapNode.Pointer) return;
            var manager = UnityEngine.Object.FindObjectOfType<MapManager>();
            var layout = manager == null ? null : CustomMapLayout.LayoutFor(manager.CurrentMap);
            var native = mapNode.Node;
            var node = layout?.Nodes.FirstOrDefault(n => n.Layer == native.LayerIndex && layout.NativeIndex(n) == native.NodeIndex);
            var run = state.runData;
            string squad = state.GetCurrentSquad()?.id;
            if (node == null || node.Type != blueprint.nodeType.ToString() || string.IsNullOrEmpty(run.runID)
                || string.IsNullOrEmpty(run.teamData.id) || string.IsNullOrEmpty(squad)
                || run.currentNodePoint.x != native.NodeIndex || run.currentNodePoint.y != native.LayerIndex)
            {
                Plugin.Log.LogWarning($"[RewardCounts] {blueprint.nodeType}: no exact authored selection; retaining native counts.");
                return;
            }
            selected = new SelectedMatch
            {
                State = state.Pointer, Run = run.Pointer, Team = run.teamData.Pointer, Blueprint = blueprint.Pointer,
                RunId = run.runID, TeamId = run.teamData.id, SquadId = squad,
                Folder = Plugin.ModFolder, Campaign = Plugin.ActiveCampaign, Act = run.CurrentAct,
                Layer = native.LayerIndex, Index = native.NodeIndex, Type = blueprint.nodeType,
                Counts = new MapLayoutNode { Id = node.Id, Layer = node.Layer, Type = node.Type,
                    TalentRewards = node.TalentRewards, RelicRewards = node.RelicRewards }
            };
            Plugin.Log.LogInfo($"[RewardCounts] Captured {node.Type} '{node.Id}' at {native.NodeIndex},{native.LayerIndex}: talents={node.TalentRewards}, relics={node.RelicRewards}, run={run.runID}.");
        }
        catch (Exception ex)
        {
            selected = null;
            Plugin.Log.LogWarning("[RewardCounts] Could not bind authored match counts; retaining native counts: " + ex.Message);
        }
    }

    internal static void Apply(RewardCounter counter, MapLayoutNode node, NodeType type, string squadId, bool relic)
    {
        if (node == null || node.Type is not ("Elite" or "Boss" or "Challenge")) return;
        if (SquadBehaviorRuntime.Enabled(squadId, "gm_rewards", squadId == "generalmanager")
            || SquadBehaviorRuntime.Enabled(squadId, "no_relic_rewards", squadId == "norelics")) return;
        if (type == NodeType.Elite) return; // Native already uses both saved counts.
        if (type is not (NodeType.Boss or NodeType.Challenge)) return;
        if (relic) counter._relicRewardsRemaining = Math.Max(0, counter._relicRewardsRemaining + node.RelicRewards);
        else counter._talentRewardsRemaining = Math.Max(0, counter._talentRewardsRemaining - 1 + node.TalentRewards);
    }
    internal static void After(RewardCounter counter, bool relic)
    {
        if (counter == null || Plugin.IsDefaultMode || !T2TUtils.IsInRogueMatch) return;
        try
        {
            var state = CampaignUtil.CampaignState;
            if (state?.runData?.teamData == null || state.currentNode == null
                || state.currentNode.nodeType is not (NodeType.Boss or NodeType.Challenge)) return;
            var match = selected;
            if (match == null || !match.Matches(state))
            {
                Plugin.Log.LogWarning($"[RewardCounts] {state.currentNode.nodeType} {(relic ? "relic" : "talent")}: authored selection missing or changed; retaining native counts.");
                return;
            }
            if (state.runData.teamData.HaveRelic<NoRewardCurse>()
                || SquadBehaviorRuntime.Enabled(match.SquadId, "gm_rewards", match.SquadId == "generalmanager")
                || SquadBehaviorRuntime.Enabled(match.SquadId, "no_relic_rewards", match.SquadId == "norelics"))
            {
                Plugin.Log.LogInfo($"[RewardCounts] {match.Type} '{match.Counts.Id}': native squad/curse reward rule retained.");
                return;
            }
            int before = relic ? counter._relicRewardsRemaining : counter._talentRewardsRemaining;
            Apply(counter, match.Counts, match.Type, match.SquadId, relic);
            int after = relic ? counter._relicRewardsRemaining : counter._talentRewardsRemaining;
            Plugin.Log.LogInfo($"[RewardCounts] {match.Type} '{match.Counts.Id}' {(relic ? "relic" : "talent")}: native={before}, authored={(relic ? match.Counts.RelicRewards : match.Counts.TalentRewards)}, effective={after}; selection verified.");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning("[RewardCounts] Could not verify authored match counts; retaining native counts: " + ex.Message);
        }
    }
}
[HarmonyPatch(typeof(CampaignState), nameof(CampaignState.SetCurrentNode))]
internal static class CustomMatchRewardSelection
{
    [HarmonyPostfix] private static void Postfix(CampaignState __instance, MapNode __0) => CustomMatchRewardCounts.Selected(__instance, __0);
}
[HarmonyPatch(typeof(RewardCounter), "SetTalentRewardCount")]
internal static class CustomMatchTalentCount
{
    [HarmonyPostfix] private static void Postfix(RewardCounter __instance) => CustomMatchRewardCounts.After(__instance, false);
}
[HarmonyPatch(typeof(RewardCounter), "SetRelicRewardCount")]
internal static class CustomMatchRelicCount
{
    [HarmonyPostfix] private static void Postfix(RewardCounter __instance) => CustomMatchRewardCounts.After(__instance, true);
}
