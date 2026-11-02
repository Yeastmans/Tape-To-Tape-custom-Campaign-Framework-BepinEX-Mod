using EndlessMode;
using CampaignCore;
using Data;
using State;
using Tape2Tape.Hockey.UI;
using Rogue.NPC.GeneralManagerNPC;
using Pool = Il2CppSystem.Collections.Generic.List<Data.ForwardData>;

int checks = 0;
void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
Check(BaseGamePoolSelectionRuntime.Applies(true, true), "Enabled Base Game profile must apply");
Check(!BaseGamePoolSelectionRuntime.Applies(false, true), "Custom campaigns must remain inert");
Check(!BaseGamePoolSelectionRuntime.Applies(true, false), "Disabled/missing profile must remain inert");
var original = new List<string> { "native" };
var recruitment = original;
var bench = new List<string> { "angus" };
var benchOriginal = bench;
try
{
    using var scope = new BaseGamePoolSelectionRuntime.Scope();
    scope.Replace(() => recruitment, value => recruitment = value, new List<string> { "recruit" });
    scope.Replace(() => bench, value => bench = value, null);
    Check(recruitment[0] == "recruit" && bench == null, "GM must receive recruitment and bypass squad bench source");
    using (var nested = new BaseGamePoolSelectionRuntime.Scope())
    {
        nested.Replace(() => recruitment, value => recruitment = value, new List<string> { "star" });
        Check(recruitment[0] == "star", "Nested superstar source independent of recruits");
    }
    Check(recruitment[0] == "recruit", "Nested source restores outer recruitment");
    throw new InvalidOperationException("native call failed");
}
catch (InvalidOperationException) { }
Check(ReferenceEquals(recruitment, original) && ReferenceEquals(bench, benchOriginal), "Exception restores exact original source references");
using (var noOp = new BaseGamePoolSelectionRuntime.Scope()) { noOp.Dispose(); noOp.Dispose(); }
var filtered = BaseGamePoolSelectionRuntime.Eligible(new[] { "locked", "owned", "allowed", "excluded", "previous" }, p => p == "allowed");
Check(filtered.SequenceEqual(new[] { "allowed" }), "All native eligibility exclusions are honored before sampling");
foreach (int available in new[] { 0, 1, 2, 3, 12 })
foreach (int desired in new[] { 2, 3 })
{
    var candidates = Enumerable.Range(0, available).ToList();
    var result = BaseGamePoolSelectionRuntime.Sample(candidates, desired, maximum => maximum - 1);
    Check(result.Count == Math.Min(available, desired), $"Bounded offer count {available}/{desired}");
    Check(result.Distinct().Count() == result.Count, "No duplicate fallback offers");
    Check(candidates.SequenceEqual(Enumerable.Range(0, available)), "Sampling leaves source candidates unchanged");
    if (available > desired) Check(result.Contains(available - 1), "Candidates beyond native offer count remain selectable");
}
string root = Path.Combine(Path.GetTempPath(), "base-pools-runtime-" + Guid.NewGuid().ToString("N"));
Plugin.ModContentRoot = root;
try
{
    Check(BaseGamePoolsRuntime.Selection() == null, "Missing settings inert");
    var selected = new BaseGamePoolSelection { Enabled = true };
    foreach (string kind in BaseGamePlayerPools.Kinds)
    {
        Directory.CreateDirectory(BaseGamePlayerPools.PoolDirectory(root, kind));
        File.WriteAllText(Path.Combine(BaseGamePlayerPools.PoolDirectory(root, kind), "one.txt"), "Name = " + kind + " One\n");
        selected.Pools[kind] = new[] { "one.txt" };
    }
    BaseGamePlayerPools.Save(root, selected);
    var state = new CampaignState(); Tape2Tape.CampaignUtil.CampaignState = state;
    var stateOriginal = state.freeAgents;
    var squadOriginal = state.Squad.m_CustomSwapPool;
    var menu = new BenchwarmerMenu { m_CampaignState = state };
    state.Squad.m_MaxBenchwarmersOnBench = 0;
    BaseGamePoolsRuntime.BenchPrefix(menu, out var scope);
    Check(scope == null && ReferenceEquals(state.Squad.m_CustomSwapPool, squadOriginal), "No-bench squad remains unchanged");
    state.Squad.m_MaxBenchwarmersOnBench = 3;
    BaseGamePoolsRuntime.BenchPrefix(menu, out scope);
    Check(state.Squad.m_CustomSwapPool[0].firstName == "draft_pool One", "Bench receives shared selected pool");
    Check(state.Squad.m_CustomSwapPool[0].skaterCategory == SkaterCategory.Benchwarmer,
        "Added benchwarmer must qualify for native Benchwarmer Boost when its prototype is a free agent");
    Check(UnityEngine.Resources.Players[0].skaterCategory == SkaterCategory.FreeAgents,
        "Classifying an added benchwarmer must not change the shared native prototype");
    Check(ReferenceEquals(state.freeAgents, stateOriginal), "Bench does not replace recruitment source");
    BaseGamePoolsRuntime.Restore(scope, new Exception("native bench failure"));
    Check(ReferenceEquals(state.Squad.m_CustomSwapPool, squadOriginal), "Bench failure restores original pool");
    Check(state.Squad.m_MaxBenchwarmersOnBench == 3, "Bench capacity preserved");
    BaseGamePoolsRuntime.RandomPrefix(state, out scope);
    Check(state.freeAgents[0].skaterCategory == SkaterCategory.FreeAgents,
        "Benchwarmer classification must not leak into the independent free-agent pool");
    string customId = state.freeAgents[0].id;
    Check(customId == BaseGamePlayerPools.CustomPlayerId("free_agents", "one.txt"), "Imported template gets stable ID");
    bool unlocked = false;
    Check(!BaseGamePoolsRuntime.UnlockPrefix(customId, ref unlocked) && unlocked, "Registered authored ID unlocked only during consumer");
    Check(BaseGamePoolsRuntime.UnlockPrefix(BaseGamePlayerPools.CustomIdPrefix + "unknown", ref unlocked), "Unregistered prefixed ID stays native");
    Check(BaseGamePoolsRuntime.UnlockPrefix("native", ref unlocked), "Native IDs stay native");
    BaseGamePoolsRuntime.Restore(scope, null);
    Check(BaseGamePoolsRuntime.UnlockPrefix(customId, ref unlocked), "Authored unlock override ends with consumer");
    Pool pregen = stateOriginal;
    BaseGamePoolsRuntime.PreGeneratePrefix(ref pregen, out scope);
    Check(pregen[0].id == customId && ReferenceEquals(state.freeAgents, stateOriginal), "Pregen replaces only input argument");
    BaseGamePoolsRuntime.Restore(scope, null);
    var team = state.runData.teamData;
    team.forwards.Add(new ForwardData { id = "owned", firstName = "Owned" });
    var starMenu = new OldSquadMenu { m_CampaignState = state, m_Team = team, m_UnlockSystem = state._unlockSystem };
    Cysharp.Threading.Tasks.UniTask unrelatedTask = default;
    Check(BaseGamePoolsRuntime.PopulateStarsPrefix(starMenu, ref unrelatedTask), "Unowned empty reward menu remains native");
    Pool stars = null;
    Check(!BaseGamePoolsRuntime.StarsPrefix(starMenu, ref stars, out scope), "One-star native indexing bypassed");
    Check(stars.Count == 1 && stars[0].firstName == "superstars One" && starMenu.Creates == 1, "One star uses native offer factory");
    Check(stars[0].skaterCategory == SkaterCategory.FreeAgents,
        "Benchwarmer classification must not change the independent superstar pool's existing category");
    Check(state.freeAgents[0].firstName == "superstars One", "Stars independent of FA source");
    BaseGamePoolsRuntime.RandomPrefix(state, out var nestedScope);
    Check(state.freeAgents[0].firstName == "free_agents One", "Nested faceless consumer receives FA source");
    BaseGamePoolsRuntime.Restore(nestedScope, null);
    Check(state.freeAgents[0].firstName == "superstars One", "Nested FA restores star source");
    BaseGamePoolsRuntime.Restore(scope, null);
    Check(ReferenceEquals(state.freeAgents, stateOriginal), "Star source restores native reference");
    var node = new GeneralManagerEvent(); bool gmResult = false;
    Check(!BaseGamePoolsRuntime.ManagerPrefix(node, 0, ref gmResult, out scope) && gmResult, "One GM choice uses bounded path");
    Check(node.Creates == 1 && node.eventChoices.Count == 1 && state.Squad.m_CustomSwapPool == null, "One GM offer from native factory bypasses squad pool");
    BaseGamePoolsRuntime.Restore(scope, null);
    Check(ReferenceEquals(state.Squad.m_CustomSwapPool, squadOriginal) && ReferenceEquals(state.freeAgents, stateOriginal), "GM restores both sources");
    int rerolls = GeneralManagerEvent.Rerolls;
    Check(!BaseGamePoolsRuntime.ManagerPrefix(node, 1, ref gmResult, out scope) && !gmResult, "Previously offered only candidate exhausted");
    Check(node.eventChoices.Count == 0 && GeneralManagerEvent.Rerolls == rerolls + 1, "Exhausted GM follows native reroll/completion path");
    BaseGamePoolsRuntime.Restore(scope, null);
    team.Room = false; team.NumPlayingForwards = 5;
    Check(BaseGamePoolsRuntime.ManagerPrefix(node, 0, ref gmResult, out scope), "Full roster retains native capacity path");
    BaseGamePoolsRuntime.Restore(scope, null); team.Room = true;
    var filteredSource = new Pool();
    foreach (var p in new[] {
        new ForwardData { id = "native", firstName = "Locked" },
        new ForwardData { id = "owned", firstName = "Different name" },
        new ForwardData { id = "different", firstName = "Owned" },
        new ForwardData { id = "excluded", firstName = "Excluded", Excluded = true },
        new ForwardData { id = "gawain", firstName = "Gawain" },
        new ForwardData { id = "previous", firstName = "Previous" },
        new ForwardData { id = "eligible", firstName = "Eligible" } }) filteredSource.Add(p);
    state._unlockSystem.Locked.Add("native"); state.runData.CurrentAct = 2;
    var actualEligible = BaseGamePoolsRuntime.Eligible(filteredSource, state, team, state._unlockSystem, name => name == "Previous");
    Check(actualEligible.Count == 1 && actualEligible[0].id == "eligible", "Actual adapter preserves native unlock/owned-ID/owned-name/squad/Gawain/previous exclusions");
    state.runData.CurrentAct = 1;
    foreach (string kind in new[] { "superstars", "free_agents" })
    {
        File.WriteAllText(Path.Combine(BaseGamePlayerPools.PoolDirectory(root, kind), "two.txt"), "Name = " + kind + " Two\n");
        selected.Pools[kind] = new[] { "two.txt", "one.txt" };
    }
    BaseGamePlayerPools.Save(root, selected);
    BaseGamePoolsRuntime.StarsPrefix(starMenu, ref stars, out scope);
    Check(stars.Count == 2, "Two eligible stars never read third index");
    BaseGamePoolsRuntime.Restore(scope, null);
    node._previouslyOfferedNames.Clear();
    Check(BaseGamePoolsRuntime.ManagerPrefix(node, 0, ref gmResult, out scope), "Two GM offers preserve native implementation");
    Check(state.freeAgents[1].id == customId, "Reorder keeps custom identity");
    BaseGamePoolsRuntime.Restore(scope, null);
    // Exhaust all selected stars by roster name, preserving >=1 selected files.
    team.forwards.Add(new ForwardData { id = "star-one-owned", firstName = "superstars One" });
    team.forwards.Add(new ForwardData { id = "star-two-owned", firstName = "superstars Two" });
    Check(!BaseGamePoolsRuntime.StarsPrefix(starMenu, ref stars, out scope) && stars.Count == 0, "Nonempty authored pool may have zero eligible stars");
    BaseGamePoolsRuntime.Restore(scope, null);
    starMenu.m_SuperStarSkaters = stars;
    // The real FullSequence has not shown relic choices at this point.
    // Its native power-up recipient is Angus, which need not be the first
    // forward temporarily borrowed for the absent-star index guard.
    var nativeAngus = new ForwardData { id = "native-angus", firstName = "Angus McNineties" };
    team.forwards.Add(nativeAngus); starMenu.m_AngusData = nativeAngus;
    foreach (string name in new[] { "Native starter one", "Native starter two", "Native starter three" })
        starMenu.NativeRandomSkaters.Add(new ForwardData { id = name, firstName = name });
    int rosterCount = team.forwards.Count; int stat = team.forwards[0].Stat;
    Cysharp.Threading.Tasks.UniTask task = default;
    Harness.Frames.NativeCallbackDepth++;
    try { Check(!BaseGamePoolsRuntime.PopulateStarsPrefix(starMenu, ref task), "Empty superstar stage completes without showing unusable menu"); }
    finally { Harness.Frames.NativeCallbackDepth--; }
    Harness.Frames.Advance(6);
    Check(starMenu.RelicVisible, "The shipped animator must consume SuperStar before Relic; direct PowerUp-to-Relic stalls");
    Check(!starMenu.ReenteredNativeCaller && starMenu.RevealCancellations == 1 && starMenu.RelicStartFrame > 0,
        "Native caller finishes before reveal cancellation and frame-confirmed relic continuation");
    Check(starMenu.RelicStages == 1 && starMenu.m_CurrentRelicChoice == -1 && starMenu.Exits == 0,
        "Zero-star FullSequence continues to the still-unselected native relic stage before exiting");
    Check(task != null && task.IsCompleted, "The intercepted native caller receives a valid completed task while its menu owns the continuation");
    Check(nativeAngus.ability == null && team.relics.Count == 0 && team.forwards.Count == rosterCount,
        "Native rewards and starters remain ungranted until the user completes remaining choices");
    Cysharp.Threading.Tasks.UniTask duplicateTask = default;
    Check(!BaseGamePoolsRuntime.PopulateStarsPrefix(starMenu, ref duplicateTask) && starMenu.RelicStages == 1 && starMenu.Exits == 0,
        "Repeated empty-stage callbacks do not restart deferred native choices or grant twice");
    starMenu.ChooseRelicAndConfirmStarters();
    Check(starMenu.RewardGrants == 1 && starMenu.Exits == 1 && starMenu.StarterStages == 1 && team.Adds == 3,
        "FullSequence retains native relic and starter stages while suppressing only absent-star insertion");
    Check(nativeAngus.ability == "Tornado" && team.relics.SequenceEqual(new[] { "Brick Wall" }),
        "Native selected ability reaches Angus and selected relic reaches the team");
    Check(team.forwards.Count == rosterCount + 3 && team.forwards[0].Stat == stat && team.forwards[0].ability == null
        && ReferenceEquals(starMenu.m_SuperStarSkaters, stars) && starMenu.m_CurrentSkaterChoice == -1,
        "Native starters are retained without changing the borrowed guard player or acquiring a star");
    Check(BaseGamePoolsRuntime.AddForwardPrefix(team, team.forwards[0]), "Insertion suppression cannot escape grant scope");
    Check(BaseGamePoolsRuntime.PopulateStarsPrefix(starMenu, ref unrelatedTask), "Actual grant completion clears empty-menu ownership");
    starMenu.InitData();
    BaseGamePoolsRuntime.StarsPrefix(starMenu, ref stars, out scope); BaseGamePoolsRuntime.Restore(scope, null);
    starMenu.m_SuperStarSkaters = stars;
    starMenu.FailGrant = true;
    starMenu.m_CurrentRelicChoice = -1;
    BaseGamePoolsRuntime.PopulateStarsPrefix(starMenu, ref task);
    Harness.Frames.Advance(6);
    try { starMenu.ChooseRelicAndConfirmStarters(); throw new Exception("Expected native failure"); }
    catch (InvalidOperationException) { }
    Check(ReferenceEquals(starMenu.m_SuperStarSkaters, stars) && BaseGamePoolsRuntime.AddForwardPrefix(team, team.forwards[0]), "Native grant exception restores all suppression and references");
    Check(BaseGamePoolsRuntime.PopulateStarsPrefix(starMenu, ref unrelatedTask), "Native grant exception also clears deferred ownership");
    starMenu.InitData(); starMenu.FailGrant = false; starMenu.m_Mode = 1; int grants = starMenu.RewardGrants;
    BaseGamePoolsRuntime.StarsPrefix(starMenu, ref stars, out scope); BaseGamePoolsRuntime.Restore(scope, null);
    starMenu.m_SuperStarSkaters = stars;
    int additionsBeforeStarOnly = team.Adds, relicStagesBeforeStarOnly = starMenu.RelicStages;
    BaseGamePoolsRuntime.PopulateStarsPrefix(starMenu, ref task);
    Check(starMenu.RewardGrants == grants && team.Adds == additionsBeforeStarOnly && starMenu.RelicStages == relicStagesBeforeStarOnly,
        "Star-only empty mode grants nothing and does not invent relic stages");
    Check(task != null && task.IsCompleted && BaseGamePoolsRuntime.PopulateStarsPrefix(starMenu, ref unrelatedTask),
        "Star-only exit returns a completed wrapper and clears exact empty-result ownership");
    starMenu.InitData(); starMenu.m_Mode = 0;
    BaseGamePoolsRuntime.StarsPrefix(starMenu, ref stars, out scope); BaseGamePoolsRuntime.Restore(scope, null);
    starMenu.m_SuperStarSkaters = stars;
    int relicStagesBeforeReset = starMenu.RelicStages;
    BaseGamePoolsRuntime.PopulateStarsPrefix(starMenu, ref task);
    starMenu.InitData();
    Harness.Frames.Advance(6);
    Check(starMenu.RelicStages == relicStagesBeforeReset, "Reinitialization prevents the deferred native stage from running at all");
    Check(BaseGamePoolsRuntime.PopulateStarsPrefix(starMenu, ref unrelatedTask),
        "Native menu reinitialization clears an abandoned deferred continuation even with the same empty list");
    // A selected talent uses the same native recipient/grant branch as an ability.
    BaseGamePoolsRuntime.StarsPrefix(starMenu, ref stars, out scope); BaseGamePoolsRuntime.Restore(scope, null);
    starMenu.m_SuperStarSkaters = stars; starMenu.SelectedAbility = null; starMenu.SelectedTalent = "Momentum";
    BaseGamePoolsRuntime.PopulateStarsPrefix(starMenu, ref task); Harness.Frames.Advance(6); starMenu.ChooseRelicAndConfirmStarters();
    Check(nativeAngus.powerups.Any(p => p.name == "Momentum"), "Selected native talent is retained when no superstar is acquired");
    var controlTeam = new TeamData(); var controlAngus = new ForwardData { id = "control-angus" };
    controlTeam.forwards.Add(controlAngus);
    var actualStar = new ForwardData { id = "actual-eligible-star" };
    var controlMenu = new OldSquadMenu { m_Team = controlTeam, m_AngusData = controlAngus,
        m_SuperStarSkaters = new Pool { actualStar }, m_CurrentSkaterChoice = 0, SelectedAbility = "Native control ability" };
    Check(BaseGamePoolsRuntime.PopulateStarsPrefix(controlMenu, ref unrelatedTask), "Eligible-star native sequence remains entirely native");
    controlMenu.m_StepAnimator.SetTrigger(OldSquadMenu.SUPER_STAR); Harness.Frames.Advance(1);
    controlMenu.InstantiateRelics(); Harness.Frames.Advance(1); controlMenu.ChooseRelicAndConfirmStarters();
    Check(controlTeam.forwards.Contains(actualStar) && controlAngus.ability == "Native control ability"
        && controlTeam.relics.Count == 1, "Eligible native control grants its star, Angus ability and team relic normally");
    OldSquadMenu ScheduleEmptyMenu()
    {
        var deferredTeam = new TeamData();
        deferredTeam.forwards.Add(new ForwardData { firstName = "superstars One" });
        deferredTeam.forwards.Add(new ForwardData { firstName = "superstars Two" });
        var deferredMenu = new OldSquadMenu { m_Team = deferredTeam };
        Pool deferredStars = null;
        BaseGamePoolsRuntime.StarsPrefix(deferredMenu, ref deferredStars, out var deferredScope);
        BaseGamePoolsRuntime.Restore(deferredScope, null);
        deferredMenu.m_SuperStarSkaters = deferredStars;
        Cysharp.Threading.Tasks.UniTask deferredTask = null;
        BaseGamePoolsRuntime.PopulateStarsPrefix(deferredMenu, ref deferredTask);
        return deferredMenu;
    }
    var destroyedMenu = ScheduleEmptyMenu(); destroyedMenu.BehaviourDestroy(); Harness.Frames.Advance(8);
    Check(destroyedMenu.RelicStages == 0 && destroyedMenu.RevealCancellations == 0,
        "Menu destruction prevents all stale continuation actions");
    var replacedMenu = ScheduleEmptyMenu(); replacedMenu.m_SuperStarSkaters = new Pool(); Harness.Frames.Advance(8);
    Check(replacedMenu.RelicStages == 0 && replacedMenu.RevealCancellations == 0,
        "A replaced result pointer cannot run its former continuation");
    var blockedMenu = ScheduleEmptyMenu(); blockedMenu.m_StepAnimator.BlockTransitions = true; Harness.Frames.Advance(360);
    Check(blockedMenu.RelicStages == 0 && BaseGamePoolsRuntime.PopulateStarsPrefix(blockedMenu, ref unrelatedTask),
        "A missing native animation transition times out and releases ownership without invoking the wrong stage");
    foreach (string profileChange in new[] { "disabled", "corrupted" })
    {
        selected.Enabled = true; BaseGamePlayerPools.Save(root, selected);
        var pendingTeam = new TeamData();
        var pendingAngus = new ForwardData { id = "pending-angus-" + profileChange };
        pendingTeam.forwards.Add(pendingAngus);
        pendingTeam.forwards.Add(new ForwardData { firstName = "superstars One" });
        pendingTeam.forwards.Add(new ForwardData { firstName = "superstars Two" });
        var pendingMenu = new OldSquadMenu { m_Team = pendingTeam, m_AngusData = pendingAngus };
        pendingMenu.NativeRandomSkaters.Add(new ForwardData { id = "pending-native-starter" });
        Pool pendingStars = null;
        BaseGamePoolsRuntime.StarsPrefix(pendingMenu, ref pendingStars, out scope); BaseGamePoolsRuntime.Restore(scope, null);
        Check(pendingStars.Count == 0, "Profile-change fixture starts with an owned empty result: " + profileChange);
        pendingMenu.m_SuperStarSkaters = pendingStars;
        BaseGamePoolsRuntime.PopulateStarsPrefix(pendingMenu, ref task);
        if (profileChange == "disabled") { selected.Enabled = false; BaseGamePlayerPools.Save(root, selected); }
        else File.WriteAllText(Path.Combine(BaseGamePlayerPools.Folder(root), "settings.txt"), "schema = broken\nenabled = yes\n");
        Harness.Frames.Advance(6);
        pendingMenu.ChooseRelicAndConfirmStarters();
        Check(pendingAngus.ability == "Tornado" && pendingTeam.relics.SequenceEqual(new[] { "Brick Wall" })
            && pendingTeam.forwards.Any(p => p.id == "pending-native-starter"),
            "Already-owned continuation preserves all native grants after settings become " + profileChange);
        var futureMenu = new OldSquadMenu { m_Team = pendingTeam }; Pool futureStars = null;
        Check(BaseGamePoolsRuntime.StarsPrefix(futureMenu, ref futureStars, out scope) && scope == null,
            "Settings gate still prevents new pool generation after profile becomes " + profileChange);
    }
    selected.Enabled = true; BaseGamePlayerPools.Save(root, selected);
    Plugin.IsDefaultMode = false;
    Check(BaseGamePoolsRuntime.Selection() == null, "Saved active profile cannot affect custom campaign");
    BaseGamePoolsRuntime.BenchPrefix(menu, out scope); Check(scope == null, "Custom bench remains native to campaign adapter");
    Check(BaseGamePoolsRuntime.StarsPrefix(starMenu, ref stars, out scope), "Custom superstar path unchanged");
    Plugin.IsDefaultMode = true; selected.Enabled = false; BaseGamePlayerPools.Save(root, selected);
    BaseGamePoolsRuntime.RandomPrefix(state, out scope);
    Check(scope == null && ReferenceEquals(state.freeAgents, stateOriginal), "Disabled profile leaves native source intact");
    selected.Enabled = true;
    var benchFiles = new List<string>();
    for (int i = 0; i < 13; i++)
    {
        string filename = "extra-" + i + ".txt";
        File.WriteAllText(Path.Combine(BaseGamePlayerPools.PoolDirectory(root, "draft_pool"), filename), "Name = Extra " + i + "\n");
        benchFiles.Add(filename);
    }
    selected.Pools["draft_pool"] = benchFiles.ToArray();
    File.WriteAllText(Path.Combine(BaseGamePlayerPools.PoolDirectory(root, "superstars"), "native.txt"),
        "Name = Edited Native\n" + VanillaCopyData.PlayerId + " = native\n" + VanillaCopyData.PlayerAsset + " = native\n");
    selected.Pools["superstars"] = new[] { "native.txt" };
    BaseGamePlayerPools.Save(root, selected);
    var nativePool = BaseGamePoolsRuntime.Candidates(state, "superstars", selected);
    Check(nativePool[0].id == "native" && nativePool[0].firstName == "Edited Native", "Edited exact native variant retains native ID");
    Check(UnityEngine.Resources.Players[0].firstName == "Native", "Native source asset is untouched");
    BaseGamePoolsRuntime.StarsPrefix(starMenu, ref stars, out scope);
    Check(stars.Count == 0, "Locked native candidate remains locked after editing");
    BaseGamePoolsRuntime.Restore(scope, null);
    BaseGamePoolsRuntime.BenchPrefix(menu, out scope);
    Check(state.Squad.m_CustomSwapPool.Count == 13 && state.Squad.m_MaxBenchwarmersOnBench == 3, "Bench candidate pool has no nine-entry cap while capacity stays native");
    BaseGamePoolsRuntime.Restore(scope, null);
    string relocated = Path.Combine(root, "relocated");
    foreach (string kind in BaseGamePlayerPools.Kinds)
    {
        Directory.CreateDirectory(BaseGamePlayerPools.PoolDirectory(relocated, kind));
        foreach (string file in selected.Pools[kind]) File.Copy(Path.Combine(BaseGamePlayerPools.PoolDirectory(root, kind), file), Path.Combine(BaseGamePlayerPools.PoolDirectory(relocated, kind), file));
    }
    BaseGamePlayerPools.Save(relocated, selected); Plugin.ModContentRoot = relocated;
    BaseGamePoolsRuntime.RandomPrefix(state, out scope);
    Check(state.freeAgents[1].id == customId, "Relocating content root preserves authored template ID");
    BaseGamePoolsRuntime.Restore(scope, null);
    selected.Enabled = false; BaseGamePlayerPools.Save(relocated, selected);
    string missing = Path.Combine(BaseGamePlayerPools.PoolDirectory(relocated, "free_agents"), "one.txt");
    string savedPlayer = File.ReadAllText(missing); File.Delete(missing);
    BaseGamePoolsRuntime.RandomPrefix(state, out scope);
    Check(scope == null && ReferenceEquals(state.freeAgents, stateOriginal), "Disabled profile with missing candidate passes through safely");
    File.WriteAllText(missing, savedPlayer);
    selected.Enabled = true; BaseGamePlayerPools.Save(relocated, selected);
    string settings = Path.Combine(BaseGamePlayerPools.Folder(relocated), "settings.txt");
    string savedSettings = File.ReadAllText(settings); File.WriteAllText(settings, "schema = broken\nenabled = yes\n");
    BaseGamePoolsRuntime.BenchPrefix(menu, out scope);
    Check(scope == null && ReferenceEquals(state.Squad.m_CustomSwapPool, squadOriginal), "Corrupt active settings cannot break native bench or leave scopes");
    Check(BaseGamePoolsRuntime.ManagerPrefix(node, 0, ref gmResult, out scope) && scope == null, "Corrupt active settings retain native GM behavior");
    File.WriteAllText(settings, savedSettings);
    string brokenPlayer = Path.Combine(BaseGamePlayerPools.PoolDirectory(relocated, "superstars"), "native.txt");
    File.WriteAllText(brokenPlayer, "Name = Broken native\n" + VanillaCopyData.PlayerId + " = nonexistent\n" + VanillaCopyData.PlayerAsset + " = missing\n");
    Check(BaseGamePoolsRuntime.StarsPrefix(starMenu, ref stars, out scope) && scope == null && ReferenceEquals(state.freeAgents, stateOriginal),
        "Invalid native candidate binding falls back before source replacement");
    Check(BaseGamePoolsRuntime.UnlockPrefix(customId, ref unlocked), "Invalid profiles never leak authored unlock scope");
    Check(Plugin.Log.Errors.Any(message => message.Contains("settings") && message.Contains("Campaign Studio"))
        && Plugin.Log.Errors.Any(message => message.Contains("superstars") && message.Contains("Native pools")), "Invalid profile logs identify the failed source and repair action");
    // A native prototype is an allocation/appearance source, not an implicit
    // talent or ability grant for independently imported player documents.
    var poweredDonor = UnityEngine.Resources.Players[0];
    poweredDonor.powerups.Add(new Rogue.Talent { name = "Unrelated donor power" });
    poweredDonor.ability = "Unrelated donor ability";
    poweredDonor.preventEliminatorRemoval = true;
    string cleanCustom = Path.Combine(BaseGamePlayerPools.PoolDirectory(relocated, "superstars"), "clean-custom.txt");
    File.WriteAllText(cleanCustom, "Name = Clean custom\nTalents =\nAbility =\n");
    selected.Pools["superstars"] = new[] { "clean-custom.txt" }; BaseGamePlayerPools.Save(relocated, selected);
    var cleanPool = BaseGamePoolsRuntime.Candidates(state, "superstars", selected);
    Check(!cleanPool[0].preventEliminatorRemoval, "Custom import must not inherit a special native prototype's eliminator immunity");
    Check(cleanPool[0].powerups.Count == 0 && cleanPool[0].ability == null && cleanPool[0].TalentCacheResets > 0,
        "Talentless custom import clears prototype powers, ability and talent cache");
    Check(poweredDonor.powerups.Count == 1 && poweredDonor.ability == "Unrelated donor ability", "Custom initialization never mutates prototype powers");
    Check(poweredDonor.preventEliminatorRemoval, "Custom initialization preserves the native donor immunity flag");
    File.WriteAllText(Path.Combine(BaseGamePlayerPools.PoolDirectory(relocated, "superstars"), "powered-custom.txt"), "Name = Authored powers\nTalents = Authored talent\nAbility = Authored ability\n");
    selected.Pools["superstars"] = new[] { "powered-custom.txt" }; BaseGamePlayerPools.Save(relocated, selected);
    var poweredPool = BaseGamePoolsRuntime.Candidates(state, "superstars", selected);
    Check(poweredPool[0].powerups.Count == 1 && poweredPool[0].powerups[0].name == "Authored talent" && poweredPool[0].ability == "Authored ability",
        "Explicit imported talents and ability remain after prototype cleanup");
    File.WriteAllText(Path.Combine(BaseGamePlayerPools.PoolDirectory(relocated, "superstars"), "bound-powers.txt"),
        "Name = Native bound powers\n" + VanillaCopyData.PlayerId + " = native\n" + VanillaCopyData.PlayerAsset
        + " = native\nTalents = Bound talent\nAbility = Bound ability\n");
    selected.Pools["superstars"] = new[] { "bound-powers.txt" }; BaseGamePlayerPools.Save(relocated, selected);
    var boundPool = BaseGamePoolsRuntime.Candidates(state, "superstars", selected);
    Check(boundPool[0].id == "native" && boundPool[0].TalentCacheResets == 1
        && boundPool[0].powerups[0].name == "Bound talent" && boundPool[0].ability == "Bound ability",
        "Native-bound candidate retains the existing helper path without custom initialization");
    Check(boundPool[0].preventEliminatorRemoval, "Native-bound candidate preserves its own native eliminator immunity");
    var priorDraftSelection = selected.Pools["draft_pool"];
    poweredDonor.skaterCategory = SkaterCategory.Angus;
    File.WriteAllText(Path.Combine(BaseGamePlayerPools.PoolDirectory(relocated, "draft_pool"), "native-angus.txt"),
        "Name = Native Angus\n" + VanillaCopyData.PlayerId + " = native\n" + VanillaCopyData.PlayerAsset + " = native\n");
    selected.Pools["draft_pool"] = new[] { "native-angus.txt" };
    var nativeDraft = BaseGamePoolsRuntime.Candidates(state, "draft_pool", selected);
    Check(nativeDraft[0].skaterCategory == SkaterCategory.Angus && poweredDonor.skaterCategory == SkaterCategory.Angus,
        "A native-bound draft candidate preserves its stock category rather than acquiring bench-only relic effects");
    selected.Pools["draft_pool"] = priorDraftSelection;
    // Native GM reproduction: an unchanged manifest points at two wrappers
    // whose Unity objects were unloaded after an earlier temporary consumer.
    Tape2Tape.CampaignUtil.CampaignState = state;
    string[] faFiles = BaseGamePlayerPools.SelectedFiles(relocated, "free_agents", selected);
    var fileContents = faFiles.Select(File.ReadAllText).ToArray();
    var fileTimes = faFiles.Select(File.GetLastWriteTimeUtc).ToArray();
    var cachedAgents = BaseGamePoolsRuntime.Candidates(state, "free_agents", selected);
    var originalIds = cachedAgents.Select(p => p.id).ToArray();
    var originalNames = cachedAgents.Select(p => p.FullName).ToArray();
    Check(ReferenceEquals(cachedAgents, BaseGamePoolsRuntime.Candidates(state, "free_agents", selected)), "Live unchanged candidate cache is reused");
    foreach (var player in cachedAgents) player.Destroyed = true;
    Check(cachedAgents[0] == null && !ReferenceEquals(cachedAgents[0], null) && cachedAgents[0].Pointer != IntPtr.Zero,
        "Destroyed native candidate retains its nonnull managed wrapper and pointer");
    var cacheNode = new GeneralManagerEvent(); bool cacheResult = false;
    bool runNative = BaseGamePoolsRuntime.ManagerPrefix(cacheNode, 0, ref cacheResult, out var cacheScope);
    Check(runNative && state.freeAgents.Count == 2 && state.freeAgents.All(p => p != null),
        "GM rebuilds Unity-destroyed cached candidates and retains native two-offer route");
    var rebuiltAgents = state.freeAgents;
    Check(!ReferenceEquals(rebuiltAgents, cachedAgents) && rebuiltAgents.Select(p => p.id).SequenceEqual(originalIds)
        && rebuiltAgents.Select(p => p.FullName).SequenceEqual(originalNames), "Rebuilt candidates preserve authored identity, selection and order");
    BaseGamePoolsRuntime.Restore(cacheScope, null);
    Check(ReferenceEquals(state.freeAgents, stateOriginal), "GM rebuilt-source scope restores the exact native source");
    Check(faFiles.Select(File.ReadAllText).SequenceEqual(fileContents) && faFiles.Select(File.GetLastWriteTimeUtc).SequenceEqual(fileTimes),
        "Cache liveness recovery requires no authored file changes");
    Check(ReferenceEquals(rebuiltAgents, BaseGamePoolsRuntime.Candidates(state, "free_agents", selected)), "Fresh live replacement remains cached");
    rebuiltAgents[0].Destroyed = true;
    var partiallyRebuilt = BaseGamePoolsRuntime.Candidates(state, "free_agents", selected);
    Check(!ReferenceEquals(partiallyRebuilt, rebuiltAgents) && partiallyRebuilt.All(p => p != null)
        && partiallyRebuilt.Select(p => p.id).SequenceEqual(originalIds), "One destroyed candidate rebuilds the same complete selected pool");
    foreach (string kind in new[] { "draft_pool", "superstars" })
    {
        var poolBefore = BaseGamePoolsRuntime.Candidates(state, kind, selected);
        var idsBefore = poolBefore.Select(p => p.id).ToArray();
        Check(ReferenceEquals(poolBefore, BaseGamePoolsRuntime.Candidates(state, kind, selected)), kind + " reuses its unchanged live cache");
        poolBefore[0].Destroyed = true;
        var poolAfter = BaseGamePoolsRuntime.Candidates(state, kind, selected);
        Check(!ReferenceEquals(poolBefore, poolAfter) && poolAfter.All(p => p != null) && poolAfter.Select(p => p.id).SequenceEqual(idsBefore),
            kind + " recovers unloaded candidates without changing native/custom identity or selection");
        Check(ReferenceEquals(poolAfter, BaseGamePoolsRuntime.Candidates(state, kind, selected)), kind + " reuses its recovered live cache");
    }
    // Literal values from the archived R34 Charlotte save. The native relic
    // accepts category 2; older custom bench entries incorrectly saved 3.
    var oldBench = new ForwardData {
        id = "CCF_BASE_POOL_12B2C94588AC5964D2635897A390A3300F4B589A7F9D1AEC267EBE3D36A2353C",
        name = "CCF_BASE_POOL_draft_pool_custom-8c1788297d4a4e728da131d5a8ed497c(Clone)",
        firstName = "Charlotte", lastName = "Chapstick", skaterCategory = SkaterCategory.FreeAgents, Stat = 47,
        ability = "Authored ability", powerups = new() { new Rogue.Talent { name = "Authored talent" } }
    };
    var savedTeam = new TeamData();
    savedTeam.forwards.Add(oldBench);
    savedTeam.forwards.Add(new ForwardData { id = "native-angus", name = "CCF_BASE_POOL_draft_pool_native-angus(Clone)", skaterCategory = SkaterCategory.Angus });
    savedTeam.forwards.Add(new ForwardData { id = "CCF_BASE_POOL_free", name = "CCF_BASE_POOL_free_agents_custom-free(Clone)", skaterCategory = SkaterCategory.FreeAgents });
    savedTeam.forwards.Add(new ForwardData { id = "CCF_BASE_POOL_star", name = "CCF_BASE_POOL_superstars_custom-star(Clone)", skaterCategory = SkaterCategory.Superstar });
    savedTeam.forwards.Add(new ForwardData { id = "CCF_BASE_POOL_unknown", name = "Unrelated player", skaterCategory = SkaterCategory.Default });
    var loaded = new RunData { teamData = savedTeam };
    Plugin.IsDefaultMode = false;
    BaseGamePoolsRuntime.RestoreLoadedBenchwarmers(loaded);
    Check(oldBench.skaterCategory == SkaterCategory.FreeAgents, "Loaded category repair remains inert outside Base Game mode");
    Plugin.IsDefaultMode = true;
    selected.Enabled = false; BaseGamePlayerPools.Save(relocated, selected);
    var oldPowers = oldBench.powerups;
    BaseGamePoolsRuntime.RestoreLoadedBenchwarmers(loaded);
    Check(oldBench.skaterCategory == SkaterCategory.Benchwarmer,
        "An existing saved custom benchwarmer must regain native relic eligibility even when new pool offers are disabled");
    Check(oldBench.Stat == 47 && oldBench.FullName == "Charlotte Chapstick" && oldBench.ability == "Authored ability" && ReferenceEquals(oldBench.powerups, oldPowers),
        "Loaded category repair preserves player attributes, name and authored powers");
    Check(savedTeam.forwards.Skip(1).Select(p => p.skaterCategory).SequenceEqual(new[] { SkaterCategory.Angus, SkaterCategory.FreeAgents, SkaterCategory.Superstar, SkaterCategory.Default }),
        "Native characters, recruits, superstars and unrelated custom players retain their categories");
    BaseGamePoolsRuntime.RestoreLoadedBenchwarmers(loaded);
    Check(oldBench.skaterCategory == SkaterCategory.Benchwarmer && savedTeam.forwards.Count == 5,
        "Repeated load repair does not duplicate players or alter a corrected category");
}
catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
finally { Directory.Delete(root, true); }
Console.WriteLine($"PASS {checks} Base Game pool runtime checks (source-linked, native boundaries simulated)");
return 0;
