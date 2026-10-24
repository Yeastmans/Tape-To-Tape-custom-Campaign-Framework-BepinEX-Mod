using System.Reflection;
using System.Text.Json;
using CampaignCore;
using EndlessMode;
using HarmonyLib;
using Rogue;
using Rogue.Powerups.Repository;
using Talents = Il2CppSystem.Collections.Generic.List<Rogue.Talent>;
using Abilities = Il2CppSystem.Collections.Generic.List<Rogue.Ability>;

var checks = new List<object>();
int failures = 0;
void Check(bool condition, string name)
{
    checks.Add(new { name, passed = condition });
    Console.WriteLine((condition ? "PASS " : "FAIL ") + name);
    if (!condition) failures++;
}
var output = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/qa10-heads/chaos-runtime-checks.json");
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
Plugin.ModFolder = Path.Combine(Path.GetDirectoryName(output)!, "chaos-runtime-fixture");
Directory.CreateDirectory(Plugin.ModFolder);
string poolFile = Path.Combine(Plugin.ModFolder, "reward_pools.txt");
void Write(string text) => File.WriteAllText(poolFile, text);
var nativeResult = new Talents { new Talent { id = "native-sentinel", name = "Native sentinel" } };
var forward = new Data.ForwardData();
var repository = new TalentRepository();
(bool native, Talents result) Pick(TalentCategory category, int count = 2, Talents excluded = null, bool clone = false)
{
    object[] arguments = [repository, category, excluded ?? new Talents(), clone, count, false, true, forward, nativeResult];
    bool native = (bool)typeof(CustomTalentPool).GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, arguments)!;
    return (native, (Talents)arguments[^1]);
}
Abilities PickAbilities(int count, Abilities excluded)
{
    object[] arguments = [new AbilityRepository(), TalentCategory.UsedInCampaignPool, excluded, false, count, new Abilities()];
    bool native = (bool)typeof(CustomAbilityPool).GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, arguments)!;
    if (native) throw new Exception("Blademaster reproduction requires an explicit custom pool");
    return (Abilities)arguments[^1];
}
var normal = new Talent { id = "normal", name = "Normal", category = TalentCategory.UsedInCampaignPool };
var chaos = new ChaosTalent { id = "chaos", name = "Chaos", category = TalentCategory.Chaos };
var chaosPlus = new ChaosTalent { id = "chaos-plus", name = "Chaos plus", category = TalentCategory.Chaos, level = 2 };
var chaosOther = new Talent { id = "chaos-other", name = "Chaos other", category = TalentCategory.Chaos };
var goalie = new Talent { id = "goalie", name = "Goalie", category = TalentCategory.Goalie };
var stats = new Talent { id = "stats", name = "Stats", category = TalentCategory.OnlyStats };
var ab1 = new Ability { id = "ability-one", name = "Ability one" };
var ab2 = new Ability { id = "ability-two", name = "Ability two" };
var ab3 = new Ability { id = "ability-three", name = "Ability three" };
UnityEngine.Resources.Assets = [normal, chaos, chaosPlus, chaosOther, goalie, stats, ab1, ab2, ab3];

Write("Pool Mode Talents = Custom\nSelected Talents = normal, chaos\n");
var pick = Pick(TalentCategory.Chaos);
Check(pick.native && ReferenceEquals(pick.result, nativeResult), "Absent Chaos definition preserves the native result and original picker with normal custom talents");
Check(TalentRepository.WeightedCalls == 0, "Base Game Chaos never invokes the custom weighted picker");
Check(CustomRewardPools.Read("Chaos Talents") == null, "Absent Chaos keys do not migrate ordinary talent selections");

Write("Excluded Talents = chaos, normal\nIncluded Talents = normal\n");
RewardPoolPicks.Refresh();
pick = Pick(TalentCategory.Chaos);
Check(pick.native && Plugin.ExcludedRewardTalentIds.Contains("chaos"), "Legacy ordinary talent exclusions do not intercept Base Game Chaos");
Write("Pool Mode Talents = Custom\nSelected Talents = normal\nPool Mode Chaos Talents = Base Game\nSelected Chaos Talents = chaos\n");
pick = Pick(TalentCategory.Chaos);
Check(pick.native && ReferenceEquals(pick.result, nativeResult), "Explicit Base Game Chaos ignores retained checked entries");

Write("Pool Mode Talents = Base Game\nPool Mode Chaos Talents = Custom\nSelected Chaos Talents = chaos, chaos-other\n");
pick = Pick(TalentCategory.UsedInCampaignPool);
Check(pick.native && ReferenceEquals(pick.result, nativeResult), "A custom Chaos pool does not intercept ordinary Base Game talents");
pick = Pick(TalentCategory.Chaos);
Check(!pick.native && pick.result.Select(t => t.id).ToHashSet().SetEquals(["chaos", "chaos-other"]), "Custom Chaos selects only its independent checked entries");
Check(Plugin.Log.Lines.Any(l => l.Contains("Custom Chaos Talents:")), "Runtime diagnostics name the selected Chaos pool explicitly");

Write("Pool Mode Talents = Custom\nSelected Talents = normal\nPool Mode Chaos Talents = Custom\nSelected Chaos Talents = chaos, chaos-other\nPool Mode Goalie Talents = Custom\nSelected Goalie Talents = goalie\n");
pick = Pick(TalentCategory.UsedInCampaignPool);
Check(!pick.native && pick.result.Single().id == "normal", "Ordinary custom talents remain independent after a Chaos selection");
pick = Pick(TalentCategory.UsedInCampaignGoaliePool);
Check(!pick.native && pick.result.Single().id == "goalie", "Goalie custom talents keep their separate role and definition");
pick = Pick(TalentCategory.Chaos, clone: true);
Check(pick.result.Count == 2 && pick.result.All(t => !ReferenceEquals(t, chaos) && !ReferenceEquals(t, chaosOther)), "Chaos clone requests return clones rather than shared catalog assets");
Check(chaos.name == "Chaos" && chaos.level == 1 && chaos.category == TalentCategory.Chaos, "Selection leaves native catalog data unchanged");

var owned = new Talents { chaos };
pick = Pick(TalentCategory.Chaos, excluded: owned);
Check(pick.result.Single().id == "chaos-other", "Chaos owned exclusions can produce a shorter list without ordinary fallback");
Check(owned.Count == 1 && ReferenceEquals(owned[0], chaos), "Chaos selection does not mutate caller exclusions");
Write("Pool Mode Chaos Talents = Custom\nSelected Chaos Talents = chaos-plus\n");
pick = Pick(TalentCategory.Chaos);
Check(pick.result.Single().id == "chaos-plus" && pick.result[0].level == 2, "Custom Chaos preserves the exact selected level");
Write("Pool Mode Chaos Talents = Custom\nSelected Chaos Talents = chaos, chaos-plus\n");
pick = Pick(TalentCategory.Chaos, excluded: new Talents { chaosPlus });
Check(pick.result.Count == 0 && !pick.native, "An owned Chaos upgrade excludes both its selected exact entry and base family entry");

Write("Pool Mode Talents = Custom\nSelected Talents = normal\nPool Mode Chaos Talents = Custom\nSelected Chaos Talents =\n");
pick = Pick(TalentCategory.Chaos);
Check(!pick.native && pick.result.Count == 0, "An explicitly empty custom Chaos pool stays empty without native or normal fallback");
Write("Pool Mode Chaos Talents = Custom\nSelected Chaos Talents = removed-native-asset\n");
pick = Pick(TalentCategory.Chaos);
Check(!pick.native && pick.result.Count == 0, "Missing selected Chaos assets do not cause an unchecked fallback");
Write("Pool Mode Chaos Talents = Custom\nSelected Chaos Talents = goalie, chaos\n");
pick = Pick(TalentCategory.Chaos);
Check(pick.result.Count == 1 && pick.result[0].id == "chaos", "A skater Chaos request cannot offer a goalie-only asset");
Write("Pool Mode Talents = Custom\nSelected Talents = normal, stats\n");
pick = Pick(TalentCategory.OnlyStats);
Check(pick.result.Single().id == "stats", "Ordinary stat-only selection retains its native role restriction");

Write("Pool Mode Chaos Talents = Custom\nSelected Chaos Talents = chaos\n");
Plugin.IsDefaultMode = true;
pick = Pick(TalentCategory.Chaos);
Check(pick.native && ReferenceEquals(pick.result, nativeResult), "Default campaign mode bypasses authored Chaos pools");
Plugin.IsDefaultMode = false;
bool probeNative = RewardPoolPicks.Probe(() => Pick(TalentCategory.Chaos).native);
Check(probeNative && !RewardPoolPicks.Probing, "A native probe bypasses custom Chaos and restores probe state afterward");
try { RewardPoolPicks.Probe<int>(() => throw new IOException("expected probe exception")); } catch (IOException) { }
Check(!RewardPoolPicks.Probing && !Pick(TalentCategory.Chaos).native, "A failed probe restores subsequent ordinary custom selection");

float weight = 0;
CustomRewardPools.EnsureWeight("chaos", ref weight);
Check(weight == 0, "A saved custom Chaos definition does not change weights outside its active selection");
bool selectedWeight = false, foreignWeight = false, positiveWeight = false, nanWeight = false;
TalentRepository.WeightedOverride = (source, count, f) =>
{
    float selected = 0, foreign = 0, positive = 8, nan = float.NaN;
    CustomRewardPools.EnsureWeight("chaos", ref selected);
    CustomRewardPools.EnsureWeight("normal", ref foreign);
    CustomRewardPools.EnsureWeight("chaos", ref positive);
    CustomRewardPools.EnsureWeight("chaos", ref nan);
    selectedWeight = selected == 1; foreignWeight = foreign == 0; positiveWeight = positive == 8; nanWeight = nan == 1;
    return new Talents { chaos, chaos, normal };
};
pick = Pick(TalentCategory.Chaos);
Check(selectedWeight && foreignWeight && positiveWeight && nanWeight, "Weight repair is scoped to selected Chaos IDs and preserves existing positive weights");
Check(pick.result.Count == 1 && pick.result[0].id == "chaos", "Returned duplicate or unchecked weighted choices cannot escape the selected pool");
weight = 0; CustomRewardPools.EnsureWeight("chaos", ref weight);
Check(weight == 0, "Successful custom selection restores the prior native weight scope");
TalentRepository.WeightedOverride = (_, _, _) => throw new IOException("expected weighted picker failure");
pick = Pick(TalentCategory.Chaos);
weight = 0; CustomRewardPools.EnsureWeight("chaos", ref weight);
Check(!pick.native && pick.result.Count == 0 && weight == 0, "Picker exceptions keep custom selection empty and restore native weight scope");
TalentRepository.WeightedOverride = null;

bool nestedRestored = false;
CustomRewardPools.Select("outer", 1, new[] { chaos }, new Talents(), t => t.id, t => t,
    (list, count) =>
    {
        CustomRewardPools.Select("inner", 1, new[] { normal }, new Talents(), t => t.id, t => t,
            (inner, _) => inner, t => t);
        float outer = 0, innerWeight = 0;
        CustomRewardPools.EnsureWeight("chaos", ref outer);
        CustomRewardPools.EnsureWeight("normal", ref innerWeight);
        nestedRestored = outer == 1 && innerWeight == 0;
        return list;
    }, t => t);
Check(nestedRestored, "Nested selected pools restore the outer allow-list rather than leaking inner weights");

var retired = new[] { typeof(TalentPoolPicks), typeof(TalentPoolUpgrade), typeof(RelicPoolPicks), typeof(RelicPoolUpgrade), typeof(AbilityPoolPicks) };
Check(retired.All(t => !t.IsDefined(typeof(HarmonyPatch))), "Legacy exclusion and probe patches stay retired with no HarmonyPatch target");
var liveTalentPatches = Assembly.GetExecutingAssembly().GetTypes()
    .Select(t => (type: t, patch: t.GetCustomAttribute<HarmonyPatch>()))
    .Where(x => x.patch?.Type == typeof(TalentRepository) && x.patch.Method == nameof(TalentRepository.GetRandomTalents)).ToArray();
Check(liveTalentPatches.Length == 1 && liveTalentPatches[0].type == typeof(CustomTalentPool), "Only the independent custom-pool hook is registered on GetRandomTalents");

// This is a native-control-flow reproduction, not execution of the native event.
// Current build 25837690: 37DC035 computes 4-selectedPlayers, 37DC0D1
// loops that requested count, and 37DC1F3 reads returnedAbilities[i].
// Production CustomAbilityPool supplies the short returned lists below.
int NativeBlademasterLoop(int eligiblePlayers)
{
    var excluded = new Abilities();
    int chosenPlayers = Math.Min(3, eligiblePlayers), cards = 0;
    for (int player = 0; player < chosenPlayers; player++)
    {
        int requested = 4 - chosenPlayers;
        var offered = PickAbilities(requested, excluded);
        excluded.AddRange(offered);
        for (int choice = 0; choice < requested; choice++)
        {
            _ = offered[choice].id;
            if (++cards >= 3) return cards;
        }
    }
    return cards;
}
Write("Pool Mode Abilities = Custom\nSelected Abilities = ability-one, ability-two, ability-three\n");
for (int players = 1; players <= 3; players++)
    Check(NativeBlademasterLoop(players) == 3, $"Native-shaped Blademaster loop succeeds with three eligible abilities for {players} eligible skaters");
Write("Pool Mode Abilities = Custom\nSelected Abilities = ability-one\n");
Check(NativeBlademasterLoop(0) == 0, "No-ability-candidate Blademaster state does not enter the risky short-list loop");
int reproduced = 0;
for (int players = 1; players <= 3; players++)
{
    bool threw = false;
    try { NativeBlademasterLoop(players); } catch (ArgumentOutOfRangeException) { threw = true; reproduced++; }
    Check(threw, $"Known defect reproduced: one selected ability causes native-shaped indexing failure for {players} eligible skaters");
}
Write("Pool Mode Abilities = Custom\nSelected Abilities =\n");
bool emptyThrew = false;
try { NativeBlademasterLoop(1); } catch (ArgumentOutOfRangeException) { emptyThrew = true; }
Check(emptyThrew, "Known defect reproduced: empty ability pool reaches unchecked first index for an eligible skater");

int blademasterStart = checks.Count;
Rogue.NPC.BlademasterNPC.BlademasterEvent Blade(int players)
{
    var node = new Rogue.NPC.BlademasterNPC.BlademasterEvent();
    for (int i = 0; i < players; i++) node.Eligible.Add(new() { name = "Skater " + i });
    return node;
}
string[] allAbilityIds = [ab1.id, ab2.id, ab3.id];
var state = new State.CampaignState();
for (int abilityCount = 0; abilityCount <= 3; abilityCount++)
for (int playerCount = 1; playerCount <= 3; playerCount++)
{
    Write("Pool Mode Abilities = Custom\nSelected Abilities = " + string.Join(", ", allAbilityIds.Take(abilityCount)) + "\n");
    var node = Blade(playerCount);
    bool native = CustomBlademasterPool.Prefix(node, state);
    Check(!native && node.eventChoices.Count == abilityCount,
        $"Bounded Blademaster offers {abilityCount} selected abilities for {playerCount} eligible skaters without native fallback");
    Check(node.eventChoices.Select(c => ((Ability)c.PowerUpOrRelic).id).Distinct().Count() == abilityCount,
        $"Blademaster excludes previously offered ability IDs across {playerCount} skaters with {abilityCount} choices");
    Check(node.EligibilityCalls == 1 && ReferenceEquals(node.LastState, state),
        $"Native skater eligibility receives the current campaign for {playerCount} skaters and {abilityCount} choices");
    foreach (var choice in node.eventChoices)
    {
        foreach (var player in node.Eligible) player.ability = null;
        choice.onSelection();
        Check(ReferenceEquals(choice.Skater.ability, choice.PowerUpOrRelic) && node.Eligible.Where(f => f != choice.Skater).All(f => f.ability == null),
            $"Card callback assigns only the displayed {((Ability)choice.PowerUpOrRelic).id} to {choice.Skater.FullName}");
    }
    Check(node.eventChoices.All(c => c.interactable && c.nodeType == STS.Map.NodeType.Blademaster && c.ChoiceType == STS.Map.EEventChoiceType.PowerUp
        && c.choicesTermParams.Count == 0 && c.buttonName == null && !c.SuppressRewardTracking && c.PowerUpOrRelic2 == null
        && c.translatedButtonName == "Learn " + c.PowerUpOrRelic.name + ": " + c.Skater.FullName
        && c.CustomLocalizedTexts.Single() == c.PowerUpOrRelic.name),
        $"Blademaster preserves native new-ability card data for {abilityCount} choices and {playerCount} skaters");
    Check(node.eventChoices.All(c => !UnityEngine.Resources.Assets.Contains(c.PowerUpOrRelic)),
        $"Blademaster cards use cloned abilities for {abilityCount} choices and {playerCount} skaters");
}
Write("Pool Mode Abilities = Custom\nSelected Abilities = ability-one, ability-two, ability-three\n");
var noPlayers = Blade(0);
Check(!CustomBlademasterPool.Prefix(noPlayers, state) && noPlayers.eventChoices.Count == 0,
    "Custom Blademaster with no eligible skaters leaves an empty offer for the native no-choices exit");
for (int existing = 1; existing <= 3; existing++)
{
    var node = Blade(3);
    Action existingCallback = () => { };
    for (int i = 0; i < existing; i++) node.eventChoices.Add(new() { translatedButtonName = "Existing upgrade " + i, onSelection = existingCallback });
    bool native = CustomBlademasterPool.Prefix(node, state);
    Check(!native && node.eventChoices.Count == 3 && node.eventChoices.Take(existing).All(c => ReferenceEquals(c.onSelection, existingCallback))
        && node.eventChoices.Take(existing).Select(c => c.translatedButtonName).SequenceEqual(Enumerable.Range(0, existing).Select(i => "Existing upgrade " + i)),
        $"Blademaster retains {existing} existing upgrade cards and fills only the remaining offer slots");
}
Write("Pool Mode Abilities = Custom\nSelected Abilities =\n");
var retainedUpgrade = Blade(2);
retainedUpgrade.eventChoices.Add(new() { translatedButtonName = "Owned upgrade" });
Check(!CustomBlademasterPool.Prefix(retainedUpgrade, state) && retainedUpgrade.eventChoices.Single().translatedButtonName == "Owned upgrade",
    "An exhausted random ability pool does not remove an explicit owned upgrade");
Write("Pool Mode Abilities = Base Game\nSelected Abilities = ability-one\n");
var nativeBlade = Blade(2);
Check(CustomBlademasterPool.Prefix(nativeBlade, state) && nativeBlade.EligibilityCalls == 0 && nativeBlade.eventChoices.Count == 0,
    "Base Game ability mode leaves the original Blademaster method and eligibility work untouched");
Write("Pool Mode Abilities = Custom\nSelected Abilities = ability-one\n");
Plugin.IsDefaultMode = true;
Check(CustomBlademasterPool.Prefix(nativeBlade, state) && nativeBlade.EligibilityCalls == 0, "Default campaign Blademaster remains native");
Plugin.IsDefaultMode = false;
AbilityRepository.ThrowWeighted = true;
retainedUpgrade = Blade(2); retainedUpgrade.eventChoices.Add(new() { translatedButtonName = "Owned upgrade" });
Check(!CustomBlademasterPool.Prefix(retainedUpgrade, state) && retainedUpgrade.eventChoices.Single().translatedButtonName == "Owned upgrade",
    "A failed custom picker preserves an existing upgrade without retrying an unchecked native fallback");
AbilityRepository.ThrowWeighted = false;
Write("Pool Mode Abilities = Custom\nSelected Abilities = ability-one, ability-two, ability-three\n");
AbilityRepository.WeightedCalls = 0; AbilityRepository.ThrowWeightedAt = 2;
var partial = Blade(3);
Check(!CustomBlademasterPool.Prefix(partial, state) && partial.eventChoices.Count == 1
    && partial.eventChoices[0].translatedButtonName == "Learn Ability one: " + partial.eventChoices[0].Skater.FullName,
    "A later player picker failure retains only the previously completed native-shaped card");
partial.eventChoices[0].onSelection();
Check(ReferenceEquals(partial.eventChoices[0].Skater.ability, partial.eventChoices[0].PowerUpOrRelic),
    "A retained card after a later picker failure still assigns its exact displayed ability");
float retainedWeight = 0; CustomRewardPools.EnsureWeight(ab1.id, ref retainedWeight);
Check(retainedWeight == 0 && Plugin.Log.Lines.Any(l => l.Contains("retaining 1 complete choices without native fallback")),
    "Partial-offer failure is logged and restores the custom weighting scope");
AbilityRepository.ThrowWeightedAt = 0;
var replacementPlayer = new Data.ForwardData { name = "Replace check", ability = ab1 };
var replacement = CustomBlademasterPool.CreateChoice(replacementPlayer, ab2);
Check(replacement.ChoiceType == STS.Map.EEventChoiceType.PowerUpSwap && ReferenceEquals(replacement.PowerUpOrRelic2, ab1),
    "Native replacement card shape preserves the prior ability when a caller supplies one");
replacement.onSelection();
Check(ReferenceEquals(replacementPlayer.ability, ab2), "Replacement callback assigns the exact displayed ability");
Check(I2.Loc.LocalizationManager.LastKey == "npc/blademaster/new_ability_card_title", "Blademaster uses the native new-ability translation key");
Check(typeof(CustomBlademasterPool).GetCustomAttribute<HarmonyPatch>()!.Method == "OldBlademasterEvent", "The bounded adapter targets only the old new-ability fallback, not explicit upgrade or locked reward methods");
int blademasterChecks = checks.Count - blademasterStart;

int boxedCardStart = checks.Count;
var boxedPlayer = new Data.ForwardData { name = "Boxed native card" };
var localizedAbility = new Ability { id = "localized-card", name = "Internal asset", LocalizedName = "Localized Dash+" };
var boxedChoice = CustomBlademasterPool.CreateChoice(boxedPlayer, localizedAbility);
var brokenCopy = Il2CppInterop.Runtime.NativeCardMemory.CopyArgument(boxedChoice.Pointer);
Check(boxedChoice.ChoiceType == STS.Map.EEventChoiceType.PowerUp && brokenCopy.ChoiceType == STS.Map.EEventChoiceType.Skater,
    "Known live defect reproduced: passing a boxed EventChoice header to native Add shifts interactable into the Skater choice discriminator");
var boxedNode = Blade(0);
int unboxBefore = Il2CppInterop.Runtime.IL2CPP.UnboxCalls;
CustomBlademasterPool.AddChoice(boxedNode, boxedChoice);
var unboxedCopy = boxedNode.eventChoices.Single();
Check(Il2CppInterop.Runtime.IL2CPP.UnboxCalls == unboxBefore + 1 && unboxedCopy.ChoiceType == STS.Map.EEventChoiceType.PowerUp,
    "Production AddChoice passes the unboxed value payload to the native list method");
Check(!ReferenceEquals(unboxedCopy, boxedChoice) && unboxedCopy.interactable && ReferenceEquals(unboxedCopy.Skater, boxedPlayer)
    && ReferenceEquals(unboxedCopy.PowerUpOrRelic, localizedAbility) && unboxedCopy.PowerUpOrRelic2 == null,
    "Unboxed list copy preserves the complete native card data and reference fields");
Check(unboxedCopy.translatedButtonName == "Learn Localized Dash+: Boxed native card"
    && unboxedCopy.CustomLocalizedTexts.Single() == "Localized Dash+",
    "Native card text uses the localized power-up name rather than the Unity asset name");
unboxedCopy.onSelection();
Check(ReferenceEquals(boxedPlayer.ability, localizedAbility), "Copied native card retains its exact recipient callback");
var swapChoice = CustomBlademasterPool.CreateChoice(boxedPlayer, ab2);
CustomBlademasterPool.AddChoice(boxedNode, swapChoice);
Check(boxedNode.eventChoices[1].ChoiceType == STS.Map.EEventChoiceType.PowerUpSwap
    && ReferenceEquals(boxedNode.eventChoices[1].PowerUpOrRelic2, localizedAbility), "Unboxed copy preserves replacement card type and prior ability");
foreach (bool missing in new[] { true, false })
{
    Il2CppInterop.Runtime.IL2CPP.MissingMethod = missing;
    Il2CppInterop.Runtime.IL2CPP.FailInvoke = !missing;
    bool rejected = false;
    try { CustomBlademasterPool.AddChoice(boxedNode, boxedChoice); } catch (Exception) { rejected = true; }
    Check(rejected && boxedNode.eventChoices.Count == 2, missing
        ? "Missing native Add method rejects without adding a malformed card"
        : "Native Add exception is surfaced without adding a malformed card");
}
Il2CppInterop.Runtime.IL2CPP.MissingMethod = Il2CppInterop.Runtime.IL2CPP.FailInvoke = false;
int boxedCardChecks = checks.Count - boxedCardStart;

int policyStart = checks.Count;
var policyTeam = new TeamConfig();
Plugin.PlayerTeamConfigs["policy"] = policyTeam;
var policyState = new State.CampaignState { runData = new() { squadId = "Custom_policy" } };
foreach (string poolMode in new[] { "Custom", "Base Game" })
foreach (string template in new[] { "Basic Squad", "Tchel Squad" })
foreach (string setting in new[] { "On", "Off", "Inherit" })
foreach (int existing in new[] { 0, 1, 2 })
{
    policyTeam.SquadTemplate = template;
    policyTeam.SquadBehaviors["Squad Behavior no_ability_offers"] = setting;
    bool suppressed = setting == "On" || (setting == "Inherit" && template == "Tchel Squad");
    Write($"Pool Mode Abilities = {poolMode}\nSelected Abilities = ability-one\n");
    var policyNode = Blade(3);
    int upgradeHits = 0;
    for (int i = 0; i < existing; i++) policyNode.eventChoices.Add(new() {
        ChoiceType = STS.Map.EEventChoiceType.PowerUpUpgrade, translatedButtonName = "Owned " + i, onSelection = () => upgradeHits++ });
    bool native = CustomBlademasterPool.Prefix(policyNode, policyState);
    Check(suppressed
        ? !native && policyNode.EligibilityCalls == 0 && policyNode.eventChoices.Count == existing
        : poolMode == "Base Game" ? native && policyNode.EligibilityCalls == 0 && policyNode.eventChoices.Count == existing
        : !native && policyNode.EligibilityCalls == 1 && policyNode.eventChoices.Count == existing + 1,
        $"Effective no_ability_offers {template}/{setting}, {poolMode}, {existing} upgrades: suppress only new ability fallback");
    foreach (var upgrade in policyNode.eventChoices.Take(existing)) upgrade.onSelection();
    Check(upgradeHits == existing && policyNode.eventChoices.Take(existing).All(c => c.ChoiceType == STS.Map.EEventChoiceType.PowerUpUpgrade),
        $"Existing upgrade data/callbacks remain intact for {template}/{setting}, {poolMode}, {existing} upgrades");
}
policyTeam.SquadBehaviors["Squad Behavior no_ability_offers"] = "On";
Write("Pool Mode Abilities = Base Game\n");
foreach (string squadId in new[] { "Native_tchel", "Custom_unknown", null })
{
    var untouchedNode = Blade(3);
    Check(CustomBlademasterPool.Prefix(untouchedNode, new State.CampaignState { runData = new() { squadId = squadId } })
        && untouchedNode.EligibilityCalls == 0, "Unconfigured/native squad retains original fallback: " + (squadId ?? "null"));
}
Plugin.IsDefaultMode = true;
Check(CustomBlademasterPool.Prefix(Blade(3), policyState), "Default campaign ignores stale custom no_ability_offers settings");
Plugin.IsDefaultMode = false;
Plugin.PlayerTeamConfigs.Clear();
int policyChecks = checks.Count - policyStart;

int crossCategoryStart = checks.Count;
Write("Pool Mode Talents = Custom\nSelected Talents = normal, chaos\nPool Mode Chaos Talents = Custom\nSelected Chaos Talents = chaos, stats\n");
var regularCross = Pick(TalentCategory.UsedInCampaignPool, 10);
var chaosCross = Pick(TalentCategory.Chaos, 10);
Check(!regularCross.native && regularCross.result.Select(t => t.id).ToHashSet().SetEquals(["normal", "chaos"]),
    "Regular custom talents can explicitly select both regular and Chaos-category skater assets");
Check(!chaosCross.native && chaosCross.result.Select(t => t.id).ToHashSet().SetEquals(["chaos", "stats"]),
    "Chaos custom talents can explicitly select both Chaos and ordinary skater assets");
Check(!regularCross.result.Any(t => t.id == "stats") && !chaosCross.result.Any(t => t.id == "normal"),
    "Cross-category expansion does not merge the two selected allow-lists");
Check(regularCross.result.Count(t => t.id == "chaos") == 1 && chaosCross.result.Count(t => t.id == "chaos") == 1,
    "An overlapping selected asset remains independently available once in each pool");
regularCross = Pick(TalentCategory.UsedInCampaignPool, 10, new Talents { chaos });
chaosCross = Pick(TalentCategory.Chaos, 10);
Check(regularCross.result.Single().id == "normal" && chaosCross.result.Count == 2,
    "Ownership exclusions for one caller do not globally consume an overlapping asset from the other pool");
Write("Pool Mode Talents = Custom\nSelected Talents = normal\nPool Mode Chaos Talents = Custom\nSelected Chaos Talents = chaos, stats\n");
regularCross = Pick(TalentCategory.UsedInCampaignPool, 10);
chaosCross = Pick(TalentCategory.Chaos, 10);
Check(regularCross.result.Single().id == "normal" && chaosCross.result.Select(t => t.id).ToHashSet().SetEquals(["chaos", "stats"]),
    "Removing an overlapping asset from Regular leaves the Chaos saved selection intact");
Write("Pool Mode Talents = Base Game\nSelected Talents = normal, chaos\nPool Mode Chaos Talents = Custom\nSelected Chaos Talents = normal\n");
regularCross = Pick(TalentCategory.UsedInCampaignPool, 10);
chaosCross = Pick(TalentCategory.Chaos, 10);
Check(regularCross.native && ReferenceEquals(regularCross.result, nativeResult) && chaosCross.result.Single().id == "normal",
    "Base Game Regular remains native while Custom Chaos offers an explicitly selected regular talent");
foreach (bool regularExpansion in new[] { false, true })
foreach (bool chaosExpansion in new[] { false, true })
{
    var document = new CampaignDocument("");
    var regularDefinition = new RewardPoolDefinition { UseBaseGame = false, IncludeOtherCategory = regularExpansion };
    regularDefinition.Selected.UnionWith(["normal", "chaos"]);
    var chaosDefinition = new RewardPoolDefinition { UseBaseGame = false, IncludeOtherCategory = chaosExpansion };
    chaosDefinition.Selected.UnionWith(["chaos", "stats"]);
    regularDefinition.Write(document, "Talents");
    chaosDefinition.Write(document, RewardPoolDefinition.ChaosTalents);
    Write(document.ToString());
    regularCross = Pick(TalentCategory.UsedInCampaignPool, 10);
    chaosCross = Pick(TalentCategory.Chaos, 10);
    Check(regularCross.result.Select(t => t.id).ToHashSet().SetEquals(["normal", "chaos"])
        && chaosCross.result.Select(t => t.id).ToHashSet().SetEquals(["chaos", "stats"]),
        $"Catalog expansion preferences regular={regularExpansion}/Chaos={chaosExpansion} leave both exact runtime allow-lists unchanged");
    Check(document.Get(RewardPoolDefinition.IncludeChaosTalentsKey) == (regularExpansion ? "yes" : "no")
        && document.Get(RewardPoolDefinition.IncludeRegularTalentsKey) == (chaosExpansion ? "yes" : "no"),
        $"Catalog preferences save independently for regular={regularExpansion}/Chaos={chaosExpansion}");
}
int crossCategoryChecks = checks.Count - crossCategoryStart;

var regularRelic = new Relic { id = "regular-relic", name = "Regular" };
var chaosRelic = new Relic { id = "chaos-relic", name = "Chaos", category = RelicCategory.Chaos };
UnityEngine.Resources.Assets = [regularRelic, chaosRelic];
(bool native, Il2CppSystem.Collections.Generic.List<Relic> list) PickRelics(RelicCategory category) {
    object[] arguments = [new Rogue.Relics.Repository.RelicRepository(), category, new Il2CppSystem.Collections.Generic.List<Relic>(), false, 3, null];
    bool native = (bool)typeof(CustomRelicPool).GetMethod("Prefix", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, arguments)!;
    return (native, (Il2CppSystem.Collections.Generic.List<Relic>)arguments[^1]);
}
Write("Pool Mode Relics = Custom\nSelected Relics = regular-relic\nPool Mode Chaos Relics = Custom\nSelected Chaos Relics = chaos-relic\n");
Check(PickRelics(RelicCategory.None).list.Single().id == regularRelic.id && PickRelics(RelicCategory.Chaos).list.Single().id == chaosRelic.id,
    "Regular and Chaos relic category requests use independent selected pools");
Write("Pool Mode Relics = Custom\nSelected Relics = regular-relic\n");
Check(PickRelics(RelicCategory.Chaos).native, "Absent Chaos relic pool preserves native behavior");
var coachA = new CustomCoachDefinition { Name = "Only Chaos", Relics = { "chaos-relic" } };
var coachB = new CustomCoachDefinition { Name = "Only regular", Relics = { "regular-relic" } };
using (new CustomCoachScope(coachA)) {
    Check(PickRelics(RelicCategory.None).list.Single().id == "chaos-relic", "Node pool overrides campaign pool for normal Coach requests");
    Check(PickRelics(RelicCategory.Chaos).list.Single().id == "chaos-relic", "Node scope is exact for Chaos requests too");
    Check(PickRelics(RelicCategory.OnlyStats).list.Count == 0, "Node pool respects stat mode without outside fallback");
    var actChoices = new Il2CppSystem.Collections.Generic.List<Relic>();
    Check(!CustomCoachSpeedrunActPool.Prefix(new Rogue.Relics.Repository.RelicRepository(), new(), ref actChoices)
        && actChoices.Count == 1 && actChoices[0].id == "chaos-relic", "Speedrun act offers only node-selected relics, with no injected preset relic");
    Check(!CustomCoachSpeedrunActPool.Prefix(new Rogue.Relics.Repository.RelicRepository(), new() { chaosRelic }, ref actChoices)
        && actChoices.Count == 0, "Owned node-only speedrun relic exhausts the pool without fallback");
    object[] statArgs = [new Rogue.Relics.Repository.RelicRepository(), new Il2CppSystem.Collections.Generic.List<Relic>(), null];
    Check(!(bool)typeof(CustomStatCoachPool).GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, statArgs)!
        && ((Il2CppSystem.Collections.Generic.List<Relic>)statArgs[2]).Count == 0, "Stat-specific Coach path does not escape an ineligible node pool");
    object[] starterArgs = [null];
    Check(!(bool)typeof(CustomSpeedrunCoachPool).GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, starterArgs)!
        && ((Il2CppSystem.Collections.Generic.List<Relic>)starterArgs[0]).Single().id == "chaos-relic", "Speedrun starter path honors exact node selection");
    using (new CustomCoachScope(coachB)) Check(PickRelics(RelicCategory.None).list.Single().id == "regular-relic", "Nested event uses its own pool");
    Check(PickRelics(RelicCategory.None).list.Single().id == "chaos-relic", "Nested scope restores caller pool");
    Plugin.IsDefaultMode = true;
    Check(PickRelics(RelicCategory.None).native, "Default game ignores a stale custom event scope");
    Plugin.IsDefaultMode = false;
}
Check(PickRelics(RelicCategory.None).list.Single().id == "regular-relic", "Coach scope ends before later rewards");
try { using (new CustomCoachScope(coachA)) throw new InvalidOperationException(); } catch (InvalidOperationException) { }
Check(CustomCoachScope.Current == null, "Exception cleanup cannot leak a node pool");
var untouchedAct = new Il2CppSystem.Collections.Generic.List<Relic> { regularRelic };
Check(CustomCoachSpeedrunActPool.Prefix(new Rogue.Relics.Repository.RelicRepository(), new(), ref untouchedAct) && untouchedAct.Single() == regularRelic,
    "Ordinary speedrun act picker retains native behavior outside node scope");
Write("Pool Mode Relics = Base Game\nPool Mode Chaos Relics = Custom\nSelected Chaos Relics = missing\n");
Check(PickRelics(RelicCategory.None).native && PickRelics(RelicCategory.Chaos).list.Count == 0,
    "Invalid custom Chaos relic pool never falls back to unchecked rewards or changes the ordinary pool");

// Real selection prefixes must enforce the new custom ownership categories,
// even when imported pool fields name an item in several places.
var customDefinitions = CustomRewardDefinition.Kinds.Select(k => new CustomRewardDefinition { Name = k, Kind = k }).ToArray();
Check(CustomRewardEffects.ForTrigger(CustomRewardDefinition.CustomRelics, "Shot").Contains(CustomRewardEffects.PuckGhost)
    && !CustomRewardEffects.ForTrigger(CustomRewardDefinition.CustomRelics, "Goal").Contains(CustomRewardEffects.PuckGhost)
    && !CustomRewardEffects.ForTrigger(CustomRewardDefinition.CustomChaosTalents, "Shot").Contains(CustomRewardEffects.PuckGhost),
    "Ghost Vision's fixed X-Ray component belongs to relics while retaining shot timing and kind restrictions");
foreach (var item in customDefinitions) CustomRewardRuntime.Definitions.Add(item.Id, item);
var customAssets = customDefinitions.Select(d => d.IsRelic
    ? (UnityEngine.Object)new Relic { id = d.Id, name = d.Name }
    : new Talent { id = d.Id, name = d.Name, category = d.IsGoalie ? TalentCategory.Goalie : d.Kind == CustomRewardDefinition.CustomChaosTalents ? TalentCategory.Chaos : TalentCategory.None }).ToArray();
UnityEngine.Resources.Assets = customAssets;
string allCustomIds = string.Join(",", customDefinitions.Select(d => d.Id));
Write(string.Join("\n", new[] { "Talents", "Chaos Talents", "Goalie Talents", "Relics" }.Select(k => "Pool Mode " + k + " = Custom\nSelected " + k + " = " + allCustomIds)));
foreach (var pair in new[] { (TalentCategory.None, CustomRewardDefinition.CustomTalents), (TalentCategory.Chaos, CustomRewardDefinition.CustomChaosTalents), (TalentCategory.Goalie, CustomRewardDefinition.CustomGoalieTalents) })
    Check(Pick(pair.Item1, 9).result.Select(t => t.id).SequenceEqual(customDefinitions.Where(d => d.Kind == pair.Item2).Select(d => d.Id)), "Runtime offers only the matching custom kind: " + pair.Item2);
Check(PickRelics(RelicCategory.None).list.Single().id == customDefinitions.Single(d => d.IsRelic).Id, "Runtime relic rewards reject custom talent IDs in their saved pool");
customDefinitions.Single(d => d.IsRelic).NeedsRedesign = true;
Check(PickRelics(RelicCategory.None).list.Count == 0, "Runtime excludes a legacy draft even if its ID remains selected");
CustomRewardRuntime.Definitions.Clear();

var scoringTeam = new Tape2Tape.Team(); var defendingTeam = new Tape2Tape.Team(); scoringTeam.otherTeam = defendingTeam; defendingTeam.otherTeam = scoringTeam;
Events.GoalScoredArg Goal() => new() { goalType = NHL94.GoalType.GOOD, scorer = new Skaters.Skater { Team = scoringTeam } };
var denier = new Rogue.Relics.Components.GoalDenierComponent { Pointer = (IntPtr)7, _team = defendingTeam, _disableActionCoroutine = new object() };
int awards = 0;
UnityEngine.Time.frameCount = 1;
var accepted = Goal(); CustomRewardGoalConfirmation.Begin(accepted); CustomRewardGoalConfirmation.Defer(() => awards++); CustomRewardGoalConfirmation.Processed(accepted);
CustomRewardGoalConfirmation.Tick(); Check(awards == 0, "A goal waits until all same-frame native listeners have finished");
UnityEngine.Time.frameCount++; CustomRewardGoalConfirmation.Tick(); CustomRewardGoalConfirmation.Tick();
Check(awards == 1, "An uncontested accepted goal awards exactly once");
var pending = Goal(); CustomRewardGoalConfirmation.Begin(pending); CustomRewardGoalConfirmation.Defer(() => awards += 10); CustomRewardGoalConfirmation.Open(denier, pending); CustomRewardGoalConfirmation.Processed(pending);
UnityEngine.Time.frameCount++; CustomRewardGoalConfirmation.Tick(); CustomRewardGoalConfirmation.Flush();
Check(awards == 1, "An open native denial window blocks irreversible custom actions, including a completion flush");
CustomRewardGoalConfirmation.Close(denier); CustomRewardGoalConfirmation.Tick();
Check(awards == 1, "Closing input does not commit before the same-frame denial event");
object[] denialState = [null];
typeof(CustomRewardGoalDenied).GetMethod("Prefix", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, denialState);
Check(ReferenceEquals(denialState[0], defendingTeam) && CustomRewardRuntime.Signals.Count == 0, "Goal reversal captures the protected team before clearing the pending goal");
typeof(CustomRewardGoalDenied).GetMethod("Postfix", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, denialState);
Check(CustomRewardRuntime.Signals.Single() == ("Goal reversed", defendingTeam._teamData.id), "The separate payoff fires for the protected team only after score rollback");
UnityEngine.Time.frameCount++; CustomRewardGoalConfirmation.Tick();
Check(awards == 1, "A denied goal discards queued custom actions");
var afterWindow = Goal(); CustomRewardGoalConfirmation.Begin(afterWindow); CustomRewardGoalConfirmation.Defer(() => awards++); CustomRewardGoalConfirmation.Open(denier, afterWindow); CustomRewardGoalConfirmation.Processed(afterWindow);
CustomRewardGoalConfirmation.Close(denier); UnityEngine.Time.frameCount++; CustomRewardGoalConfirmation.Tick();
Check(awards == 2, "A closed, uncontested denial window commits the accepted goal");
var repeated = Goal(); CustomRewardGoalConfirmation.Begin(repeated); CustomRewardGoalConfirmation.Defer(() => awards++); CustomRewardGoalConfirmation.Begin(repeated); CustomRewardGoalConfirmation.Processed(repeated); CustomRewardGoalConfirmation.Flush();
Check(awards == 3, "Native listeners can begin the same transaction without dropping its queued assists or hat tricks");
var abandoned = Goal(); CustomRewardGoalConfirmation.Begin(abandoned); CustomRewardGoalConfirmation.Defer(() => awards++); CustomRewardGoalConfirmation.Processed(abandoned); CustomRewardGoalConfirmation.Clear(); UnityEngine.Time.frameCount++; CustomRewardGoalConfirmation.Tick();
Check(awards == 3, "Leaving a match cancels an unfinished scoring transaction");
var removed = Goal(); CustomRewardGoalConfirmation.Begin(removed); CustomRewardGoalConfirmation.Defer(() => awards++); CustomRewardGoalConfirmation.Open(denier, removed); CustomRewardGoalConfirmation.Processed(removed);
typeof(CustomRewardGoalDenierRemoved).GetMethod("Prefix", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [denier]);
UnityEngine.Time.frameCount++; CustomRewardGoalConfirmation.Tick();
Check(awards == 4, "Removing a native denial component cannot leave custom goal actions permanently suspended");

// Custom assets must not enter native offers, including minimum-choice fallback.
CustomRewardRuntime.Definitions["isolation-custom"] = new CustomRewardDefinition { Id = "isolation-custom" };
var customTalent = new Talent { id = "isolation-custom", name = "CCF_Reward_isolation-custom" };
var customRelic = new Relic { id = "isolation-custom", name = "CCF_Reward_isolation-custom" };
bool Filtered(bool relic) {
 object[] arguments = relic ? [new Il2CppSystem.Collections.Generic.List<Relic> { customRelic }, 3] : [new Talents { customTalent }, 3];
 typeof(EligibleRewardFilter).GetMethod(relic ? "Relics" : "Talents", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, arguments);
 return relic ? !((Il2CppSystem.Collections.Generic.IEnumerable<Relic>)arguments[0]).Any() : !((Il2CppSystem.Collections.Generic.IEnumerable<Talent>)arguments[0]).Any();
}
foreach(bool defaultMode in new[]{true,false}) {
 Plugin.IsDefaultMode=defaultMode;
 Plugin.ExcludedRewardTalentIds.Clear();Plugin.ExcludedRewardRelicIds.Clear();
 Check(Filtered(false)&&Filtered(true), "Custom rewards excluded from " + (defaultMode?"base-game":"unselected custom-campaign") + " offers with no exclusion settings");
 Plugin.ExcludedRewardTalentIds.Add(customTalent.id);Plugin.ExcludedRewardRelicIds.Add(customRelic.id);
 Check(Filtered(false)&&Filtered(true), "Minimum-choice fallback never restores custom rewards in " + (defaultMode?"base-game":"unselected campaign"));
 float unselected=7;CustomRewardPools.EnsureWeight(customTalent.id,ref unselected);
 Check(unselected==0,"Unselected custom weight is zero in " + (defaultMode?"base-game":"custom campaign"));
}
Plugin.IsDefaultMode=false;
bool selectedAllowed=false;
CustomRewardPools.Select("isolation",1,new[]{customTalent},new Talents(),t=>t.id,t=>t,(list,count)=> {
 selectedAllowed=!Filtered(false);float selected=0;CustomRewardPools.EnsureWeight(customTalent.id,ref selected);
 Check(selectedAllowed&&selected==1,"Explicit custom campaign selection permits its custom reward");
 Plugin.IsDefaultMode=true;Check(Filtered(false),"Default mode blocks custom rewards even with a retained selection scope");Plugin.IsDefaultMode=false;
 return list;
},t=>t);
Check(Filtered(false),"Selection scope cannot leak into subsequent native offers");
Plugin.IsDefaultMode=true;
Plugin.ExcludedRewardTalentIds.Add(normal.id);
object[] nativeArgs=[new Talents { normal, customTalent },3];
typeof(EligibleRewardFilter).GetMethod("Talents",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,nativeArgs);
Check(((Il2CppSystem.Collections.Generic.IEnumerable<Talent>)nativeArgs[0]).Single()==normal,"Base game retains native candidates and ignores campaign exclusion settings");
CustomRewardRuntime.Definitions.Remove(customTalent.id);
Check(Filtered(false)&&Filtered(true),"Custom asset identity remains blocked even before its definition is loaded");
Plugin.IsDefaultMode=false;

File.WriteAllText(output, JsonSerializer.Serialize(new
{
    createdUtc = DateTime.UtcNow,
    kind = "Source-linked production reward adapter checks with managed native doubles",
    checks, failed = failures,
    knownNativeConsumerRisk = new { name = "OldBlademasterEvent short ability list", reproducedNonemptyCases = reproduced, emptyThrew, liveNativeExecution = false, repairApplied = true, managedRepairChecks = blademasterChecks },
    crossCategoryChecks, boxedCardChecks, policyChecks,
    limits = new[] { "No native Unity/Harmony/event UI execution.", "Native weighting distribution, menu rendering and gameplay remain unaccepted.", "Blademaster checks deliberately reproduce the current unsafe consumer; they do not mark that behavior as working." }
}, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"{checks.Count - failures}/{checks.Count} source-linked reward checks passed. Report: {output}");
Environment.ExitCode = failures == 0 ? 0 : 1;

internal sealed class ChaosTalent : Talent { }
