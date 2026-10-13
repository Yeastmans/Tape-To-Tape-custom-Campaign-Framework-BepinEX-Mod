using System;
using System.Linq;
using CampaignCore;
using Data;
using HarmonyLib;
using I2.Loc;
using State;
using STS.Map;
using UnityEngine;

namespace EndlessMode;

// All three native LaunchMatch methods finish configuring the timer before
// SetCurrentNode, then start LoadGame. Apply here, before the scene reads it.
[HarmonyPatch(typeof(CampaignState), nameof(CampaignState.SetCurrentNode))]
internal static class MatchPeriods
{
    internal const string PeriodsTerm = "ingame/team_presentation/periods";
    private static InGameData owner;
    private static TimerRuleStateData original, owned;
    private static bool originalRetentionAdded;
    private static bool singlePeriodHud;

    // Native Rogue HUDs assume count <= 1 means a Challenge's sudden death.
    // Bind the exception to the exact timer clone we authored; a later native
    // match, a count=0 default, or another mod's replacement stays untouched.
    internal static bool UsesSinglePeriodHud(InGameData data)
    {
        if (Plugin.IsDefaultMode || !singlePeriodHud || owned == null || data?.timerRule == null
            || data.timerRule.Pointer != owned.Pointer || data.timerRule.numOfPeriod != 1) return false;
        var conditions = data.endOfGameConditionData?.endOfGameConditions;
        return conditions != null && conditions.Contains(EndOfGameConditionType.Timer)
            && !conditions.Contains(EndOfGameConditionType.MaxGoal)
            && !conditions.Contains(EndOfGameConditionType.NextGoal);
    }

    internal static void Preview(MatchPreviewMenu menu)
    {
        if (Plugin.IsDefaultMode || menu?.currentMapNode?.Node == null || menu.duration == null) return;
        try
        {
            var manager = UnityEngine.Object.FindObjectOfType<MapManager>();
            var layout = manager == null ? null : CustomMapLayout.LayoutFor(manager.CurrentMap);
            var native = menu.currentMapNode.Node;
            var node = layout?.Nodes.FirstOrDefault(n => n.Layer == native.LayerIndex && layout.NativeIndex(n) == native.NodeIndex);
            PreviewLabel(menu.duration, node);
        }
        catch (Exception ex) { Plugin.Log.LogWarning("[Periods] Could not update match preview: " + ex.Message); }
    }

    internal static void PreviewLabel(Localize label, MapLayoutNode node)
    {
        if (Plugin.IsDefaultMode || label == null || node == null || node.Type is not ("Elite" or "Boss")
            || node.PeriodCount < 1 || node.PeriodCount > 3 || !float.IsFinite(node.PeriodLengthMinutes)
            || node.PeriodLengthMinutes != 0 && (node.PeriodLengthMinutes < .5f || node.PeriodLengthMinutes > 20)) return;
        // Native Challenge matches remain first-goal-wins, even in a timed
        // campaign. Likewise retain every goal-target label for Elite/Boss.
        if (label.mTerm is not ("v2ui/match_preview/elite/mode/timer" or "v2ui/match_preview/boss/mode/timer" or PeriodsTerm)) return;
        var parameters = label.GetComponent<LocalizationParamsManager>()
            ?? label.gameObject.AddComponent<LocalizationParamsManager>();
        parameters.SetParameterValue("NUMPERIOD", node.PeriodCount.ToString(System.Globalization.CultureInfo.InvariantCulture), false);
        label.SetTerm(PeriodsTerm);
        label.OnLocalize(true);
    }

    [HarmonyPostfix]
    internal static void Selected(MapNode __0)
    {
        if (Plugin.IsDefaultMode || __0?.TryCast<MatchMapNode>() == null || __0.Node == null) return;
        try
        {
            var manager = UnityEngine.Object.FindObjectOfType<MapManager>();
            var layout = manager == null ? null : CustomMapLayout.LayoutFor(manager.CurrentMap);
            var node = layout?.Nodes.FirstOrDefault(n => n.Layer == __0.Node.LayerIndex && layout.NativeIndex(n) == __0.Node.NodeIndex);
            ApplyCore(__0.ingameData, node);
        }
        catch (Exception ex) { End(); Plugin.Log.LogError("[Periods] Could not apply match settings: " + ex); }
    }

    internal static void Apply(InGameData data, MapLayoutNode node)
        => ApplyCore(data, node);

    private static void ApplyCore(InGameData data, MapLayoutNode node)
    {
        if (Plugin.IsDefaultMode) return;
        if (node == null) return;
        if (node.Type is not ("Elite" or "Boss" or "Challenge")) return;
        if (node.PeriodCount == 0 && node.PeriodLengthMinutes == 0) return;
        if (data?.timerRule == null) return;
        if (node.PeriodCount < 0 || node.PeriodCount > 3 || !float.IsFinite(node.PeriodLengthMinutes)
            || node.PeriodLengthMinutes != 0 && (node.PeriodLengthMinutes < .5f || node.PeriodLengthMinutes > 20)) return;
        var conditions = data.endOfGameConditionData?.endOfGameConditions;
        if (conditions == null || !conditions.Contains(EndOfGameConditionType.Timer))
        {
            Plugin.Log.LogInfo("[Periods] Goal-target match keeps native settings.");
            return;
        }
        if (owned != null) return; // SetCurrentNode may be repeated; never clone our clone.
        original = data.timerRule;
        owned = UnityEngine.Object.Instantiate(original);
        // Scene cleanup unloads unused native assets. Once data holds our clone,
        // this managed field alone does not keep the original ScriptableObject alive.
        // Pin only the detached original, before replacing its native reference.
        originalRetentionAdded = (original.hideFlags & HideFlags.DontUnloadUnusedAsset) == 0;
        if (originalRetentionAdded) original.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        owner = data;
        if (node.PeriodCount > 0) owned.numOfPeriod = node.PeriodCount;
        if (node.PeriodLengthMinutes > 0) owned.periodLength = node.PeriodLengthMinutes;
        owner.timerRule = owned;
        singlePeriodHud = node.PeriodCount == 1 && node.Type is ("Elite" or "Boss");
        Plugin.Log.LogInfo($"[Periods] Node {node.Id}: {owned.numOfPeriod} periods x {owned.periodLength} minutes (native {original.numOfPeriod} x {original.periodLength}); applied before loading.");
        return;
    }

    internal static void End()
    {
        if (owner != null && owned != null && owner.timerRule != null && owner.timerRule.Pointer == owned.Pointer)
            owner.timerRule = original;
        // Release our retention only after restoring the native reference. Preserve
        // pre-existing retention and any unrelated flags changed by another mod.
        if (originalRetentionAdded && original != null)
            original.hideFlags &= ~HideFlags.DontUnloadUnusedAsset;
        if (owned != null) UnityEngine.Object.Destroy(owned);
        owner = null; original = owned = null;
        originalRetentionAdded = false;
        singlePeriodHud = false;
    }
}
