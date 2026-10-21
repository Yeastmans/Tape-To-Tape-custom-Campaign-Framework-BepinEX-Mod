using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using CampaignCore;
using Data;
using HarmonyLib;
using Rogue;
using Rogue.BenchSnapshots;
using Rogue.Powerups.Chaos;
using Rogue.Relics;
using Rogue.Relics.Controllers;
using Rogue.Upgrades;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EndlessMode;

// Use native owned assets where the component is itself a persisted lifecycle.
// Strip bundled attributes before native controllers or pickup methods see it.
internal static class CustomRewardLifecycle
{
    private static T Clone<T>() where T : Object
    {
        var template = Resources.FindObjectsOfTypeAll<T>().FirstOrDefault(v => v != null
            && (v.TryCast<PowerUp>() is not { } p || !CustomRewardRuntime.IsCustom(p.id))
            && (v.TryCast<Relic>() is not { } r || !CustomRewardRuntime.IsCustom(r.id)));
        return template == null ? null : Object.Instantiate(template);
    }
    internal static Talent CreateTalent(CustomRewardDefinition d)
    {
        if (d.Effect == CustomRewardEffects.DefenseTeleport) return Clone<DefenseTeleportPowerup>();
        if (d.Effect == CustomRewardEffects.ReboundCriticals) {
            var item = Clone<FastRebound>(); if (item != null) { item.oddsIncrease = d.Amount; item.timeSinceLastShot = d.Seconds; } return item;
        }
        if (CustomRewardEffects.IsTransferHelmet(d.Effect)) return CreateHelmet(d);
        if (d.Effect == CustomRewardEffects.RandomMatchAttribute) {
            var item = Clone<StatStuffer>(); if (item != null) item.bonusPerGame = d.Amount; return item;
        }
        if (d.Effect == CustomRewardEffects.CenterDefenderChecks) return Clone<CenterBonecrusherTalent>();
        if (d.Effect == CustomRewardEffects.LiquidateTalents) {
            var item = Clone<Liquidation>(); if (item != null) item.overallBonus = 0; return item;
        }
        if (d.Effect == CustomRewardEffects.FogScoreboard) return Clone<Alzheimer>();
        if (d.Effect == CustomRewardEffects.PickupAttribute) {
            var item = Clone<Tape2Tape.Hockey.ForwardStatBonusOnPickup>();
            if (item != null) { item.m_Stat = d.Stat switch { "Speed" => EForwardStat.Speed, "Accuracy" => EForwardStat.Accuracy, "Checking" => EForwardStat.Checking, "Shot Power" => EForwardStat.ShotPower, _ => EForwardStat.Overall }; item.m_StatBonus = d.Amount; }
            return item;
        }
        if (d.Effect == CustomRewardEffects.ScorelessExpiry) {
            var item = Clone<MonkeysPaw>(); if (item != null) { item.BONUS = 0; item.HasScoredThisGame = false; } return item;
        }
        if (d.Effect == CustomRewardEffects.UpgradeOwnTalents) {
            var item = Clone<MoldPowerup>(); if (item != null) item.ovrPenalty = 0; return item;
        }
        if (d.Effect == CustomRewardEffects.AlternateOverall) {
            var item = Clone<SplitPersonality>();
            if (item != null) { item.baseBonus = item.upgradeBonus = d.Amount; item._isBoosted = true; }
            return item;
        }
        if (d.Effect == CustomRewardEffects.RetainPowers) return Clone<PowerUpRetentionTalent>();
        return null;
    }
    private static Talent CreateHelmet(CustomRewardDefinition d)
    {
        LoseHelmet item = d.Effect switch {
            CustomRewardEffects.HeavyHelmetAttribute => Clone<HeavyHelmet>(),
            CustomRewardEffects.IronHelmetAttribute => Clone<IronHelmet>(),
            CustomRewardEffects.PropellerHelmetAttribute => Clone<PropellerHelmet>(),
            _ => Clone<LancelovHelmet>()
        };
        if (item?.helmet == null) return null;
        var helmet = Object.Instantiate(item.helmet);
        var template = item.helmet.statusEffects?.ToArray().Select(s => s?.TryCast<ForwardStatBonusEffect>()).FirstOrDefault(s => s != null);
        if (template == null) { Object.Destroy(helmet); Object.Destroy(item); return null; }
        var attribute = Object.Instantiate(template);
        attribute.checkingBonus = d.Stat == "Checking" ? d.Amount : 0;
        attribute.shotPowerBonus = d.Stat == "Shot Power" ? d.Amount : 0;
        attribute.shotAccuracyBonus = d.Stat == "Accuracy" ? d.Amount : 0;
        attribute.speedBonus = d.Stat == "Speed" ? d.Amount : 0;
        attribute.hideFlags = HideFlags.DontUnloadUnusedAsset;
        helmet.id = "CCF_Helmet_" + d.Id; helmet.name = d.Name;
        helmet.statusEffects = new Il2CppSystem.Collections.Generic.List<StatusEffectDefinition>();
        helmet.statusEffects.Add(attribute); helmet.hideFlags = HideFlags.DontUnloadUnusedAsset;
        item.helmet = helmet; item.upgradeHelmet = helmet;
        return item;
    }
    internal static Relic CreateRelic(CustomRewardDefinition d)
    {
        if (d.Effect == CustomRewardEffects.AngusZero) return Clone<Rogue.CanadaCurses.CanadaCurseAngusZeroOvr>();
        if (d.Effect == CustomRewardEffects.OpposingBonecrusher) return Clone<Rogue.CanadaCurses.CanadaCurseOpponentBonecrusher>();
        if (d.Effect == CustomRewardEffects.OpposingBriefcase) return Clone<Rogue.CanadaCurses.CanadaCurseOpponentBriefcase>();
        if (d.Effect == CustomRewardEffects.FreezeOwnRelics) return Clone<Rogue.CanadaCurses.CanadaCurseFreezeTwoRelics>();
        if (d.Effect == CustomRewardEffects.FrogOwnPair) return Clone<Rogue.CanadaCurses.CanadaCurseFrogTwo>();
        if (d.Effect == CustomRewardEffects.MinimumTeamSize) return Clone<Rogue.CanadaCurses.CanadaCurseXsTeam>();
        if (d.Effect == CustomRewardEffects.BlueLineLaunch) { var item = Clone<BlueLineLaunchRelic>(); if (item != null) item.launchForce = d.Amount; return item; }
        if (CustomRewardEffects.IsBlueLineKnockout(d.Effect)) {
            var item = Clone<BlueLineKnockoutRelic>();
            if (item != null) { item.mode = d.Effect == CustomRewardEffects.DefensiveBlueLineKnockout ? BlueLineKnockoutMode.DefensiveEntry : BlueLineKnockoutMode.OffensiveEntry; item.knockoutDuration = d.Seconds; }
            return item;
        }
        if (d.Effect == CustomRewardEffects.KnockoutDuration) { var item = Clone<SnoozeAlarm>(); if (item != null) { item.baseModifier = d.Amount / 100f; item.upgradeModifier = 0; } return item; }
        if (d.Effect == CustomRewardEffects.ParticipationBonus) {
            var item = Clone<ParticipationTrophyRelic>(); if (item != null) { item.baseOvrBonus = item.upgradedOvrBonus = d.Amount; item.State = new ParticipationTrophyState(); item.State.forwardOvrBonuses = new Il2CppSystem.Collections.Generic.Dictionary<string, int>(); } return item;
        }
        if (d.Effect == CustomRewardEffects.BreakawayShotPenalty || d.Effect == CustomRewardEffects.BreakawayShotFall) return Clone<Rogue.Relics.ChaosRelics.YipsRelic>();
        if (d.Effect == CustomRewardEffects.OpeningInjuries) { var item = Clone<NutJob>(); if (item != null) item.numOfGuaranteedInjury = d.Amount; return item; }
        if (d.Effect == CustomRewardEffects.CyclicInjuries) { var item = Clone<EggTimer>(); if (item != null) item.knockoutsRequired = d.Amount; return item; }
        if (d.Effect == CustomRewardEffects.PuckInjuries) { var item = Clone<HardBoiledEgg>(); if (item != null) item.injuryChance = d.Amount / 100f; return item; }
        if (d.Effect == CustomRewardEffects.VoodooAttribute) { var item = Clone<VoodooDoll>(); if (item != null) item.bonus = -d.Amount; return item; }
        if (d.Effect == CustomRewardEffects.OpeningCriticals) {
            var item = Clone<DryNoodle>(); if (item != null) { item.shots = item.upgradeShots = item.shotsLeft = d.Amount; } return item;
        }
        if (d.Effect == CustomRewardEffects.HealedInjuryBonus) {
            var item = Clone<Stethoscope>();
            if (item != null) { item.baseOvrBonus = item.upgradedOvrBonus = d.Amount; item.InjuriesHealed = 0; item.forwardOvrBonuses = new Il2CppSystem.Collections.Generic.Dictionary<string, int>(); }
            return item;
        }
        if (d.Effect == CustomRewardEffects.BenchCapacity) return Clone<RandomTalentBenchBonus>();
        if (d.Effect == CustomRewardEffects.DonateBenchAttribute || d.Effect == CustomRewardEffects.DonateBenchTalents) return Clone<BenchwarmerStrip>();
        if (d.Effect == CustomRewardEffects.UpgradeAngusTalents || d.Effect == CustomRewardEffects.UpgradeAngusAbility) {
            var item = Clone<AngusAscension>(); if (item != null) item._hasUpgraded = false; return item;
        }
        if (d.Effect == CustomRewardEffects.PostGoal) return Clone<FalseGoalOnPost>();
        if (d.Effect == CustomRewardEffects.GrantSpeedTalent) return Clone<BlackCoffee>();
        if (d.Effect == CustomRewardEffects.GrantCheckingTalent) return Clone<CrashTestDummy>();
        if (d.Effect == CustomRewardEffects.GrantDefenseTalent) return Clone<InkBottle>();
        if (d.Effect == CustomRewardEffects.GrantShootingTalent) return Clone<CompanionPuck>();
        if (d.Effect == CustomRewardEffects.GrantSlapshotTalent) return Clone<SlapshotPlushie>();
        if (d.Effect == CustomRewardEffects.ShareTalent) return Clone<HotCake>();
        if (d.Effect == CustomRewardEffects.FrogHelmets) return Clone<FrogGoldenHelmetRelic>();
        if (d.Effect == CustomRewardEffects.OctopusAttacks) {
            var item = Clone<OctopusAttackRelic>(); if (item != null) item.explosionRadius = d.Radius; return item;
        }
        return null;
    }
    internal static bool Rule(string id, string effect, out CustomRewardDefinition d)
        => CustomRewardRuntime.Definitions.TryGetValue(id ?? "", out d) && !d.NeedsRedesign && d.Effect == effect;

    internal static int LiquidatedCount(SkaterData skater)
    {
        int count = 0;
        if (skater?.powerups != null) foreach (var talent in skater.powerups) {
            var source = talent?.TryCast<Liquidation>();
            if (source != null && !source.PayoffSuppressed && source.State != null) count += Math.Max(0, source.State.liquidatedTalents);
        }
        return count;
    }
    private static string RetainedPath {
        get {
            string profile = ProfileData.Instance?.fileName;
            if (string.IsNullOrEmpty(profile)) return null;
            string id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(profile)));
            return Path.Combine(Plugin.ModContentRoot, "custom_reward_retained", id + ".txt");
        }
    }
    internal static bool LoadRetained(IDictionary<string, CustomRewardDefinition> definitions)
    {
        if (Plugin.IsDefaultMode || !File.Exists(RetainedPath)) return true;
        try {
            foreach (var d in CustomRewardExport.Read(new CampaignDocument(File.ReadAllText(RetainedPath))))
                if (!definitions.ContainsKey(d.Id)) definitions[d.Id] = d;
            return true;
        } catch (Exception ex) { CustomRewardRuntime.DefinitionSourceUnavailable(); Plugin.Log.LogError("[CustomRewards] Retained bench definitions: " + ex.Message); return false; }
    }
    internal static void StoreRetained()
    {
        if (Plugin.IsDefaultMode || RetainedPath == null || BenchSnapshotService.BenchSnapshots == null) return;
        var retained = new Dictionary<string, CustomRewardDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in BenchSnapshotService.BenchSnapshots)
            if (pair.Value?.powerupIds != null) foreach (string id in pair.Value.powerupIds)
                if (CustomRewardRuntime.Definitions.TryGetValue(id, out var d)) retained[id] = d;
        var document = new CampaignDocument(""); CustomRewardExport.Embed(document, retained.Values);
        Directory.CreateDirectory(Path.GetDirectoryName(RetainedPath));
        string temp = RetainedPath + ".tmp";
        File.WriteAllText(temp, document.ToString()); File.Move(temp, RetainedPath, true);
    }
}

[HarmonyPatch(typeof(BenchSnapshotService), nameof(BenchSnapshotService.Restore))]
internal static class CustomRewardRetentionRestore
{
    [HarmonyPrefix] private static void Prefix() => CustomRewardRuntime.Safe(() => {
        // Profile identity is available here even if asset lookup began before
        // the profile finished loading. Restore resolves saved native IDs next.
        CustomRewardRuntime.EnsureAssets();
        CustomRewardLifecycle.LoadRetained(CustomRewardRuntime.Definitions);
        CustomRewardRuntime.EnsureAssets();
    });
}

[HarmonyPatch(typeof(MoldPowerup), nameof(MoldPowerup.OnPickup))]
internal static class CustomRewardPickupUpgrades
{
    [HarmonyPrefix] private static bool Prefix(MoldPowerup __instance, SkaterData skaterData)
    {
        if (!CustomRewardLifecycle.Rule(__instance.id, CustomRewardEffects.UpgradeOwnTalents, out _)) return true;
        CustomRewardRuntime.Safe(() => {
            foreach (var upgrade in UpgradeHelper.GetUpgradeableTalents(skaterData))
                if (!CustomRewardRuntime.IsCustom(upgrade.talent?.id)) upgrade.Upgrade();
        });
        return false;
    }
}

[HarmonyPatch(typeof(PowerUpRetentionTalent), "get_RetentionCount")]
internal static class CustomRewardRetentionCount
{
    [HarmonyPostfix] private static void Postfix(PowerUpRetentionTalent __instance, ref int __result)
    {
        if (CustomRewardLifecycle.Rule(__instance.id, CustomRewardEffects.RetainPowers, out var d)) __result = d.Amount;
    }
}

[HarmonyPatch(typeof(BenchSnapshotService), nameof(BenchSnapshotService.Store))]
internal static class CustomRewardRetentionSnapshot
{
    [HarmonyPostfix] private static void Postfix() => CustomRewardRuntime.Safe(CustomRewardLifecycle.StoreRetained);
}

[HarmonyPatch(typeof(HotCakeController), "AddEffect")]
internal static class CustomRewardSharedTalentScope
{
    [ThreadStatic] internal static bool Active;
    [HarmonyPrefix] private static void Prefix(HotCakeController __instance, out bool __state)
    {
        __state = Active;
        Active = CustomRewardLifecycle.Rule(__instance.HotCakeRelic?.id, CustomRewardEffects.ShareTalent, out _);
    }
    [HarmonyFinalizer] private static void Finalizer(bool __state) => Active = __state;
}

[HarmonyPatch(typeof(HotCakeController), "GetEligibleTalents")]
internal static class CustomRewardSharedTalentEligibility
{
    [HarmonyPostfix] private static void Postfix(Il2CppSystem.Collections.Generic.List<Talent> __result)
    {
        if (!CustomRewardSharedTalentScope.Active || __result == null) return;
        for (int i = __result.Count - 1; i >= 0; i--)
            if (CustomRewardRuntime.IsCustom(__result[i]?.id)) __result.RemoveAt(i);
    }
}
