using System;
using System.Collections.Generic;
using System.Linq;
using CampaignCore;
using Data;
using HarmonyLib;
using Rogue.Rewards;
using Tape2Tape;
using Tape2Tape.Hockey.UI;

namespace EndlessMode;

internal static class CustomRewardCampaignRules
{
    internal static CustomRewardDefinition LatestSetup(string effect)
    {
        CustomRewardDefinition result = null; long priority = long.MinValue;
        foreach (var team in new[] { GameManager.HomeTeam?._teamData, GameManager.VisitorTeam?._teamData }) if (team != null)
            foreach (var rule in Owned(team, "Match setup").Where(d => d.Effect == effect)) {
                long acquired = CustomRewardRuntime.Acquired(team, rule);
                if (acquired >= priority) { result = rule; priority = acquired; }
            }
        return result;
    }
    // Whole-team match rules reaching this team: its own (aimed at itself) and
    // the other team's rules aimed at Opposing team.
    internal static IEnumerable<CustomRewardDefinition> Applying(Team team, string trigger, string effect)
    {
        if (team?._teamData == null) yield break;
        foreach (var d in Owned(team._teamData, trigger)) if (d.Effect == effect && d.Target != CustomRewardEffects.OpposingTeam) yield return d;
        if (team.otherTeam?._teamData != null)
            foreach (var d in Owned(team.otherTeam._teamData, trigger)) if (d.Effect == effect && d.Target == CustomRewardEffects.OpposingTeam) yield return d;
    }
    internal static IEnumerable<CustomRewardDefinition> Owned(TeamData team, string trigger)
    {
        if (team == null) yield break;
        CustomRewardRuntime.EnsureAssets();
        CustomRewardRuntime.EnsureRun(); CustomRewardRuntime.ObserveOwnership(team);
        foreach (var d in CustomRewardRuntime.Definitions.Values) {
            if (d.NeedsRedesign || d.Trigger != trigger) continue;
            if (d.IsRelic) { if (CustomRewardRuntime.Has(team, d.Id)) yield return d; }
            else foreach (var player in team.GetAllSkaters()) if (CustomRewardRuntime.Has(player, d.Id) && player.powerups.HasMatch(t => t != null && t.id == d.Id && !t.PayoffSuppressed)) yield return d;
        }
    }
    // Unbounded authored counts are summed in long and saturated: native
    // counters add this to their own value, so stay well inside int.
    internal static int Amount(TeamData team, string trigger, string effect) => (int)Math.Clamp(Owned(team, trigger).Where(d => d.Effect == effect).Sum(d => (long)d.Amount), 0, int.MaxValue / 4);
    internal static string Surface(TeamData home, TeamData away)
    {
        CustomRewardDefinition selected = null; long priority = long.MinValue;
        foreach (var team in new[] { home, away }) if (team != null)
            foreach (var d in Owned(team, "Match setup").Where(d => d.IsRelic && CustomRewardEffects.IsRink(d.Effect))) {
                long acquired = CustomRewardRuntime.Acquired(team, d);
                if (acquired >= priority) { selected = d; priority = acquired; }
            }
        return selected?.Effect switch { CustomRewardEffects.GolfRink => "Golf ice", CustomRewardEffects.SoccerRink => "Soccer field", CustomRewardEffects.TycoonsRink => "Tycoons ice", _ => "" };
    }
}
[HarmonyPatch(typeof(NHL94.Goal), "SetGoalieNet")]
internal static class CustomRewardNetSetup
{
    [HarmonyPostfix] private static void Postfix(NHL94.Goal __instance) => CustomRewardRuntime.Safe(() => {
        if (__instance?.transform?.parent == null) return;
        var width = CustomRewardCampaignRules.LatestSetup(CustomRewardEffects.NetWidth);
        var height = CustomRewardCampaignRules.LatestSetup(CustomRewardEffects.NetHeight);
        var look = CustomRewardCampaignRules.LatestSetup(CustomRewardEffects.SoccerNetLook);
        var scale = __instance.transform.parent.localScale;
        if (width != null) scale.x = __instance.originalScale.x * width.Amount / 100f;
        if (height != null) scale.y = __instance.originalScale.y * height.Amount / 100f;
        if (width != null || height != null) __instance.transform.parent.localScale = scale;
        if (look != null && __instance.goalPostRenderer != null && __instance.soccerNetMaterial != null)
            __instance.goalPostRenderer.material = __instance.soccerNetMaterial;
    });
}
[HarmonyPatch(typeof(RewardCounter), "SetTalentRewardCount")]
internal static class CustomRewardTalentCount
{
    [HarmonyPostfix] private static void Postfix(RewardCounter __instance) {
        if (Plugin.IsDefaultMode || !T2TUtils.IsInRogueMatch) return;
        var team = CampaignUtil.CampaignState?.runData?.teamData;
        if (team == null || team.HaveRelic<Rogue.Relics.FinalSquadRelics.NoRewardCurse>()) return;
        __instance._talentRewardsRemaining += CustomRewardCampaignRules.Amount(team, "Match rewards", CustomRewardEffects.TalentRewards);
    }
}
[HarmonyPatch(typeof(RewardCounter), "SetRelicRewardCount")]
internal static class CustomRewardRelicCount
{
    [HarmonyPostfix] private static void Postfix(RewardCounter __instance) {
        if (Plugin.IsDefaultMode || !T2TUtils.IsInRogueMatch) return;
        var team = CampaignUtil.CampaignState?.runData?.teamData;
        if (team == null || team.HaveRelic<Rogue.Relics.FinalSquadRelics.NoRewardCurse>()) return;
        __instance._relicRewardsRemaining += CustomRewardCampaignRules.Amount(team, "Match rewards", CustomRewardEffects.RelicRewards);
    }
}
// IL2CPP inlines CalculateAvailableRerolls in the menu callers. InitData copies
// the native count into each menu; augment that copy, leaving reusable metadata
// unchanged. Rerolls then decrement the menu's count through its native path.
[HarmonyPatch(typeof(SkaterPowerupRewardMenu), "InitData")]
internal static class CustomRewardTalentRerolls {
    [HarmonyPostfix] private static void Postfix(SkaterPowerupRewardMenu __instance, Il2CppSystem.Object __0) {
        if (!Plugin.IsDefaultMode && __0?.TryCast<SkaterPowerupRewardMenu.Metadata>() is { } data)
            __instance.m_RerollCount += CustomRewardCampaignRules.Amount(data.Team, "Reward selection", CustomRewardEffects.Rerolls);
    }
}
[HarmonyPatch(typeof(RelicRewardMenu), "InitData")]
internal static class CustomRewardRelicRerolls {
    [HarmonyPostfix] private static void Postfix(RelicRewardMenu __instance, Il2CppSystem.Object __0) {
        if (!Plugin.IsDefaultMode && __0?.TryCast<RelicRewardMenu.Metadata>() is { } data)
            __instance.m_RerollCount += CustomRewardCampaignRules.Amount(data.Team, "Reward selection", CustomRewardEffects.Rerolls);
    }
}
[HarmonyPatch(typeof(EventChoiceMenu), "InitData")]
internal static class CustomRewardEventRerolls {
    [HarmonyPostfix] private static void Postfix(EventChoiceMenu __instance, Il2CppSystem.Object __0) {
        if (!Plugin.IsDefaultMode && __0?.TryCast<EventChoiceMenu.Metadata>() != null)
            __instance.m_RerollCount += CustomRewardCampaignRules.Amount(CampaignUtil.CampaignState?.runData?.teamData, "Map event choices", CustomRewardEffects.Rerolls);
    }
}
