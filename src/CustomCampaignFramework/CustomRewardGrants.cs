using System;
using System.Collections.Generic;
using System.Linq;
using CampaignCore;
using HarmonyLib;
using Rogue;
using Rogue.Upgrades;
using Rogue.Powerups.Repository;
using Rogue.Powerups.Controllers.Talents;
using Rogue.Relics.FinalSquadRelics;
using Skaters;
using Tape2Tape;
using UnityEngine;

namespace EndlessMode;

internal static class CustomRewardGrants
{
    internal static bool Talent(Forward recipient)
    {
        var campaignTeam = CampaignUtil.CampaignState?.runData?.teamData;
        var persistent = recipient == null ? null : campaignTeam?.GetForward(recipient._forwardData.id);
        var repository = CampaignUtil.Instance?._talentRepository;
        if (persistent == null || repository == null || recipient.Team?._teamData?.id != campaignTeam.id) return false;
        var excluded = new Il2CppSystem.Collections.Generic.List<Rogue.Talent>();
        foreach (var owned in recipient._forwardData.powerups) excluded.Add(owned);
        // Mirrors Performance Bonus's bounded native picker. Rejected pickup
        // grants/curses are excluded for subsequent attempts, not saved.
        for (int attempt = 0; attempt < 10; attempt++) {
            var talent = repository.GetRandomTalent(TalentCategory.UsedInCampaignPool, excluded, true, false, true, recipient._forwardData);
            if (talent == null) return false;
            if (PerformanceBonusController.HasPickupEffect(talent) || talent.TryCast<ICurse>() != null
                || CustomRewardRuntime.Definitions.TryGetValue(talent.id, out var custom) && (custom.NeedsRedesign || custom.IsGoalie || custom.Effect == CustomRewardEffects.GainTalent)) {
                excluded.Add(talent); continue;
            }
            if (!recipient.TryAddTalent(talent, CustomRewardRuntime.IsCustom(talent.id))) { excluded.Add(talent); continue; }
            // Live player data is a match copy. The native Performance Bonus
            // explicitly adds the same award to the run's canonical player.
            if (!persistent.powerups.HasMatch(t => t != null && t.id == talent.id)) persistent.powerups.Add(talent);
            persistent.ResetTalentCache();
            recipient.RefreshSkaterUI();
            return true;
        }
        return false;
    }
    internal static bool Upgrade(CustomRewardDefinition rule, Skater actor)
    {
        var team = CampaignUtil.CampaignState?.runData?.teamData;
        if (team == null || actor?.Team?._teamData?.id != team.id) return false;
        var eligible = new List<UpgradeablePowerup>();
        foreach (var power in UpgradeHelper.GetUpgradeablePowerups(team)) {
            bool include = rule.Effect == CustomRewardEffects.UpgradeTalent ? power.TryCast<UpgradeableTalent>() is { } t && !CustomRewardRuntime.IsCustom(t.talent?.id)
                : rule.Effect == CustomRewardEffects.UpgradeAbility ? power.TryCast<UpgradeableAbility>() != null
                : power.TryCast<UpgradeableRelic>() is { } r && !CustomRewardRuntime.IsCustom(r.relic?.id);
            if (include) eligible.Add(power);
        }
        int count = Math.Min(rule.Amount, eligible.Count);
        for (int i = 0; i < count; i++) {
            int chosen = UnityEngine.Random.Range(i, eligible.Count);
            (eligible[i], eligible[chosen]) = (eligible[chosen], eligible[i]);
            eligible[i].Upgrade();
            RandomUpgrade.PushUpgradeNotification(eligible[i], team);
        }
        return count > 0;
    }
}

[HarmonyPatch(typeof(Rogue.FreeAgents.FreeAgentManager), "HandleFreeAgentsLeaving")]
internal static class CustomRewardContractRetention
{
    // Native Handcuffs skips every removal in this method. No contracts are
    // altered and no departed player is restored from an old save.
    [HarmonyPrefix] private static bool Prefix(Rogue.FreeAgents.FreeAgentManager __instance)
        => Plugin.IsDefaultMode || !CustomRewardCampaignRules.Owned(__instance._campaignState?.runData?.teamData, "Contract expiry")
            .Any(d => d.Effect == CustomRewardEffects.KeepFreeAgents);
}
