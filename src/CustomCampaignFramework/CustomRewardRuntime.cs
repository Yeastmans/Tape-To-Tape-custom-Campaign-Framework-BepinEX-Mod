using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using CampaignCore;
using Data;
using Events;
using HarmonyLib;
using Rogue;
using Rogue.Powerups.Repository;
using Rogue.Relics.Repository;
using Skaters;
using Skaters.Injuries;
using Tape2Tape;
using Tape2Tape.Hockey.UI;
using UnityEngine;
using Object = UnityEngine.Object;
using NativeGoalie = global::Goaltender.Goaltender;

namespace EndlessMode;

internal static class CustomRewardRuntime
{
    private static bool mercyWon;
    private static readonly HashSet<IntPtr> teamsWithOpposingKnockouts = new();
    internal static readonly Dictionary<string, CustomRewardDefinition> Definitions = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Talent> talents = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Relic> relics = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<IntPtr, Forward> forwards = new();
    private static readonly Dictionary<IntPtr, Skater> actors = new();
    private static readonly HashSet<string> dispatching = new();
    private sealed class TimedTag { internal Skater Skater; internal StatusTag Tag; internal float Until; internal string Reward, Owner; }
    private static readonly Dictionary<string, TimedTag> tags = new();
    private static float nextRefresh;
    private static readonly HashSet<IntPtr> boundStats = new();
    private static readonly List<object> delegates = new();
    private static readonly List<Action> unsubscribe = new();
    private static readonly List<Action> removeModifiers = new();
    private static readonly CustomRewardProgress progress = new();
    private static bool loaded, eventsBound, creating;
    private static string run = "";
    private static bool inMatch;
    [ThreadStatic] private static PassingArg currentPass;
    internal static bool InMatch => inMatch && !completed;
    private static bool completed;
    private static IntPtr match;
    private static string matchStartProgress = "";
    internal static bool IsCustom(string id) => id != null && Definitions.ContainsKey(id);
    internal static bool OwnsDataEffect(SkaterData player, string effect) {
        if (player == null) return false;
        EnsureAssets();
        return player.powerups.HasMatch(t => t != null && !t.PayoffSuppressed
            && Definitions.TryGetValue(t.id, out var rule) && !rule.NeedsRedesign && rule.Effect == effect);
    }
    internal static void RegisterExport(CampaignDocument document)
    {
        // New exports can arrive while the game is already open. Resolve the
        // complete companion before native model references are reconstructed.
        var items = CustomRewardExport.Read(document).ToArray();
        if (items.Length == 0) return;
        foreach (var item in items) CustomRewardExport.ValidatePlayNow(item);
        EnsureAssets();
        foreach (var item in items)
            if (Definitions.TryGetValue(item.Id, out var existing) && existing.ToDocument().ToString() != item.ToDocument().ToString())
                throw new ArgumentException("Conflicting exported custom reward: " + item.Name);
        foreach (var item in items) Definitions[item.Id] = item;
        EnsureAssets();
    }
    internal static bool HasMatch<T>(this Il2CppSystem.Collections.Generic.List<T> list, Func<T, bool> predicate) {
        if (list != null) for (int i = 0; i < list.Count; i++) if (predicate(list[i])) return true;
        return false;
    }
    internal static void EnsureAssets()
    {
        if (creating) return;
        creating = true;
        try {
            if (!loaded) {
                var snapshot = new Dictionary<string, CustomRewardDefinition>(StringComparer.OrdinalIgnoreCase);
                if (!Plugin.IsDefaultMode) {
                    var authored = new Dictionary<string, CustomRewardDefinition>(StringComparer.OrdinalIgnoreCase);
                    foreach (var d in CustomRewardDefinition.LoadFolder(Path.Combine(Plugin.ModFolder, CustomRewardDefinition.Folder))) authored[d.Id] = d;
                    // Imported players/teams can carry definitions before the
                    // campaign has next been saved in Studio. Explicit campaign
                    // copies take precedence over these embedded snapshots.
                    foreach (string file in Directory.EnumerateFiles(Plugin.ModFolder, "*.txt", SearchOption.AllDirectories)
                        .Where(f => !Path.GetRelativePath(Plugin.ModFolder, f).Split(Path.DirectorySeparatorChar).SkipLast(1)
                            .Any(p => p.StartsWith('_') || p.StartsWith('.') || p is "custom_reward_progress" or "map_backups")))
                        foreach (var d in CustomRewardExport.Read(new CampaignDocument(File.ReadAllText(file))))
                            if (!authored.ContainsKey(d.Id)) CustomRewardExport.AddSnapshot(snapshot, d);
                    foreach (var d in authored.Values) snapshot[d.Id] = d;
                }
                CustomRewardLifecycle.LoadRetained(snapshot);
                string exports = Path.Combine(Plugin.ModContentRoot, "play_now_overrides");
                if (Directory.Exists(exports)) foreach (string file in Directory.EnumerateFiles(exports, "*.txt")) {
                    try {
                        var items = CustomRewardExport.Read(new CampaignDocument(File.ReadAllText(file))).ToArray();
                        foreach (var d in items) {
                            CustomRewardExport.ValidatePlayNow(d);
                            if (snapshot.TryGetValue(d.Id, out var previous) && previous.ToDocument().ToString() != d.ToDocument().ToString())
                                throw new ArgumentException("Conflicting custom reward identity in export: " + d.Name);
                        }
                        foreach (var d in items) snapshot[d.Id] = d;
                    } catch (Exception ex) { Plugin.Log.LogError("[CustomRewards] Export " + Path.GetFileName(file) + ": " + ex.Message); }
                }
                Definitions.Clear(); foreach (var pair in snapshot) Definitions.Add(pair.Key, pair.Value);
                loaded = true;
            }
            if (Definitions.Count == 0) return;
            bool added = false;
            foreach (var d in Definitions.Values) {
                if (d.IsRelic && !relics.ContainsKey(d.Id)) {
                    Relic r;
                    if (d.Trigger == "Puck fart timer") {
                        r = CustomRewardFartTimers.CreateRelic(d); if (r == null) continue;
                    } else if (CustomRewardEffects.IsLifecycleEffect(d.Effect)) {
                        r = CustomRewardLifecycle.CreateRelic(d); if (r == null) continue;
                    } else if (d.Effect == CustomRewardEffects.CopyOpponentRelics || d.Effect == CustomRewardEffects.StealOpponentRelics) {
                        bool copies = d.Effect == CustomRewardEffects.CopyOpponentRelics;
                        var template = Resources.FindObjectsOfTypeAll<Rogue.Relics.RelicStealer>().FirstOrDefault(v => v != null && !IsCustom(v.id) && v.CopiesRelics == copies);
                        if (template == null) continue;
                        var transfer = Object.Instantiate(template); transfer.SetAffectedRelics(new Il2CppSystem.Collections.Generic.List<Relic>()); r = transfer;
                    } else if (d.Effect == CustomRewardEffects.FreezeOpponentRelic) {
                        var template = Resources.FindObjectsOfTypeAll<Rogue.Relics.RelicFreezer>().FirstOrDefault(v => v != null && !IsCustom(v.id));
                        if (template == null) continue;
                        r = Object.Instantiate(template);
                    } else if (d.Effect == CustomRewardEffects.GoalPlanks) {
                        var template = Resources.FindObjectsOfTypeAll<GoalPlankRelic>().FirstOrDefault(v => v != null && !IsCustom(v.id));
                        if (template?.goalPlankPrefab == null) continue;
                        r = Object.Instantiate(template);
                    } else if (d.Effect == CustomRewardEffects.BreakawayAd) {
                        var template = Resources.FindObjectsOfTypeAll<AnnoyingAd>().FirstOrDefault(v => v != null && !IsCustom(v.id));
                        if (template?.adPrefabRef == null) continue;
                        r = Object.Instantiate(template);
                    } else if (d.Effect == CustomRewardEffects.PossessionExplosion) {
                        var template = Resources.FindObjectsOfTypeAll<HotPotato>().FirstOrDefault(v => v != null && !IsCustom(v.id));
                        if (template == null) continue;
                        r = Object.Instantiate(template);
                    } else {
                        // A neutral carrier has no asset dependencies. The game's
                        // current scene need not contain a CornDog donor asset.
                        var neutral = ScriptableObject.CreateInstance<CornDog>();
                        neutral.statBonus = neutral.upgradedStatBonus = 0; r = neutral;
                    }
                    r.id = d.Id; r.name = "CCF_Reward_" + d.Id; r.level = d.Effect == CustomRewardEffects.BreakawayShotFall ? 2 : CustomRewardEffects.IsCategoryGrant(d.Effect) ? d.Amount : 1;
                    r.relicName = d.Name; r.description = d.FullDescription; r.upgradedDescription = ""; r.statBonusDescription = "";
                    r.sprite = Icon(d); r.hasUpgradeText = false; r.isHidden = false;
                    r.category = d.Effect == CustomRewardEffects.StatChange ? RelicCategory.OnlyStats : (RelicCategory)0;
                    r.hideFlags = HideFlags.DontUnloadUnusedAsset; relics.Add(d.Id, r); added = true;
                } else if (!d.IsRelic && !talents.ContainsKey(d.Id)) {
                    Talent t;
                    if (CustomRewardEffects.IsLifecycleEffect(d.Effect)) {
                        t = CustomRewardLifecycle.CreateTalent(d); if (t == null) continue;
                    } else if (d.Effect == CustomRewardEffects.HungerRage) {
                        var template = Resources.FindObjectsOfTypeAll<Feed>().FirstOrDefault(v => v != null && !IsCustom(v.id));
                        if (template == null) continue;
                        var feed = Object.Instantiate(template); feed.baseTime = feed.upgradeTime = d.Seconds; t = feed;
                    } else if (d.Effect == CustomRewardEffects.RandomAutoShot) {
                        var template = Resources.FindObjectsOfTypeAll<Spasm>().FirstOrDefault(v => v != null && !IsCustom(v.id));
                        if (template == null) continue;
                        var spasm = Object.Instantiate(template);
                        spasm.TryToSpasmEverySeconds = d.Seconds; spasm.SpasmChance = d.Amount / 100f; spasm.DelayBeforeShootingMs = d.ShotDelayMilliseconds;
                        t = spasm;
                    } else if (d.Effect == CustomRewardEffects.PlayedMatchContract) {
                        // A contract is a single native lifecycle, including its
                        // saved owner, Nurse removal and remaining-game counter.
                        // Zero Short Contract's separate attribute component.
                        if (d.Kind == CustomRewardDefinition.CustomChaosTalents) {
                            var template = Resources.FindObjectsOfTypeAll<ShortContract>().FirstOrDefault(v => v != null && !IsCustom(v.id));
                            if (template == null) continue;
                            var contract = Object.Instantiate(template); contract.bonus = 0; contract.numOfGames = d.Amount; t = contract;
                        } else {
                            var template = Resources.FindObjectsOfTypeAll<TemporaryContract>().FirstOrDefault(v => v != null && !IsCustom(v.id));
                            if (template == null) continue;
                            var contract = Object.Instantiate(template); contract.numOfGames = d.Amount; t = contract;
                        }
                    } else {
                        // BasicStatsBonus is a native ScriptableObject type, but
                        // no instance is guaranteed to be loaded at the title or
                        // squad screen. Build our own neutral carrier so custom
                        // talents resolve before player previews/deserialization.
                        var neutral = ScriptableObject.CreateInstance<BasicStatsBonus>(); neutral.delta = 0; t = neutral;
                    }
                    t.id = d.Id; t.name = "CCF_Reward_" + d.Id; t.level = d.Effect == CustomRewardEffects.CenterDefenderChecks ? d.Amount : 1;
                    t.powerupName = d.Name; t.description = d.FullDescription; t.upgradedDescription = ""; t.statBonusDescription = "";
                    t.icon = Icon(d); t.hasUpgradeText = false; t.displayInUI = true;
                    t.category = (d.Effect == CustomRewardEffects.StatChange ? TalentCategory.OnlyStats : (TalentCategory)0)
                        | (d.IsGoalie ? TalentCategory.Goalie | TalentCategory.UsedInCampaignGoaliePool : (TalentCategory)0)
                        | (d.Kind == CustomRewardDefinition.CustomChaosTalents ? TalentCategory.Chaos : (TalentCategory)0);
                    t.hideFlags = HideFlags.DontUnloadUnusedAsset; talents.Add(d.Id, t); added = true;
                }
            }
            // Assets resolve by ID for native deserialization; they are deliberately
            // not appended to the base-game random pools.
            if (added) {
                PatchBossLaunchMatch.AllRelicCache = null;
                Plugin.Log.LogInfo($"[CustomRewards] Registered {talents.Count} custom talents and {relics.Count} custom relics ({Definitions.Count} definitions).");
            }
        } catch (Exception ex) { Plugin.Log.LogError("[CustomRewards] Loading definitions: " + ex); }
        finally { creating = false; }
    }
    private static Sprite Icon(CustomRewardDefinition d)
    {
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!ImageConversion.LoadImage(texture, Convert.FromBase64String(d.IconPng))) throw new ArgumentException("Invalid icon: " + d.Name);
        texture.hideFlags = HideFlags.DontUnloadUnusedAsset;
        var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f));
        sprite.hideFlags = HideFlags.DontUnloadUnusedAsset; return sprite;
    }
    internal static Talent FindTalent(string id, bool clone) { EnsureAssets(); return talents.TryGetValue(id ?? "", out var t) ? clone ? Object.Instantiate(t) : t : null; }
    internal static Relic FindRelic(string id, bool clone) { EnsureAssets(); return relics.TryGetValue(id ?? "", out var r) ? clone ? Object.Instantiate(r) : r : null; }
    internal static bool Has(SkaterData skater, string id) => skater?.powerups != null
        && (!Definitions.TryGetValue(id, out var d) || CustomRewardAssignments.FitsPlayer(d, skater.TryCast<GoaltenderData>() != null))
        && skater.powerups.HasMatch(t => t != null && t.id == id);
    internal static bool Has(TeamData team, string id) => team?.relics != null
        && (!Definitions.TryGetValue(id, out var d) || d.IsRelic) && team.relics.HasMatch(r => r != null && r.id == id);
    private static string Player(Skater f) => "player:" + f._skaterData.id;
    private static string Team(Skater f) => "team:" + f.Team._teamData.id;
    internal static int RemainingCharges(TeamData team, CustomRewardDefinition rule) => Math.Max(0, rule.Amount - progress.ChargesSpent("team:" + team.id, rule.Id));
    internal static bool SpendCharge(TeamData team, CustomRewardDefinition rule) => progress.SpendCharge("team:" + team.id, rule.Id, rule.Amount);
    private static string StateFile()
    {
        using var sha = SHA256.Create();
        string name = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(run))).Replace("-", "");
        return Path.Combine(Plugin.ModFolder, "custom_reward_progress", name + ".txt");
    }
    internal static void EnsureRun()
    {
        // The campaign map saves before a match GameManager exists. Its static
        // GameType getter dereferences Instance, so it cannot classify map saves.
        // Keep the campaign run there; only an actual non-Rogue match clears it.
        string id = GameManager.Instance == null || GameManager.GameType == ScriptableStates.GameType.Rogue
            ? CampaignUtil.CampaignState?.runData?.runID ?? "" : "";
        if (id == run) return;
        run = id; progress.Clear();
        if (run.Length == 0) return;
        try { string file = StateFile(); if (File.Exists(file)) progress.Load(File.ReadAllText(file)); }
        catch (Exception ex) { Plugin.Log.LogError("[CustomRewards] Loading run progress: " + ex); }
    }
    internal static void Save()
    {
        EnsureRun(); if (run.Length == 0 || Definitions.Count == 0) return;
        ObserveOwnership(CampaignUtil.CampaignState?.runData?.teamData);
        try {
            string file = StateFile(); Directory.CreateDirectory(Path.GetDirectoryName(file));
            // A mid-match menu save must not commit gains from an abandoned match.
            string temp = file + ".tmp"; File.WriteAllText(temp, inMatch && !completed ? matchStartProgress : progress.Save());
            if (File.Exists(file)) File.Replace(temp, file, null); else File.Move(temp, file);
        } catch (Exception ex) { Plugin.Log.LogError("[CustomRewards] Saving run progress: " + ex); }
    }
    internal static void ObserveOwnership(TeamData team) {
        if (team == null) return;
        var ids = new List<string>();
        if (team.relics != null) foreach (var r in team.relics) if (r != null && IsCustom(r.id)) ids.Add(r.id);
        progress.ObserveOwnership("team:" + team.id, ids);
        foreach (var player in team.GetAllSkaters()) if (player != null) {
            ids.Clear();
            if (player.powerups != null) foreach (var t in player.powerups) if (t != null && IsCustom(t.id)) ids.Add(t.id);
            progress.ObserveOwnership("player:" + player.id, ids);
        }
    }
    internal static long Acquired(TeamData team, CustomRewardDefinition d) => progress.Acquired("team:" + team.id, d.Id);
    internal static long Acquired(SkaterData owner, CustomRewardDefinition d) => progress.Acquired("player:" + owner.id, d.Id);
    internal static long Acquired(Skater recipient, CustomRewardDefinition d) {
        if (d.IsRelic) return Acquired(recipient.Team._teamData, d);
        var sources = d.Target == CustomRewardEffects.Team ? actors.Values.Where(a => a?.Team == recipient.Team && a.TryCast<Forward>()?.IsBenched() != true) : new[] { recipient };
        return sources.Where(s => s != null && s.ArePowerUpsEnabled() && Has(s._skaterData, d.Id) && Condition(d.Trigger, s))
            .Select(s => progress.Acquired(Player(s), d.Id)).DefaultIfEmpty(0).Max();
    }
    internal static void MatchStarted()
    {
        var game = GameManager.Instance;
        if (game == null || (inMatch && match == game.Pointer)) return;
        EndMatch();
        EnsureAssets(); EnsureRun();
        if (Definitions.Count == 0) return;
        match = game.Pointer; matchStartProgress = progress.Save(); progress.BeginMatch(); inMatch = true; completed = false;
        BindEvents();
    }
    internal static void EndMatch()
    {
        if (inMatch && !completed && matchStartProgress.Length > 0) progress.Load(matchStartProgress);
        inMatch = false;
        CustomRewardGoalConfirmation.Clear();
        CustomRewardGoalValues.Clear();
        Safe(CustomRewardClock.Clear);
        Safe(CustomRewardReferees.Clear);
        Safe(CustomRewardSlapshotSlowmo.Clear);
        Safe(CustomRewardContactEffects.Clear);
        Safe(CustomRewardBallChains.Clear);
        Safe(CustomRewardLoadedPucks.Clear);
        CustomRewardTemperature.Clear();
        Safe(CustomRewardCultists.Clear);
        Safe(CustomRewardManualDenial.Clear);
        Safe(CustomRewardBorrowing.Clear);
        Safe(CustomRewardFartTimers.Clear);
        Safe(CustomRewardActionControllers.Clear);
        CustomRewardSkatingEvents.Clear(); teamsWithOpposingKnockouts.Clear(); mercyWon = false;
        Safe(CustomRewardSoccerWall.Clear);
        Safe(CustomRewardNativeEffects.Clear);
        CustomRewardMovementEffects.Clear();
        Safe(CustomRewardLiveRules.Clear);
        Safe(CustomRewardEquipmentEffects.Clear);
        Safe(CustomRewardAttraction.Clear);
        Safe(CustomRewardLooseAttraction.Clear);
        Safe(CustomRewardAbilityReplacement.Clear);
        CustomRewardScaling.Clear(); currentPass = null;
        foreach (var action in unsubscribe) Safe(action);
        foreach (var action in removeModifiers) Safe(action);
        foreach (var tag in tags.Values) if (tag.Skater != null) Safe(() => tag.Skater.statusEffectsManager?.RemoveTag(tag.Tag));
        tags.Clear(); dispatching.Clear(); actors.Clear(); brokenSticks.Clear(); nextRefresh = 0;
        unsubscribe.Clear(); removeModifiers.Clear(); delegates.Clear(); forwards.Clear(); boundStats.Clear(); eventsBound = false;
    }
    internal static void Bind(Forward forward)
    {
        if (forward?.stats == null || forward._forwardData == null || forward.Team?._teamData == null) return;
        MatchStarted(); if (!inMatch) return;
        forwards[forward.Pointer] = forward;
        actors[forward.Pointer] = forward;
        if (!boundStats.Add(forward.stats.Pointer)) return;
        Il2CppSystem.Func<int, int> Modifier(string stat) {
            var fn = (Il2CppSystem.Func<int, int>)(Func<int, int>)(value => {
                try { return (int)Math.Max(0, Math.Min(int.MaxValue, (long)value + Bonus(forward, stat))); }
                catch (Exception ex) { Plugin.Log.LogError("[CustomRewards] Stat evaluation: " + ex.Message); return value; }
            });
            delegates.Add(fn); return fn;
        }
        void Add(Il2CppSystem.Collections.Generic.List<Il2CppSystem.Func<int, int>> list, string stat) {
            CustomRewardScaling.Bind(forward, stat, list);
            var modifier = Modifier(stat); list.Add(modifier); removeModifiers.Add(() => list.Remove(modifier));
        }
        Add(forward.stats.overallShotPowerEvent, "Shot Power"); Add(forward.stats.overallShotAccuracyEvent, "Accuracy");
        Add(forward.stats.overallSpeedEvent, "Speed"); Add(forward.stats.overallCheckingEvent, "Checking");
        forward.stats.UpdateCachedStats();
    }
    internal static void BindGoalie(NativeGoalie goalie)
    {
        if (goalie?.goalieStats == null || goalie._skaterData == null || goalie.Team?._teamData == null) return;
        MatchStarted(); if (!inMatch) return;
        actors[goalie.Pointer] = goalie;
        if (!boundStats.Add(goalie.goalieStats.Pointer)) return;
        void Add(Il2CppSystem.Collections.Generic.List<Il2CppSystem.Func<int, int>> list, string stat) {
            var modifier = (Il2CppSystem.Func<int, int>)(Func<int, int>)(value => (int)Math.Max(0, Math.Min(int.MaxValue, (long)value + Bonus(goalie, stat))));
            delegates.Add(modifier); list.Add(modifier); removeModifiers.Add(() => list.Remove(modifier));
        }
        Add(goalie.goalieStats.overallSkillEvent, "Goalie skill"); Add(goalie.goalieStats.overallRecoveryEvent, "Goalie recovery");
        Add(goalie.goalieStats.overallStandingSpeed, "Goalie standing speed"); Add(goalie.goalieStats.overallButterflySpeed, "Goalie butterfly speed"); Add(goalie.goalieStats.overallDepth, "Goalie depth");
    }
    internal static bool StillOwned(string id, string owner)
    {
        if (owner.StartsWith("player:") || owner.Contains("|player:")) return actors.Values.Any(f => f != null
            && (owner == Player(f) || owner == CustomRewardProgress.ScopedRecipient(Player(f), Team(f)))
            && f.ArePowerUpsEnabled() && Has(f._skaterData, id) && f._skaterData.powerups.HasMatch(t => t != null && t.id == id && !t.PayoffSuppressed));
        foreach (var f in actors.Values) if (f != null && f.Team?._teamData != null && Team(f) == owner && Has(f.Team._teamData, id)) return true;
        return false;
    }
    private static readonly HashSet<string> brokenSticks = new();
    [ThreadStatic] private static bool checkingPowerThreshold;
    private static bool HasPowerThreshold(Skater actor)
    {
        if (checkingPowerThreshold || actor?.TryCast<Forward>()?.stats == null) return false;
        try { checkingPowerThreshold = true; return actor.TryCast<Forward>().stats.GetShotPower() >= 50; }
        finally { checkingPowerThreshold = false; }
    }
    internal static bool Condition(string condition, Skater actor) => condition switch {
        "While in blue-line shot range" => actor.TryCast<Forward>() is { } shooter && Rink.Instance.IsInBlueLineShotRange(shooter._rigidbody.position),
        "While classified as a benchwarmer" => actor.TryCast<Forward>()?._forwardData.skaterCategory == SkaterCategory.Benchwarmer,
        "While wearing Golden Helmet against a boss" => actor.TryCast<Forward>()?._forwardData.HasGoldenHelmetForBonuses() == true && actor.Team.otherTeam?.IsBoss() == true,
        "While wearing a special helmet" => actor.TryCast<Forward>() is { } helmetOwner && SpecialHelmetOvrRelicController.HasSpecialHelmet(helmetOwner),
        "While in final 20 seconds" => GameManager.Instance?.timeRemaining?._remainingTime <= 20,
        "While team has no opposing knockouts" => actor.Team != null && !teamsWithOpposingKnockouts.Contains(actor.Team.Pointer),
        "While size L, XL or XXL" => actor.TryCast<Forward>()?._forwardData.skaterSize is SkaterSize.Big or SkaterSize.ExtraBig or SkaterSize.ExtraExtraBig,
        "While Shot Power is at least 50" => HasPowerThreshold(actor),
        "While owned" => true,
        "While holding puck" => Puck.Instance != null && Puck.Instance.skaterPossession == actor,
        "While without puck" => Puck.Instance != null && Puck.Instance.skaterPossession != actor,
        "While enraged" => actor.statusEffectsManager?.IsEnraged() == true,
        "While skating backward" => actor.TryCast<Forward>()?.IsBackwardSkating == true,
        "While stickless" => actor.TryCast<Forward>()?.HasStick == false,
        "While score tied" => actor.Team.Score == actor.Team.otherTeam.Score,
        "While leading" => actor.Team.Score > actor.Team.otherTeam.Score,
        "While moving toward own end" => EndDirection(actor) == 1,
        "While moving toward opposing end" => EndDirection(actor) == -1,
        "While outside period 1" => GameManager.Instance.currentPeriod != 1,
        "While playing" => actor.TryCast<Forward>()?.IsBenched() == false,
        "While trailing" => actor.Team.Score < actor.Team.otherTeam.Score,
        "While opponent has puck" => Puck.Instance?.skaterPossession?.Team == actor.Team.otherTeam,
        "While team has puck" => Puck.Instance?.skaterPossession?.Team == actor.Team,
        "While in period 1" => GameManager.Instance.currentPeriod == 1,
        "While in period 2" => GameManager.Instance.currentPeriod == 2,
        "While in period 3" => GameManager.Instance.currentPeriod == 3,
        "While eye injured" => CustomRewardInjuryRules.HasType(actor, InjuryType.Eye),
        "While shoulder injured" => CustomRewardInjuryRules.HasType(actor, InjuryType.Shoulder),
        "While leg injured" => CustomRewardInjuryRules.HasType(actor, InjuryType.Leg),
        "While arm injured" => CustomRewardInjuryRules.HasType(actor, InjuryType.Arm),
        "While injured" => actor.TryCast<Forward>()?.HasAnyInjury() == true,
        "While wearing helmet" => actor.TryCast<Forward>()?.HasHelmet() == true,
        "While without helmet" => actor.TryCast<Forward>()?.HasHelmet() == false,
        "While wearing Golden Helmet" => actor.TryCast<Forward>()?._forwardData.HasGoldenHelmetForBonuses() == true,
        "While playing centre" => actor.TryCast<Forward>()?.skaterType == SkaterType.Center,
        "While playing wing" => actor.TryCast<Forward>()?.skaterType is SkaterType.LeftWinger or SkaterType.RightWinger,
        "While playing defence" => actor.TryCast<Forward>()?.skaterType is SkaterType.LeftDef or SkaterType.RightDef,
        "While scoreless" => actor.gameStats.goals == 0,
        "While having scored" => actor.gameStats.goals > 0,
        "While in defensive zone" => Rink.Instance != null && Rink.Instance.IsInDefensiveZone(actor),
        "While on a breakaway" => GameManager.PuckState != null && GameManager.PuckState.IsInBreakaway() && GameManager.PuckState.GetBreakawaySkater()?.Pointer == actor.Pointer,
        "While size XS or S" => actor.TryCast<Forward>()?._forwardData.skaterSize is SkaterSize.ExtraSmall or SkaterSize.Small,
        "While size L or XL" => actor.TryCast<Forward>()?._forwardData.skaterSize is SkaterSize.Big or SkaterSize.ExtraBig,
        "While playing against Elite" => actor.Team.otherTeam?._teamData?.IsSpartans() == true,
        "While playing against a boss" => actor.Team.otherTeam?.IsBoss() == true,
        "While team has three or fewer skaters" => actor.Team.PlayingForwards?.Count <= 3,
        "While team has an injured skater" => InjuredPlaying(actor, false) > 0,
        _ => false
    };
    private static int EndDirection(Skater actor) {
        var forward = actor?.TryCast<Forward>();
        return forward?._rigidbody == null || forward.movementManager == null || forward.Team == null ? 0
            : CustomRewardTriggers.EndDirection(forward.Team.isNorth, forward._rigidbody.linearVelocity.z, forward.movementManager.GetIntendedDirection().z);
    }
    private static int InjuredPlaying(Skater source, bool excludeSource)
    {
        int count = 0; var playing = source.Team.PlayingForwards;
        for (int i = 0; playing != null && i < playing.Count; i++)
            if (playing[i] != null && (!excludeSource || playing[i].Pointer != source.Pointer) && playing[i].HasAnyInjury()) count++;
        return count;
    }
    private static int KnockedOutOnIce()
    {
        int count = 0;
        foreach (var team in new[] { GameManager.HomeTeam, GameManager.VisitorTeam }) {
            var playing = team?.PlayingForwards;
            for (int i = 0; playing != null && i < playing.Count; i++)
                if (playing[i]?.CurrentMovementState?.TryCast<Movements.MovementStates.KnockoutState>() != null) count++;
        }
        return count;
    }
    internal static int Bonus(Skater forward, string stat)
    {
        if (!inMatch || forward == null || forward.Team?._teamData == null) return 0;
        string actor = Player(forward), team = Team(forward);
        long total = CustomRewardTemperature.Bonus(forward, stat) + (long)progress.Bonus(actor, team, stat, Time.time, StillOwned) + CustomRewardShotCalculations.Bonus(forward, stat) + CustomRewardChargedShots.Bonus(forward, stat);
        foreach (var d in Definitions.Values) {
            if (d.NeedsRedesign || d.Effect != CustomRewardEffects.StatChange || !CustomRewardTriggers.IsCondition(d.Trigger)) continue;
            bool OwnedTalent(Skater f) => f != null && f.ArePowerUpsEnabled() && Has(f._skaterData, d.Id) && f._skaterData.powerups.HasMatch(t => t != null && t.id == d.Id && !t.PayoffSuppressed) && Condition(d.Trigger, f);
            int Scale(Skater source) {
                if (CustomRewardDefinition.IsPreviousMatchBasis(d.AmountBasis)) return progress.PreviousMatchCount(Player(source), d.Id);
                if (d.AmountBasis == "Per liquidated talent") return CustomRewardLifecycle.LiquidatedCount(source._skaterData);
                if (d.AmountBasis == "Per other owned talent") return Math.Max(0, (source._skaterData.powerups?.Count ?? 0) - 1);
                if (d.AmountBasis == "Per injured playing teammate") return InjuredPlaying(source, true);
                if (d.AmountBasis == "Per goal trailing") return Math.Max(0, source.Team.otherTeam.Score - source.Team.Score);
                if (d.AmountBasis == "Per knocked-out skater on either team") return KnockedOutOnIce();
                return 1;
            }
            int relicOwners = 0;
            if (d.IsRelic) foreach (var sourceTeam in new[] { forward.Team, forward.Team.otherTeam })
                if (sourceTeam?._teamData != null && CustomRewardEffects.StatTeamApplies(d.Target, sourceTeam._teamData.id, forward.Team._teamData.id)
                    && Has(sourceTeam._teamData, d.Id) && Condition(d.Trigger, forward)) relicOwners += Scale(forward);
            if (d.Target == CustomRewardEffects.OtherTeammates && (forward.TryCast<Forward>() == null || forward.TryCast<Forward>().IsBenched())) continue;
            int owners = d.IsRelic ? relicOwners : d.Target == "Whole team" || d.Target == CustomRewardEffects.OtherTeammates
                ? actors.Values.Where(f => f != null && f.Team?._teamData != null && Team(f) == team && f.TryCast<Forward>()?.IsBenched() != true && (d.Target != CustomRewardEffects.OtherTeammates || f.Pointer != forward.Pointer) && OwnedTalent(f)).Sum(Scale) : (OwnedTalent(forward) ? Scale(forward) : 0);
            if (owners == 0) continue;
            if (d.Stat == stat || d.Stat == "Overall" && CustomRewardDefinition.IsOverallStat(stat)) total += (long)d.Amount * owners;
        }
        return (int)Math.Clamp(total, int.MinValue, int.MaxValue);
    }
    internal static IEnumerable<CustomRewardDefinition> ActiveConditions(Skater recipient)
    {
        if (!InMatch || recipient?.Team?._teamData == null) yield break;
        foreach (var d in Definitions.Values) {
            if (d.NeedsRedesign || !CustomRewardTriggers.IsCondition(d.Trigger)) continue;
            if (d.IsRelic) {
                if (Has(recipient.Team._teamData, d.Id) && Condition(d.Trigger, recipient)) yield return d;
            } else {
                var sources = d.Target == CustomRewardEffects.Team ? actors.Values.Where(a => a?.Team == recipient.Team && a.TryCast<Forward>()?.IsBenched() != true) : new[] { recipient };
                foreach (var source in sources) if (source != null && source.ArePowerUpsEnabled() && Has(source._skaterData, d.Id)
                    && source._skaterData.powerups.HasMatch(t => t != null && t.id == d.Id && !t.PayoffSuppressed) && Condition(d.Trigger, source)) yield return d;
            }
        }
    }
    internal static void Signal(string signal, Skater skater, HashSet<string> teamOnce = null, Skater other = null)
    {
        if (!inMatch || completed || skater == null) return;
        var f = skater; if (f._skaterData == null || f.Team?._teamData == null) return;
        EnsureRun();
        if (f.TryCast<Forward>() is { } forward) Bind(forward);
        else if (f.TryCast<NativeGoalie>() is { } goalie) BindGoalie(goalie);
        string actor = Player(f), team = Team(f);
        foreach (var d in Definitions.Values) {
            if (d.NeedsRedesign || d.Trigger != signal && d.ResetOn != signal || !d.IsRelic && !f.ArePowerUpsEnabled()) continue;
            if (d.Effect == CustomRewardEffects.StatChange && d.Target == CustomRewardEffects.Actor
                && d.Stat.StartsWith("Goalie ", StringComparison.Ordinal) != (f.TryCast<NativeGoalie>() != null)) continue;
            bool owned = d.IsRelic ? Has(f.Team._teamData, d.Id) : Has(f._skaterData, d.Id)
                && f._skaterData.powerups.HasMatch(t => t != null && t.id == d.Id && !t.PayoffSuppressed);
            if (!owned) continue;
            if (d.ResetOn == signal) {
                progress.ResetBonus(d, signal, d.IsRelic ? team : CustomRewardProgress.ScopedRecipient(actor, team), d.Target == CustomRewardEffects.Opponent ? null : d.Target == CustomRewardEffects.Team ? team : CustomRewardProgress.ScopedRecipient(actor, team));
                // Older saved bonuses used an unqualified player recipient/owner.
                progress.ResetBonus(d, signal, d.IsRelic ? team : actor, d.Target == CustomRewardEffects.Opponent ? null : d.Target == CustomRewardEffects.Team ? team : actor);
                foreach (var recipient in forwards.Values.Where(v => v != null && (d.Target == CustomRewardEffects.Opponent || (d.Target == CustomRewardEffects.Team ? Team(v) == team : Player(v) == actor)))) recipient.stats?.UpdateCachedStats();
            }
            if (d.Trigger != signal) continue;
            if (d.Effect == CustomRewardEffects.StatChange && d.Target == CustomRewardEffects.Opponent && (other?._skaterData == null || other.Team != (CustomRewardTriggers.IsOpponentAction(signal) ? f.Team.otherTeam : f.Team))) continue;
            string recipientId = d.Target == CustomRewardEffects.Team ? team : d.Effect == CustomRewardEffects.StatChange && d.Target == CustomRewardEffects.Opponent ? CustomRewardProgress.ScopedRecipient(Player(other), Team(other)) : CustomRewardProgress.ScopedRecipient(actor, team);
            if (d.IsRelic && teamOnce != null && !teamOnce.Add(d.Id + team)) continue;
            string counterActor = d.IsRelic && teamOnce != null ? team : CustomRewardProgress.ScopedRecipient(actor, team);
            string dispatchKey = d.Id + "|" + counterActor;
            if (!dispatching.Add(dispatchKey)) continue;
            try {
            if (progress.Signal(d, signal, counterActor, d.IsRelic ? team : CustomRewardProgress.ScopedRecipient(actor, team), recipientId, Time.time, () => ApplyEffect(d, f, other), () => UnityEngine.Random.Range(0, 100) < d.Chance))
            {
                foreach (var recipient in forwards.Values.Where(v => v != null && CustomRewardProgress.MatchesRecipient(recipientId, Player(v), Team(v)))) recipient.stats?.UpdateCachedStats();
                Notify(d, f, other);
                Plugin.Log.LogInfo("[CustomRewards] " + d.Name + " activated for " + f._skaterData.FullName);
            }
            } finally { dispatching.Remove(dispatchKey); }
        }
    }
    internal static void SignalAll(string signal, string onlyTeam = null) {
        var once = new HashSet<string>();
        foreach (var f in actors.Values.ToArray()) if (f != null && (onlyTeam == null || f.Team?._teamData?.id == onlyTeam)) Signal(signal, f, once);
    }
    internal static void SignalOpponent(string signal, Skater eventPlayer)
    {
        if (eventPlayer?.Team?.otherTeam?._teamData == null) return;
        var ownerTeam = eventPlayer.Team.otherTeam._teamData.id;
        var once = new HashSet<string>();
        foreach (var owner in actors.Values.ToArray()) if (owner?.Team?._teamData?.id == ownerTeam) Signal(signal, owner, once, eventPlayer);
    }
    internal static void Notify(CustomRewardDefinition d, Skater f, Skater other = null)
    {
        var queue = NotificationQueue.Instance; if (queue == null) return;
        // Use the game's existing toast prefab/queue. A separate timing bit means
        // an activation never flushes unrelated pending faceoff or map messages.
        var timing = CustomRewardTriggers.IsCompletion(d.Trigger) ? ENotificationTiming.WorldMap : (ENotificationTiming)(1 << 29);
        string duration = d.Duration == "Seconds" ? d.Seconds + "s" : d.Duration.ToLowerInvariant();
        string who = d.Target == CustomRewardEffects.Opponent && other?._skaterData != null ? other._skaterData.FullName : d.Target == "Whole team" ? "Team" : d.Target == CustomRewardEffects.Nearby ? "Nearby opponents" : d.Target == CustomRewardEffects.Opponent ? "Event target" : f._skaterData.FullName;
        var asset = d.IsRelic ? (ScriptableObject)FindRelic(d.Id, false) : FindTalent(d.Id, false);
        queue.Queue(new NotificationData {
            Type = ENotificationType.Generic, Timing = timing, Title = d.Name,
            TitleIcon = d.IsRelic ? FindRelic(d.Id, false)?.sprite : FindTalent(d.Id, false)?.icon,
            Description = who + ": " + CustomRewardEffects.Summary(d) + (d.Effect == CustomRewardEffects.StatChange ? " · " + duration : ""),
            SourceTeam = f.Team._teamData, SourceSkater = f._skaterData,
            IsTeamHome = f.Team == GameManager.HomeTeam, PowerUpOrRelic = asset
        });
        if (timing != ENotificationTiming.WorldMap) queue.DisplayNotificationsNow(timing);
    }
    internal static void Safe(Action action) { try { action(); } catch (Exception ex) { Plugin.Log.LogError("[CustomRewards] Event: " + ex); } }
    private static bool ApplyEffect(CustomRewardDefinition d, Skater actor, Skater other)
    {
        var actionSource = CustomRewardTriggers.IsOpponentAction(d.Trigger) ? other : actor;
        if (d.Effect == CustomRewardEffects.SonicKnockouts) return CustomRewardNativeEffects.Sonic(d, actor);
        if (d.Effect == CustomRewardEffects.DeflectLob) return CustomRewardNativeEffects.DeflectLob(actor);
        if (d.Effect == CustomRewardEffects.BoardBounce) return CustomRewardContactEffects.Bounce(d, actor);
        if (CustomRewardEffects.IsUpgrade(d.Effect)) return CustomRewardGrants.Upgrade(d, actor);
        if (d.Effect == CustomRewardEffects.PassUninterceptable) {
            if (currentPass?.passingSkater != actionSource) return false;
            bool changed = currentPass.passType != PassType.Uninterceptable;
            currentPass.passType = PassType.Uninterceptable; return changed;
        }
        if (d.Effect == CustomRewardEffects.GoalieHeadshot) {
            var shot = CustomRewardPuckContact.Shot ?? GameManager.CurrentShotInfo;
            if (shot?.shootingSkater == null || shot.shootingSkater.Team != actor.Team?.otherTeam || shot.deflectedBy == actor
                || Puck.Instance == null || Puck.Instance.IsPossessed() || Puck.Rigidbody == null) return false;
            float speed = Puck.Rigidbody.linearVelocity.magnitude * d.Amount / 100f;
            if (speed <= 0) return false;
            shot.deflectedBy = actor;
            Puck.Instance.StartCoroutine(Puck.Instance.HeadShot(shot.shootingSkater, speed, false)); return true;
        }
        if (CustomRewardEffects.IsGoalieCopy(d.Effect)) return CustomRewardGoalieCopy.Apply(d, actor);
        if (d.Effect == CustomRewardEffects.AttractPuck) return CustomRewardAttraction.Apply(d, actor);
        if (d.Effect == CustomRewardEffects.ClockTime) {
            var clock = GameManager.Instance?.timeRemaining;
            if (clock == null || clock.GetRemainingTime() <= 0) return false;
            clock.AddTime(d.Amount); return true;
        }
        if (CustomRewardEffects.IsShockwave(d.Effect)) return CustomRewardNativeEffects.Shockwave(d, actionSource);
        if (CustomRewardEffects.IsShotFlight(d.Effect)) return CustomRewardNativeEffects.Flight(d, actionSource);
        if (CustomRewardEffects.IsPuck(d.Effect)) {
            if (Puck.Instance == null || Puck.Rigidbody == null || Puck.Instance.IsPossessed()) return false;
            var velocity = Puck.Rigidbody.linearVelocity;
            if (d.Effect == CustomRewardEffects.PuckSpeed) velocity *= d.Amount / 100f;
            else velocity.y = d.Effect == CustomRewardEffects.FlatPuck ? 0 : d.Amount;
            Puck.Rigidbody.linearVelocity = velocity; return true;
        }
        var recipients = Recipients(d, actor, other);
        bool applied = false;
        foreach (var s in recipients.ToArray()) {
            if (s == null || !s.gameObject.activeInHierarchy) continue;
            var forward = s.TryCast<Forward>();
            switch (d.Effect) {
                case CustomRewardEffects.RandomMatchAbility: applied |= CustomRewardAbilityReplacement.Apply(d, actor, forward); break;
                case CustomRewardEffects.GainTalent:
                    applied |= CustomRewardGrants.Talent(forward); break;
                case CustomRewardEffects.Freeze:
                case CustomRewardEffects.Dance:
                    applied |= CustomRewardMovementEffects.Apply(d, actor, forward); break;
                case CustomRewardEffects.ThrowStick:
                    if (forward != null && forward.HasStick && forward.IsVulnerable()) { var facing = forward.GetVectorSkaterDirection(); forward.ThrowStick(new Vector2(facing.x, facing.z)); applied = true; } break;
                case CustomRewardEffects.DropHelmet:
                    if (forward != null && forward.HasHelmet()) { forward.DebugDropHelmet(); applied |= !forward.HasHelmet(); } break;
                case CustomRewardEffects.InjureArm: applied |= CustomRewardInjuryRules.Arm(forward); break;
                case CustomRewardEffects.Injure:
                    if (GameManager.GameType == ScriptableStates.GameType.Rogue && forward != null && CampaignUtil.Instance?._injurySystem is { } injuries) {
                        int before = injuries.GetInjuryCount(forward._forwardData.id);
                        injuries.AddRandomInjury(forward._forwardData, true, forward.Team._teamData, actor.Team._teamData);
                        applied |= injuries.GetInjuryCount(forward._forwardData.id) > before;
                    } break;
                case CustomRewardEffects.Heal:
                    if (GameManager.GameType == ScriptableStates.GameType.Rogue && forward != null && forward.HasAnyInjury() && CampaignUtil.Instance?._injurySystem is { } healing) { healing.HealAllInjuries(forward._forwardData); applied = true; } break;
                case CustomRewardEffects.ShotRecoil:
                case CustomRewardEffects.Boost:
                    applied |= CustomRewardNativeEffects.Boost(d, d.Effect == CustomRewardEffects.ShotRecoil ? actionSource : actor, forward); break;
                case CustomRewardEffects.EggExplosion:
                    if (forward != null) { forward.Explode(d.Radius, false, ExplosionType.Eggsplosion, false, true, Vector3.zero); applied = true; } break;
                case CustomRewardEffects.Burst:
                    if (forward != null) { forward.Explode(d.Radius, true, ExplosionType.Chexplosion, false, false, Vector3.zero); applied = true; } break;
                case CustomRewardEffects.Enrage:
                    if (s.statusEffectsManager != null) { s.statusEffectsManager.SetEnraged(Math.Max(d.Seconds, s.statusEffectsManager.GetEnragedTimeRemaining())); applied = true; } break;
                case CustomRewardEffects.Frog:
                    if (forward != null && s.statusEffectsManager != null) { s.statusEffectsManager.SetFrogged(Math.Max(d.Seconds, s.statusEffectsManager.GetFroggedTimeRemaining())); applied = true; } break;
                case CustomRewardEffects.Knockout:
                    if (forward == null) break;
                    Vector3 direction = s.transform.position - actor.transform.position; direction.y = 0;
                    // Native vulnerability/immunity and injury handling remain in Knockout.
                    applied |= forward.Knockout(direction.normalized * 8, actor, KnockoutType.Explosion, 4, d.Seconds, false, false, false, false, false); break;
                case CustomRewardEffects.BreakStick:
                    if (forward?.HasStick == true) { forward.LoseStick(); applied = true; } break;
                case CustomRewardEffects.RestoreStick:
                    if (forward != null && !forward.HasStick && !forward.IsStickForbidden) { forward.GainStick(); applied = true; } break;
                default:
                    var tag = d.Effect == CustomRewardEffects.KnockoutImmunity ? StatusTag.ImmuneToKnockout
                        : d.Effect == CustomRewardEffects.PokecheckImmunity ? StatusTag.ImmuneToPokecheck
                        : d.Effect == CustomRewardEffects.StaggerImmunity ? StatusTag.ImmuneToStagger : StatusTag.None;
                    if (tag == StatusTag.None || s.statusEffectsManager == null) break;
                    string key = d.Id + "|" + actor.Pointer + "|" + s.Pointer;
                    if (!tags.TryGetValue(key, out var active)) {
                        active = new TimedTag { Skater = s, Tag = tag, Reward = d.Id, Owner = d.IsRelic ? Team(actor) : CustomRewardProgress.ScopedRecipient(Player(actor), Team(actor)) }; tags.Add(key, active);
                        s.statusEffectsManager.AddTag(tag);
                    }
                    applied |= active.Until < Time.time + d.Seconds; active.Until = Time.time + d.Seconds; break;
            }
        }
        return applied;
    }
    private static Skater[] Recipients(CustomRewardDefinition d, Skater actor, Skater other)
    {
        if (d.Target == CustomRewardEffects.Actor) return new[] { actor };
        if (d.Target == CustomRewardEffects.Opponent) return new[] { other };
        if (d.Target == CustomRewardEffects.OpponentCarrier) return Puck.Instance?.skaterPossession is { } holder && holder.Team == actor.Team.otherTeam && holder.TryCast<Forward>() != null ? new[] { holder } : Array.Empty<Skater>();
        bool allies = d.Target == CustomRewardEffects.Team || d.Target == CustomRewardEffects.RandomAllies;
        var pool = actors.Values.Where(s => s != null && s.gameObject.activeInHierarchy && s.Team == (allies ? actor.Team : actor.Team.otherTeam)
            && (s.TryCast<Forward>() is { } f ? !f.IsBenched() : allies && d.Target == CustomRewardEffects.Team && d.Effect == CustomRewardEffects.Enrage)
            && (d.Target != CustomRewardEffects.Nearby || Vector3.Distance(s.transform.position, actor.transform.position) <= d.Radius)).ToArray();
        if (d.Target == CustomRewardEffects.ClosestOpponent) return pool.OrderBy(s => (s.transform.position - actor.transform.position).sqrMagnitude).Take(1).ToArray();
        if (!CustomRewardEffects.IsRandomTarget(d.Target)) return pool;
        // Sampling without replacement: one activation cannot select the same
        // skater twice. Fewer available skaters simply means fewer recipients.
        for (int i = 0; i < Math.Min(d.TargetCount, pool.Length); i++) {
            int pick = UnityEngine.Random.Range(i, pool.Length);
            (pool[i], pool[pick]) = (pool[pick], pool[i]);
        }
        return pool.Take(d.TargetCount).ToArray();
    }
    internal static void Tick()
    {
        CustomRewardGoalConfirmation.Tick();
        CustomRewardNativeEffects.Tick();
        CustomRewardSlapshotSlowmo.Tick();
        CustomRewardBallChains.Tick();
        if (!inMatch || completed || Time.time < nextRefresh) return;
        nextRefresh = Time.time + .1f;
        CustomRewardAbilityReplacement.Refresh();
        CustomRewardSoccerWall.Refresh();
        CustomRewardReferees.Refresh();
        CustomRewardManualDenial.Refresh();
        CustomRewardLoadedPucks.Refresh(forwards.Values);
        CustomRewardBorrowing.Refresh(forwards.Values);
        foreach (var pair in tags.ToArray()) if (pair.Value.Until <= Time.time || pair.Value.Skater == null || !StillOwned(pair.Value.Reward, pair.Value.Owner)) {
            if (pair.Value.Skater != null) pair.Value.Skater.statusEffectsManager?.RemoveTag(pair.Value.Tag);
            tags.Remove(pair.Key);
        }
        foreach (var f in forwards.Values) if (f != null) { f.stats?.UpdateCachedStats(); CustomRewardLiveRules.Refresh(f); CustomRewardCultists.Refresh(f); CustomRewardEquipmentEffects.Refresh(f); CustomRewardFartTimers.Refresh(f); CustomRewardBallChains.Refresh(f); }
    }
    internal static void ShotResolved(ShootingArg shot)
    {
        if (shot == null) return;
        Safe(() => {
            CustomRewardNativeEffects.BeginShot(shot);
            Signal("Shot", shot.shootingSkater);
            SignalOpponent("Opponent shot", shot.shootingSkater);
            if (shot.shotType == ShotType.Slapshot) SignalOpponent("Opponent slapshot", shot.shootingSkater);
            if (shot.isCritical) Signal("Critical shot", shot.shootingSkater);
            else Signal("Non-critical shot", shot.shootingSkater);
            if (shot.shotType == ShotType.Bat) Signal("Bat", shot.shootingSkater);
            if (shot.shotType == ShotType.Slapshot) Signal("Slapshot", shot.shootingSkater);
            if (shot.shotType == ShotType.Wristshot) Signal("Wrist shot", shot.shootingSkater);
            if (shot.shotType == ShotType.OneTimer) Signal("One-timer", shot.shootingSkater);
        });
    }
    internal static void PassResolved(PassingArg pass) => Safe(() => {
        CustomRewardNativeEffects.ClearFlight(); currentPass = pass;
        try { Signal("Pass sent", pass?.passingSkater, other: pass?.receivingSkater); SignalOpponent("Opponent pass", pass?.passingSkater); } finally { currentPass = null; }
    });
    internal static void ReboundResolved(NativeGoalie goalie, bool glove, bool blocker)
        => Safe(() => { if (blocker) Signal("Goalie blocker rebound", goalie); else if (glove) Signal("Goalie glove rebound", goalie); });
    internal static void ShotReboundResolved(NativeGoalie goalie, ShootingArg shot) => Safe(() => {
        if (goalie == null || shot?.shootingSkater == null || shot.shootingSkater.Team != goalie.Team?.otherTeam
            || Puck.Instance == null || Puck.Instance.IsPossessed() || Puck.Rigidbody == null || Puck.Rigidbody.linearVelocity.sqrMagnitude <= .01f) return;
        Signal("Shot rebounds off goalie", shot.shootingSkater);
    });
    internal static void GoalResolved(GoalScoredArg goal, int previousGoals, int assists)
    {
        bool defenderScored = goal?.scorer?.TryCast<Forward>()?.skaterType is SkaterType.LeftDef or SkaterType.RightDef;
        CustomRewardGoalConfirmation.Defer(() => Safe(() => {
        if (goal?.goalType != NHL94.GoalType.GOOD || goal.scorer == null) return;
        Signal("Goal", goal.scorer);
        if (goal.isCritical) Signal("Critical goal", goal.scorer);
        if (goal.scorerWasEnraged) Signal("Enraged goal", goal.scorer);
        if (assists == 0) Signal("Unassisted goal", goal.scorer);
        if (assists >= 2) Signal("Two-assist goal", goal.scorer);
        if (defenderScored) Signal("Defender goal", goal.scorer);
        if (goal.isGameEndingGoal) Signal("Game-winning goal", goal.scorer);
        // Native bonus-goal rewards may increase the score by more than one.
        // Capture the earlier count instead of assuming the new count is one.
        if (previousGoals == 0 && goal.scorer.gameStats.goals > 0) Signal("First personal goal", goal.scorer);
        SignalOpponent("Opponent goal", goal.scorer);
        Signal("Goal conceded", goal.scorer.Team.otherTeam?.goaltender);
        }));
    }
    internal static void BindEvents()
    {
        if (eventsBound || Definitions.Count == 0) return; eventsBound = true;
        void Listen<T>(BishopGames.SingleEvent<T> e, Action<T> action) {
            var callback = (Il2CppSystem.Action<T>)(Action<T>)(arg => Safe(() => action(arg)));
            delegates.Add(callback); SingleEventExtensions.AddListener(e, callback); unsubscribe.Add(() => SingleEventExtensions.RemoveListener(e, callback));
        }
        void ListenSimple(BishopGames.SingleEvent e, Action action) {
            var callback = (Il2CppSystem.Action)(Action)(() => Safe(action)); delegates.Add(callback); SingleEventExtensions.AddListener(e, callback); unsubscribe.Add(() => SingleEventExtensions.RemoveListener(e, callback));
        }
        void ListenTwo<A, B>(BishopGames.SingleEvent<A, B> e, Action<A, B> action) {
            var callback = (Il2CppSystem.Action<A, B>)(Action<A, B>)((a, b) => Safe(() => action(a, b)));
            delegates.Add(callback); SingleEventExtensions.AddListener(e, callback); unsubscribe.Add(() => SingleEventExtensions.RemoveListener(e, callback));
        }
        void ListenThree<A, B, C>(BishopGames.SingleEvent<A, B, C> e, Action<A, B, C> action) {
            var callback = (Il2CppSystem.Action<A, B, C>)(Action<A, B, C>)((a, b, c) => Safe(() => action(a, b, c)));
            delegates.Add(callback); SingleEventExtensions.AddListener(e, callback); unsubscribe.Add(() => SingleEventExtensions.RemoveListener(e, callback));
        }
        // Goal signals run after GameStats.OnGoalScored, independent of event
        // subscriber order. Hat tricks and assists retain their native events.
        ListenSimple(GameplayEvents.EndOfGame, CustomRewardAbilityReplacement.Clear);
        Listen(GameplayEvents.HatTrickGoal, (HatTrickArg a) => CustomRewardGoalConfirmation.Defer(() => Signal("Hat trick", a.skater)));
        var saveCallback = (Il2CppSystem.Action<NativeGoalie, float, bool>)(Action<NativeGoalie, float, bool>)((goalie, quality, shouldFail) => Safe(() => {
            if (!shouldFail && goalie?.Team?.otherTeam?._teamData != null) SignalAll("Opposing goalie save", goalie.Team.otherTeam._teamData.id);
        }));
        delegates.Add(saveCallback); SingleEventExtensions.AddListener(GameplayEvents.GoalieSaveAudio, saveCallback);
        unsubscribe.Add(() => SingleEventExtensions.RemoveListener(GameplayEvents.GoalieSaveAudio, saveCallback));
        Listen(GameplayEvents.AssistGained, (Skater s) => {
            var scorer = CustomRewardGoalConfirmation.CurrentScorer;
            CustomRewardGoalConfirmation.Defer(() => Signal(s?.TryCast<NativeGoalie>() != null ? "Goalie assist" : "Assist", s, other: scorer));
        });
        // GameStats.OnHit supplies Check/Checked through CustomRewardCheckEvent.
        // Il2CppInterop cannot convert Action<SkaterCheckEventArgs>: this native
        // value type contains object references. Subscribing here would abort
        // registration of every following gameplay event.
        Listen(GameplayEvents.BlockedShot, (Forward f) => Signal("Blocked shot", f));
        Listen(GameplayEvents.Knockout, (KnockoutArgs a) => {
            if (a.knockedBy?.Team != null && a.playerKnocked?.Team != null && a.knockedBy.Team != a.playerKnocked.Team) teamsWithOpposingKnockouts.Add(a.knockedBy.Team.Pointer);
            if (a.knockedBy?.TryCast<Forward>() is { } checker && checker.ArePowerUpsEnabled()
                && checker.CurrentMovementState?.TryCast<Movements.MovementStates.BodycheckState>() is { } checking
                && CustomRewardCampaignRules.Owned(checker.Team?._teamData, "Bodycheck movement").Any(d => d.Effect == CustomRewardEffects.FasterChecks)) checking.OnKnockout(a);
            var referees = GameManager.Instance?.referees;
            if (a.playerKnocked != null && referees != null && referees.HasMatch(r => r?._forward?.Pointer == a.playerKnocked.Pointer)) SignalAll("Referee knocked out");
            if (a.playerKnocked?.skaterType == SkaterType.Referee && a.knockedBy?.Team?._teamData != null) SignalAll("Team knocks out referee", a.knockedBy.Team._teamData.id);
            if (a.knockedBy?.Team != a.playerKnocked?.Team) Signal("Knockout", a.knockedBy, other: a.playerKnocked);
            else if (a.knockedBy != null && a.playerKnocked != null && a.knockedBy.Pointer != a.playerKnocked.Pointer) Signal("Teammate knockout", a.knockedBy, other: a.playerKnocked);
            Signal("Knocked out", a.playerKnocked, other: a.knockedBy);
            SignalOpponent("Opponent knocked out", a.playerKnocked);
        });
        Listen(GameplayEvents.PassReceivedEvent, (Forward f) => Signal("Pass received", f, other: GameManager.CurrentPassInfo?.passingSkater));
        Listen(GameplayEvents.ForwardPickupPuck, (Forward f) => Signal("Puck pickup", f));
        ListenTwo(GameplayEvents.Interception, (Skater s, PassingArg p) => { Signal("Interception", s, other: p?.passingSkater); Signal("Pass intercepted", p?.passingSkater, other: s); });
        // Shot/pass hooks run after Puck has assigned the launch velocity. A
        // listener on ShotEvent could run before that assignment and be erased.
        Listen(GameplayEvents.ShotOnNetRegisteredEvent, (ShootingArg a) => {
            Signal("Shot on net", a?.shootingSkater);
            var shootingTeam = a?.shootingSkater?.Team;
            if (shootingTeam == null) return;
            Signal("Goalie faces shot on net", shootingTeam.otherTeam?.goaltender);
            Signal("Goalie observes shot on either net", shootingTeam.goaltender);
            Signal("Goalie observes shot on either net", shootingTeam.otherTeam?.goaltender);
        });
        Listen(GameplayEvents.OnChargingSlapshotStart, (Forward f) => Signal("Slapshot windup", f));
        Listen(GameplayEvents.PuckOnPost, (PuckOnPostEventArgs a) => Signal("Post hit", a?.shooter));
        Listen(GameplayEvents.RetrieveLoosePuck, (Forward f) => Signal("Loose puck pickup", f));
        Listen(GameplayEvents.SkaterLosePuckPossession, (Skater f) => Signal("Puck lost", f));
        Listen(GameplayEvents.FaceoffLaunch, (FaceoffArgs a) => Signal("Faceoff won", a?.winner));
        Listen(GameplayEvents.StickBreaking, (Forward f) => { if (f?._forwardData != null) brokenSticks.Add(f._forwardData.id); Signal("Stick broken", f); });
        Listen(GameplayEvents.AbilityUsed, (Skater s) => { Signal("Ability used", s); SignalOpponent("Opponent ability used", s); });
        Listen(GameplayEvents.EnragedTriggered, (Skater s) => Signal("Enraged", s));
        Listen(GameplayEvents.ForwardReceivedInjury, (ReceivedInjuryEventArgs a) => { if (a != null) foreach (var f in forwards.Values.Where(f => f != null && f._forwardData.id == a.forwardId).ToArray()) Signal("Injury received", f); });
        Listen(GameplayEvents.ForwardCausedInjury, (CausedInjuryEventArgs a) => { if (a?.injuredBy?.Team != a?.injured?.Team) Signal("Injury caused", a?.injuredBy, other: a?.injured); });
        ListenTwo(GameplayEvents.Deflect, (Forward f, bool _) => Signal("Deflection", f));
        ListenThree(GameplayEvents.Pokecheck, (Forward source, Skater victim, bool _) => { if (victim != null) { Signal("Pokecheck", source, other: victim); Signal("Pokechecked", victim, other: source); } });
        ListenTwo(GameplayEvents.ForwardPassedBlueLine, (Forward f, bool _) => { Signal("Blue line crossed", f); SignalOpponent("Opponent crosses blue line", f); });
        Listen(GameplayEvents.ForwardPassedRedLine, (Forward f) => Signal("Red line crossed", f));
        ListenTwo(RogueEvents.Fart, (FartEventArgs a, Il2CppSystem.Type _) => Signal("Fart", a?.skaterUsingAbility));
        var goalieShot = (Il2CppSystem.Action<Vector3, NativeGoalie, float, ShootingArg>)(Action<Vector3, NativeGoalie, float, ShootingArg>)((_, g, _, shot) => Safe(() => {
            if (CustomRewardPuckContact.InCollision) { CustomRewardPuckContact.Shot = shot; CustomRewardPuckContact.ShotGoalie = g; }
            Signal("Goalie receives shot", g, other: shot?.shootingSkater);
        }));
        delegates.Add(goalieShot); SingleEventExtensions.AddListener(GameplayEvents.PuckHitGoaltender, goalieShot); unsubscribe.Add(() => SingleEventExtensions.RemoveListener(GameplayEvents.PuckHitGoaltender, goalieShot));
        Listen(GameplayEvents.GoaliePickupPuck, (NativeGoalie g) => Signal("Goalie picks up puck", g));
        Listen(GameplayEvents.GoalieLoosePuckPokecheck, (NativeGoalie g) => Signal("Goalie pokecheck", g));
        Listen(GameplayEvents.DesperateSave, (NativeGoalie g) => Signal("Goalie desperate save", g));
        Listen(GameplayEvents.BreakawayStart, (Skater s) => {
            Signal("Breakaway started", s);
            if (s?.Team?.otherTeam?._teamData is { } opposingTeam) SignalAll("Opponent breakaway", opposingTeam.id);
        });
        Listen(GameplayEvents.BattingPuck, (Forward f) => Signal("Bat puck", f));
        Listen(GameplayEvents.DefensiveDeflect, (Forward f) => Signal("Defensive deflection", f));
        ListenTwo(GameplayEvents.HelmetKnockedOff, (Forward f, Forward _) => Signal("Helmet lost", f));
        ListenTwo(GameplayEvents.SkaterHitBoard, (Forward f, Vector3 normal) => CustomRewardContactEffects.Contact(f, normal, () => Signal("Board collision", f)));
        ListenTwo(GameplayEvents.VerticalKnockoutLanding, (Forward f, float _) => Signal("Knockout landing", f));
        ListenSimple(GameplayEvents.AfterGamePresentation, () => { BindGoalie(GameManager.HomeTeam?.goaltender); BindGoalie(GameManager.VisitorTeam?.goaltender); CustomRewardTemperature.Initialize(GameManager.HomeTeam); CustomRewardTemperature.Initialize(GameManager.VisitorTeam); SignalAll("Match started"); SignalAll("Period started"); });
        ListenSimple(GameplayEvents.PeriodPresentation, () => SignalAll("Period started"));
        ListenSimple(GameplayEvents.Last30SecondsOfPeriod, () => SignalAll("Last 30 seconds of period"));
        Listen(RogueEvents.WinByMercy, (bool _) => mercyWon = true);
        ListenSimple(GameplayEvents.Last45SecondsOfPeriod, () => SignalAll("Last 45 seconds of period"));
        Listen(GameplayEvents.EndOfPeriod, (EndOfPeriodArg _) => { progress.EndPeriod(); nextRefresh = 0; Tick(); });
        // Defer listener removal until the next match/title; do not mutate the
        // native event list while it is dispatching OnGameExit.
        ListenSimple(GameplayEvents.OnGameExit, () => { if (!completed) progress.Load(matchStartProgress); inMatch = false; });
    }
    internal static void CompleteMatch(State.EndOfGame.EndOfGameContext context)
    {
        if (!inMatch || completed) return;
        if (context == null || context.HasConcededOrQuitMatch) return;
        EnsureRun();
        var team = CampaignUtil.CampaignState?.runData?.teamData;
        if (team == null) return;
        // Zoomer/HitStreak snapshot native final statistics, not their provisional
        // event counters. Capture owners before completion rewards can add items.
        foreach (var actor in actors.Values.ToArray().Where(s => s != null && s.Team?._teamData?.id == team.id && s.TryCast<Forward>() != null))
            foreach (var rule in Definitions.Values.Where(d => !d.NeedsRedesign && CustomRewardDefinition.IsPreviousMatchBasis(d.AmountBasis)
                && Has(actor._skaterData, d.Id) && actor._skaterData.powerups.HasMatch(t => t != null && t.id == d.Id && !t.PayoffSuppressed)).ToArray())
                progress.SetPreviousMatchCount(Player(actor), rule.Id, Math.Clamp(rule.AmountBasis == "If benched for the entire previous match" ? (actor.TryCast<Forward>().HasPlayedThisMatch() ? 0 : 1) : rule.AmountBasis == "Per goal in the previous match" ? actor.gameStats.goals : actor.gameStats.knockoutsPerformed, 0, 999999));
        SignalAll("Match completed", team.id);
        if (context.IsMatchWinner) { SignalAll("Match won", team.id); if (mercyWon) SignalAll("Match won by mercy", team.id); }
        foreach (var actor in actors.Values.ToArray().Where(s => s != null && s.Team?._teamData?.id == team.id)) {
            if (actor.TryCast<NativeGoalie>() != null && actor.Team.otherTeam.Score == 0) Signal("Shutout", actor);
            if (actor.TryCast<Forward>() != null && actor.gameStats.goals == 0) Signal("Scoreless match", actor);
            if (actor.TryCast<Forward>() != null && actor.gameStats.goals == 0 && actor.gameStats.assists > 0) Signal("Scoreless match with an assist", actor);
            if (actor.TryCast<Forward>() != null && brokenSticks.Contains(actor._skaterData.id)) Signal("Match with a broken stick", actor);
        }
        completed = true;
        Save();
    }
    internal static void TrainingChosen(TeamData team)
    {
        if (team == null || CampaignUtil.CampaignState?.runData?.teamData?.id != team.id) return;
        EnsureAssets(); EnsureRun(); if (run.Length == 0) return;
        ObserveOwnership(team);
        string owner = "team:" + team.id;
        foreach (var rule in Definitions.Values.Where(d => !d.NeedsRedesign && d.IsRelic && d.Trigger == "Training chosen" && Has(team, d.Id)).ToArray()) {
            if (!progress.Signal(rule, rule.Trigger, owner, owner, owner, Time.time, roll: () => UnityEngine.Random.Range(0, 100) < rule.Chance)) continue;
            NotificationQueue.Instance?.Queue(new NotificationData {
                Type = ENotificationType.Generic, Timing = ENotificationTiming.WorldMap,
                Title = rule.Name, TitleIcon = FindRelic(rule.Id, false)?.sprite,
                Description = "Team: " + CustomRewardEffects.Summary(rule) + " for the rest of the run",
                SourceTeam = team, IsTeamHome = true, PowerUpOrRelic = FindRelic(rule.Id, false)
            });
        }
        Save();
    }
    internal static string Display(string id, string field)
    {
        if (!Definitions.TryGetValue(id ?? "", out var d)) return null;
        if (field.IndexOf("Name", StringComparison.OrdinalIgnoreCase) >= 0) return d.Name;
        if (field.IndexOf("Upgraded", StringComparison.OrdinalIgnoreCase) >= 0 || field.IndexOf("StatBonus", StringComparison.OrdinalIgnoreCase) >= 0) return "";
        return d.FullDescription;
    }
    internal static IEnumerable<(string Stat, int Amount, bool Conditional)> Preview(string id, SkaterData skater, TeamData team)
    {
        if (!Definitions.TryGetValue(id ?? "", out var d)) yield break;
        if (d.NeedsRedesign || d.Effect != CustomRewardEffects.StatChange) yield break;
        // Reward previews describe this owner's team. An opponent-only bonus
        // must not be rendered as a change to the selecting player's ratings.
        if (d.Target == CustomRewardEffects.OpposingTeam || d.Target == CustomRewardEffects.OtherTeammates) yield break;
        EnsureRun();
        if (CustomRewardTriggers.IsCondition(d.Trigger)) yield return (d.Stat, d.Amount, d.Trigger != "While owned" || d.AmountBasis != "Fixed amount");
        else {
            string actor = "player:" + skater?.id, recipientTeam = "team:" + team?.id;
            string owner = d.IsRelic ? recipientTeam : CustomRewardProgress.ScopedRecipient(actor, recipientTeam);
            int amount = progress.Effects.Where(e => e.Reward == id && (e.Owner == owner || !d.IsRelic && e.Owner == actor) && CustomRewardProgress.MatchesRecipient(e.Recipient, actor, recipientTeam)
                && (e.Duration == "Rest of run" || (inMatch && !completed && (e.Duration != "Seconds" || e.Expires > Time.time))))
                .Sum(e => e.Amount * e.Stacks);
            if (amount != 0) yield return (d.Stat, amount, false);
            else yield return (d.Stat, d.Amount, true); // Native UI shows an unactivated rule as conditional.
        }
    }
}

[HarmonyPatch(typeof(State.CampaignState), "LoadRunData")]
internal static class CustomRewardsBeforeLoad { [HarmonyPrefix, HarmonyPriority(Priority.First)] private static void Prefix() => CustomRewardRuntime.EnsureAssets(); }
[HarmonyPatch(typeof(GameStats), "OnGoalScored")]
internal static class CustomRewardGoalScored
{
    [HarmonyPrefix] private static void Prefix(GoalScoredArg __0, out int __state) {
        CustomRewardGoalConfirmation.Begin(__0); __state = __0?.scorer?.gameStats?.goals ?? -1;
    }
    [HarmonyPostfix] private static void Postfix(GameStats __instance, GoalScoredArg __0, int __state) {
        CustomRewardRuntime.Safe(() => CustomRewardLoadedPucks.Scored(__0));
        CustomRewardRuntime.GoalResolved(__0, __state, __instance.lastAssists?.Count ?? 0); CustomRewardGoalConfirmation.Processed(__0);
    }
}
[HarmonyPatch(typeof(Tape2Tape.Hockey.UI.TitleScreen), "RefreshCampaignData")]
internal static class CustomRewardsAtTitle { [HarmonyPrefix] private static void Prefix() { CustomRewardRuntime.EndMatch(); CustomRewardRuntime.EnsureAssets(); } }
[HarmonyPatch(typeof(State.CampaignState), "SaveRunData")]
internal static class CustomRewardsSave { [HarmonyPostfix] private static void Postfix() => CustomRewardRuntime.Save(); }
[HarmonyPatch(typeof(GameManager), "Start")]
internal static class CustomRewardsState { [HarmonyPostfix] private static void Postfix() => CustomRewardRuntime.MatchStarted(); }
[HarmonyPatch(typeof(Forward), "OnGameInitializationDone")]
internal static class CustomRewardsBind { [HarmonyPostfix] private static void Postfix(Forward __instance) => CustomRewardRuntime.Bind(__instance); }
[HarmonyPatch(typeof(Skater), "Update")]
internal static class CustomRewardsRefresh { [HarmonyPostfix] private static void Postfix() => CustomRewardRuntime.Safe(CustomRewardRuntime.Tick); }
[HarmonyPatch(typeof(Puck), "OnShot")]
internal static class CustomRewardShot { [HarmonyPostfix] private static void Postfix(ShootingArg __0) => CustomRewardRuntime.ShotResolved(__0); }
[HarmonyPatch(typeof(Puck), "OnPass")]
internal static class CustomRewardPass { [HarmonyPostfix] private static void Postfix(PassingArg __0) => CustomRewardRuntime.PassResolved(__0); }
[HarmonyPatch(typeof(Puck), "OnCollisionEnter")]
internal static class CustomRewardPuckContact
{
    [ThreadStatic] internal static NativeGoalie Goalie;
    [ThreadStatic] internal static NativeGoalie ShotGoalie;
    [ThreadStatic] internal static ShootingArg Shot;
    [ThreadStatic] internal static bool Glove, Blocker, InCollision;
    [HarmonyPrefix] private static void Prefix() { Goalie = ShotGoalie = null; Shot = null; InCollision = true; CustomRewardRuntime.Safe(CustomRewardNativeEffects.Contact); }
    [HarmonyPostfix] private static void Postfix() {
        var goalie = Goalie; Goalie = null; InCollision = false;
        if (goalie != null) CustomRewardRuntime.ReboundResolved(goalie, Glove, Blocker);
        CustomRewardRuntime.ShotReboundResolved(ShotGoalie, Shot); ShotGoalie = null; Shot = null;
    }
    [HarmonyFinalizer] private static void Finalizer() { Goalie = ShotGoalie = null; Shot = null; InCollision = false; }
}
[HarmonyPatch(typeof(Puck), "TryGetScriptedGoalieRebound")]
internal static class CustomRewardGoalieRebound
{
    [HarmonyPostfix] private static void Postfix(NativeGoalie __1, bool __2, bool __3, bool __result) {
        if (!__result || !CustomRewardPuckContact.InCollision) return;
        CustomRewardPuckContact.Goalie = __1; CustomRewardPuckContact.Glove = __2; CustomRewardPuckContact.Blocker = __3;
    }
}
[HarmonyPatch(typeof(RelicRepository), "GetRelic", new[] { typeof(string), typeof(bool) })]
internal static class CustomRelicLookup
{
    [HarmonyPrefix] private static bool Prefix(string __0, bool __1, ref Relic __result) { var r = CustomRewardRuntime.FindRelic(__0, __1); if (r == null) return true; __result = r; return false; }
}
[HarmonyPatch(typeof(TalentRepository), "GetTalent", new[] { typeof(string), typeof(bool) })]
internal static class CustomTalentLookup
{
    [HarmonyPrefix] private static bool Prefix(string __0, bool __1, ref Talent __result) { var t = CustomRewardRuntime.FindTalent(__0, __1); if (t == null) return true; __result = t; return false; }
}
[HarmonyPatch(typeof(State.EndOfGame.Steps.CampaignUpdateStep), "UpdateCampaignPowerupAndRelicState")]
internal static class CustomRewardCompletion { [HarmonyPostfix] private static void Postfix(State.EndOfGame.EndOfGameContext __0) { CustomRewardGoalConfirmation.Flush(); CustomRewardRuntime.CompleteMatch(__0); } }
[HarmonyPatch(typeof(Skater), "TryAddTalent")]
internal static class CustomTalentAcquisition
{
    [HarmonyPrefix] private static void Prefix(Talent __0, ref bool __1) { if (__0 != null && CustomRewardRuntime.IsCustom(__0.id)) __1 = true; }
}
[HarmonyPatch(typeof(RogueUtils), "GetTalentIndex")]
internal static class CustomTalentIdentity
{
    [HarmonyPrefix] private static bool Prefix(SkaterData __0, Talent __1, bool __2, ref int __result)
    {
        if (__0?.powerups == null || __1 == null || (!CustomRewardRuntime.IsCustom(__1.id) && !__0.powerups.HasMatch(t => t != null && CustomRewardRuntime.IsCustom(t.id)))) return true;
        __result = -1;
        for (int i = 0; i < __0.powerups.Count; i++) {
            var t = __0.powerups[i]; if (t == null) continue;
            if (t.id == __1.id || (__2 && !CustomRewardRuntime.IsCustom(__1.id) && !CustomRewardRuntime.IsCustom(t.id) && t.GetIl2CppType() == __1.GetIl2CppType())) { __result = i; break; }
        }
        return false;
    }
}
[HarmonyPatch(typeof(SkaterData), "HasTalentType")]
internal static class CustomTalentNativeTypeIdentity
{
    [HarmonyPrefix] private static bool Prefix(SkaterData __instance, Il2CppSystem.Type __0, ref bool __result) {
        if (__instance?.powerups == null || !__instance.powerups.HasMatch(t => t != null && CustomRewardRuntime.IsCustom(t.id))) return true;
        // A neutral carrier does not make its native template already owned.
        __result = __0 != null && __instance.powerups.HasMatch(t => t != null && !CustomRewardRuntime.IsCustom(t.id) && t.GetIl2CppType() == __0);
        return false;
    }
}
[HarmonyPatch(typeof(RogueUtils), "HasRelic")]
internal static class CustomRelicIdentity
{
    [HarmonyPrefix] private static bool Prefix(TeamData __0, Relic __1, ref bool __result) {
        if (__0?.relics == null || __1 == null || (!CustomRewardRuntime.IsCustom(__1.id) && !__0.relics.HasMatch(r => r != null && CustomRewardRuntime.IsCustom(r.id)))) return true;
        __result = __0.relics.HasMatch(r => r != null && (r.id == __1.id || (!CustomRewardRuntime.IsCustom(__1.id) && !CustomRewardRuntime.IsCustom(r.id) && r.GetIl2CppType() == __1.GetIl2CppType())));
        return false;
    }
}
[HarmonyPatch(typeof(RogueUtils), "HasFullyUpgradedRelic")]
internal static class CustomRelicOwned
{
    [HarmonyPrefix] private static bool Prefix(TeamData __0, Relic __1, ref bool __result) {
        if (__1 == null || !CustomRewardRuntime.IsCustom(__1.id)) return true;
        __result = CustomRewardRuntime.Has(__0, __1.id); return false;
    }
}
[HarmonyPatch(typeof(RogueUtils), "HasFullyUpgradedTalent")]
internal static class CustomTalentOwned
{
    [HarmonyPrefix] private static bool Prefix(SkaterData __0, Talent __1, ref bool __result) {
        if (__1 == null || !CustomRewardRuntime.IsCustom(__1.id)) return true;
        __result = CustomRewardRuntime.Has(__0, __1.id); return false;
    }
}
[HarmonyPatch(typeof(TalentRepository), "TryUpgradeTalent")]
internal static class CustomTalentRandomUpgrade
{
    [HarmonyPrefix] private static bool Prefix(Talent __0, bool __2, ref Talent __result) {
        if (__0 == null || !CustomRewardRuntime.IsCustom(__0.id)) return true;
        __result = __2 ? Object.Instantiate(__0) : __0; return false;
    }
}
[HarmonyPatch(typeof(RelicRepository), "TryUpgradeRelic")]
internal static class CustomRelicRandomUpgrade
{
    [HarmonyPrefix] private static bool Prefix(Relic __0, bool __2, ref Relic __result) {
        if (__0 == null || !CustomRewardRuntime.IsCustom(__0.id)) return true;
        __result = __2 ? Object.Instantiate(__0) : __0; return false;
    }
}
[HarmonyPatch]
internal static class CustomRewardDisplay
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (var type in new[] { typeof(PowerUp), typeof(Relic), typeof(BasicStatsUpgradeRelic), typeof(TemporaryContract), typeof(ShortContract), typeof(Feed), typeof(Tape2Tape.Hockey.ForwardStatBonusOnPickup), typeof(FartingPuck), typeof(Rogue.Powerups.Chaos.MonkeysPaw), typeof(Rogue.Powerups.Chaos.MoldPowerup), typeof(SplitPersonality), typeof(Rogue.CanadaCurses.CanadaCurseAngusZeroOvr), typeof(Rogue.CanadaCurses.CanadaCurseOpponentBonecrusher), typeof(Rogue.CanadaCurses.CanadaCurseOpponentBriefcase), typeof(Rogue.CanadaCurses.CanadaCurseFreezeTwoRelics), typeof(Rogue.CanadaCurses.CanadaCurseFrogTwo), typeof(Rogue.CanadaCurses.CanadaCurseXsTeam), typeof(BlueLineLaunchRelic), typeof(BlueLineKnockoutRelic), typeof(SnoozeAlarm), typeof(ParticipationTrophy), typeof(Rogue.Relics.ChaosRelics.YipsRelic), typeof(NutJob), typeof(EggTimer), typeof(HardBoiledEgg), typeof(Rogue.Relics.VoodooDoll), typeof(FastRebound), typeof(DryNoodle), typeof(Stethoscope), typeof(LoseHelmet), typeof(StatStuffer), typeof(Rogue.Powerups.Chaos.Liquidation), typeof(Rogue.Relics.RelicStealer) })
            foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                // BaseLocalizedUpgraded/StatBonus getters share native addresses
                // with their public counterparts. Cached fields also get managed
                // accessors in interop; those have no native method to patch.
                if (method.ReturnType == typeof(string) && method.GetParameters().Length == 0
                    && method.Name.StartsWith("get_localized", StringComparison.OrdinalIgnoreCase))
                    yield return method;
    }
    [HarmonyPrefix] private static bool Prefix(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase __instance, MethodBase __originalMethod, ref string __result)
    {
        string id = __instance.TryCast<PowerUp>()?.id ?? __instance.TryCast<Relic>()?.id;
        string text = CustomRewardRuntime.Display(id, __originalMethod.Name);
        if (text == null) return true; __result = text; return false;
    }
}
[HarmonyPatch(typeof(TalentRepository), "HasUpgrade")]
internal static class CustomTalentNoUpgrade { [HarmonyPrefix] private static bool Prefix(Talent __0, ref bool __result) { if (__0 == null || !CustomRewardRuntime.IsCustom(__0.id)) return true; __result = false; return false; } }
[HarmonyPatch(typeof(RelicRepository), "HasUpgrade")]
internal static class CustomRelicNoUpgrade { [HarmonyPrefix] private static bool Prefix(Relic __0, ref bool __result) { if (__0 == null || !CustomRewardRuntime.IsCustom(__0.id)) return true; __result = false; return false; } }
[HarmonyPatch(typeof(TalentRepository), "GetUpgradeForTalent")]
internal static class CustomTalentUpgrade { [HarmonyPrefix] private static bool Prefix(Talent __0, ref Talent __result) { if (__0 == null || !CustomRewardRuntime.IsCustom(__0.id)) return true; __result = null; return false; } }
[HarmonyPatch(typeof(RelicRepository), "GetUpgradeForRelic")]
internal static class CustomRelicUpgrade { [HarmonyPrefix] private static bool Prefix(Relic __0, ref Relic __result) { if (__0 == null || !CustomRewardRuntime.IsCustom(__0.id)) return true; __result = null; return false; } }
