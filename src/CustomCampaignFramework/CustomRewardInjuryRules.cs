using CampaignCore;
using System;
using System.Linq;
using Data;
using HarmonyLib;
using Skaters.Injuries;
using Skaters;
using Tape2Tape;

namespace EndlessMode;

internal static class CustomRewardInjuryRules
{
    internal static bool HasType(Skater player, InjuryType type) => player?.TryCast<Forward>() is { } forward
        && CampaignUtil.Instance?._injurySystem?.IsForwardInjured(forward._forwardData, type) == true;
    internal static bool Arm(Forward target)
    {
        if (GameManager.GameType != ScriptableStates.GameType.Rogue || target == null || CampaignUtil.Instance?._injurySystem is not { } injuries
            || injuries.AreInjuriesDisabledInMatch() || !InjurySystem.CanBeInjured(target._forwardData, target.Team._teamData)
            || !InjurySystem.CanHaveInjuryType(target._forwardData, target.Team._teamData, InjuryType.Arm)
            || !injuries.GetPossibleInjuries(target._forwardData).Contains(InjuryType.Arm)
            || injuries.WasInjuryPrevented(target.Team._teamData, target._forwardData, InjuryType.Arm)) return false;
        int before = injuries.GetInjuryCount(target._forwardData.id);
        injuries.AddInjuryToForward(InjuryType.Arm, target._forwardData, InjurySystem.GetRandomizedHealingTimeInGame());
        return injuries.GetInjuryCount(target._forwardData.id) > before;
    }
    internal static float ScaleChance(Forward defender, float chance)
    {
        if (!CustomRewardRuntime.InMatch || defender?.Team?._teamData == null) return chance;
        double multiplier = 1;
        foreach (var team in new[] { defender.Team, defender.Team.otherTeam }) {
            if (team?._teamData == null) continue;
            foreach (var d in CustomRewardCampaignRules.Owned(team._teamData, "Injury attempted"))
                if (d.Effect == CustomRewardEffects.InjuryMultiplier && (d.Target == CustomRewardEffects.OpposingTeam ? team != defender.Team : team == defender.Team)) multiplier *= d.Amount / 100d;
        }
        return (float)Math.Clamp(chance * multiplier, 0, 1);
    }
    internal static int HealingAdjustment(ForwardData player)
    {
        if (Plugin.IsDefaultMode || player == null) return 0;
        CustomRewardRuntime.EnsureAssets();
        var team = CampaignUtil.CampaignState?.runData?.teamData;
        if (CustomRewardRuntime.InMatch) foreach (var candidate in new[] { GameManager.HomeTeam?._teamData, GameManager.VisitorTeam?._teamData })
            if (candidate?.GetAllSkaters().HasMatch(s => s?.id == player.id) == true) { team = candidate; break; }
        bool inTeam = team?.GetAllSkaters().HasMatch(s => s?.id == player.id) == true;
        int adjustment = 0;
        foreach (var d in CustomRewardRuntime.Definitions.Values) {
            if (d.NeedsRedesign || d.Trigger != "New injury" || d.Effect != CustomRewardEffects.InjuryHealing) continue;
            if (d.Target == CustomRewardEffects.Actor) { if (Owns(player, d.Id)) adjustment += d.Amount; }
            else if (inTeam) foreach (var source in team.GetAllSkaters()) if (Owns(source, d.Id)) adjustment += d.Amount;
        }
        return adjustment;
    }
    internal static bool Prevents(ForwardData player, TeamData team, InjuryType? type)
    {
        if (player == null || Plugin.IsDefaultMode) return false;
        CustomRewardRuntime.EnsureAssets();
        foreach (var d in CustomRewardRuntime.Definitions.Values) {
            if (d.NeedsRedesign || d.Trigger != "Injury attempted" || !Covers(d.Effect, type)) continue;
            if (d.IsRelic) { if (CustomRewardRuntime.Has(team, d.Id)) return true; }
            else if (d.Target == CustomRewardEffects.Actor) { if (Owns(player, d.Id)) return true; }
            else if (team != null && team.GetAllSkaters().HasMatch(s => Owns(s, d.Id))) return true;
        }
        return false;
    }
    private static bool Owns(SkaterData player, string id) => CustomRewardRuntime.Has(player, id)
        && player.powerups.HasMatch(t => t != null && t.id == id && !t.PayoffSuppressed);
    private static bool Covers(string effect, InjuryType? type) => effect == CustomRewardEffects.PreventInjury || type.HasValue && effect == (type.Value switch {
        InjuryType.Eye => CustomRewardEffects.PreventEye, InjuryType.Shoulder => CustomRewardEffects.PreventShoulder,
        InjuryType.Leg => CustomRewardEffects.PreventLeg, InjuryType.Arm => CustomRewardEffects.PreventArm, _ => ""
    });
}

[HarmonyPatch(typeof(InjurySystem), "CalculateInjuryChance")]
internal static class CustomRewardInjuryChance
{
    [HarmonyPostfix] private static void Postfix(Forward __1, ref float __result) => __result = CustomRewardInjuryRules.ScaleChance(__1, __result);
}
// The event overload constructs and stores the injury directly. The match
// overload accepts an already-created injury. Neither calls the other, so each
// new injury is adjusted exactly once, before its native notification/save.
[HarmonyPatch(typeof(InjurySystem), "AddInjuryToForward", new[] { typeof(Injury), typeof(ForwardData), typeof(bool) })]
internal static class CustomRewardMatchHealingTime
{
    [HarmonyPrefix] private static void Prefix(Injury __0, ForwardData __1) {
        if (__0 == null) return;
        int adjustment = CustomRewardInjuryRules.HealingAdjustment(__1);
        if (adjustment == 0) return;
        __0.HealingTime = Math.Max(1, __0.HealingTime + adjustment);
        __0.OriginalTime = __0.HealingTime;
    }
}
[HarmonyPatch(typeof(InjurySystem), "AddInjuryToForward", new[] { typeof(InjuryType), typeof(ForwardData), typeof(int) })]
internal static class CustomRewardEventHealingTime
{
    [HarmonyPrefix] private static void Prefix(ForwardData __1, ref int __2) {
        int adjustment = CustomRewardInjuryRules.HealingAdjustment(__1);
        if (adjustment != 0) __2 = Math.Max(1, __2 + adjustment);
    }
}

// Both native predicates have verified direct callers in match and campaign
// event injury paths. Returning false retains the native prevention result and
// avoids an apply-then-heal workaround or an extra owned reward.
[HarmonyPatch(typeof(InjurySystem), "CanBeInjured")]
internal static class CustomRewardInjuryEligibility
{
    [HarmonyPostfix] private static void Postfix(ForwardData __0, TeamData __1, ref bool __result) {
        if (__result && CustomRewardInjuryRules.Prevents(__0, __1, null)) __result = false;
    }
}
[HarmonyPatch(typeof(InjurySystem), "CanHaveInjuryType")]
internal static class CustomRewardInjuryTypeEligibility
{
    [HarmonyPostfix] private static void Postfix(ForwardData __0, TeamData __1, InjuryType __2, ref bool __result) {
        if (__result && CustomRewardInjuryRules.Prevents(__0, __1, __2)) __result = false;
    }
}
