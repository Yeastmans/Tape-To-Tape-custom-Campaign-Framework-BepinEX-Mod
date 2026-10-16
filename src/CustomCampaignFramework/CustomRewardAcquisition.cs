using System;
using System.Linq;
using CampaignCore;
using Data;
using HarmonyLib;
using Rogue;
using Rogue.Powerups.BenchQuestPowerups;
using Rogue.Upgrades;
using State;
using Tape2Tape;
using Tape2Tape.Hockey.UI;
using UnityEngine;

namespace EndlessMode;

internal static class CustomRewardAcquisition
{
    internal static void Notify(Relic relic, CustomRewardDefinition rule, ForwardData source, TeamData team)
        => NotificationQueue.Instance?.Queue(new NotificationData {
            Type = ENotificationType.Generic, Timing = ENotificationTiming.WorldMap,
            Title = rule.Name, TitleIcon = relic.sprite, Description = rule.RulesText,
            SourceSkater = source, SourceTeam = team, PowerUpOrRelic = relic
        });

    internal static void Donate(BenchwarmerStrip relic, CustomRewardDefinition rule, CampaignState state)
    {
        var team = state?.runData?.teamData;
        if (team == null) return;
        var roster = team.GetForwards(false).ToArray();
        // Native Organ Donor selects category 2, not whichever players happen
        // to be sitting on the bench in this match.
        var donors = roster.Where(f => f.skaterCategory == SkaterCategory.Benchwarmer).ToList();
        for (int i = donors.Count - 1; i > 0; i--) {
            int j = UnityEngine.Random.Range(0, i + 1);
            (donors[i], donors[j]) = (donors[j], donors[i]);
        }
        donors = donors.Take(2).ToList();
        var recipients = roster.Where(f => !donors.Any(d => d.Pointer == f.Pointer)).ToArray();
        if (donors.Count == 0 || recipients.Length == 0) return;
        if (rule.Effect == CustomRewardEffects.DonateBenchAttribute) {
            int total = donors.Sum(f => rule.Stat switch {
                "Shot Power" => f.shotPower, "Accuracy" => f.shotAccuracy,
                "Speed" => f.speed, _ => f.checking
            });
            foreach (var donor in donors) {
                switch (rule.Stat) {
                    case "Shot Power": donor.SetShotPower(0); break;
                    case "Accuracy": donor.SetShotAccuracy(0); break;
                    case "Speed": donor.SetSpeed(0); break;
                    default: donor.SetChecking(0); break;
                }
            }
            var stat = rule.Stat switch { "Shot Power" => BasicStats.ShotPower,
                "Accuracy" => BasicStats.ShotAccuracy, "Speed" => BasicStats.Speed, _ => BasicStats.Checking };
            foreach (var recipient in recipients) recipient.IncrementStats(stat, total / recipients.Length, true, true);
        } else {
            foreach (var donor in donors) {
                var talents = donor.powerups.ToArray().Where(t => t != null && t.TryCast<IBenchQuestTalent>() == null).ToArray();
                foreach (var talent in talents) {
                    donor.powerups.Remove(talent);
                    recipients[UnityEngine.Random.Range(0, recipients.Length)].powerups.Add(talent);
                }
                donor.ResetTalentCache();
            }
            foreach (var recipient in recipients) recipient.ResetTalentCache();
        }
        foreach (var donor in donors) Notify(relic, rule, donor, team);
    }

    internal static void UpgradeAngus(AngusAscension relic, CustomRewardDefinition rule, CampaignState state)
    {
        if (relic._hasUpgraded || state?.runData?.teamData == null) return;
        var squad = state.GetCurrentSquad();
        if (squad == null) return;
        var team = state.runData.teamData;
        var skaters = new Il2CppSystem.Collections.Generic.List<SkaterData>();
        foreach (var f in team.GetForwards(false)) if (squad.IsAngus(f.id)) skaters.Add(f);
        if (skaters.Count == 0) return;
        if (rule.Effect == CustomRewardEffects.UpgradeAngusAbility) {
            foreach (var upgrade in UpgradeHelper.GetUpgradeableAbilities(skaters)) {
                upgrade.Upgrade(); RandomUpgrade.PushUpgradeNotification(upgrade, team);
            }
        } else {
            foreach (var upgrade in UpgradeHelper.GetUpgradeableTalents(skaters)) {
                if (CustomRewardRuntime.IsCustom(upgrade.talent?.id)) continue;
                upgrade.Upgrade(); RandomUpgrade.PushUpgradeNotification(upgrade, team);
            }
        }
        relic._hasUpgraded = true;
    }
}

[HarmonyPatch(typeof(BenchwarmerStrip), nameof(BenchwarmerStrip.OnPickup))]
internal static class CustomRewardBenchDonation
{
    [HarmonyPrefix] private static bool Prefix(BenchwarmerStrip __instance, CampaignState campaignState)
    {
        if (!CustomRewardRuntime.Definitions.TryGetValue(__instance.id ?? "", out var rule)
            || rule.NeedsRedesign || rule.Effect != CustomRewardEffects.DonateBenchAttribute && rule.Effect != CustomRewardEffects.DonateBenchTalents) return true;
        CustomRewardRuntime.Safe(() => CustomRewardAcquisition.Donate(__instance, rule, campaignState));
        return false;
    }
}

[HarmonyPatch(typeof(AngusAscension), nameof(AngusAscension.OnPickup))]
internal static class CustomRewardAngusUpgrades
{
    [HarmonyPrefix] private static bool Prefix(AngusAscension __instance, CampaignState campaignState)
    {
        if (!CustomRewardRuntime.Definitions.TryGetValue(__instance.id ?? "", out var rule)
            || rule.NeedsRedesign || rule.Effect != CustomRewardEffects.UpgradeAngusTalents && rule.Effect != CustomRewardEffects.UpgradeAngusAbility) return true;
        CustomRewardRuntime.Safe(() => CustomRewardAcquisition.UpgradeAngus(__instance, rule, campaignState));
        return false;
    }
}

// Do not patch ForwardStatBonusOnPickup.IsHidden. In the audited native build
// it shares the constant-true body with IsPickupOnly and thousands of unrelated
// methods (including static methods). A Harmony instance hook there reads
// unrelated pointers as talents and crashes startup. CustomTalentPool supplies
// authored pickup rewards explicitly; keep the native hidden/pickup-only flags
// so acquisition consumes the reward instead of retaining and reapplying it.
