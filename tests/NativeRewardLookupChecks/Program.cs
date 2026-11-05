using System.Reflection;
using CampaignCore;
using Data;
using EndlessMode;
using Rogue;
using Tape2Tape;
using static CampaignCore.CustomRewardEffects;

var teams = typeof(CustomRewardTeamCapabilities);
var players = typeof(CustomRewardDataCapabilities);
void Register(Type type, string field, int handle, string value)
    => ((Dictionary<IntPtr,string>)type.GetField(field, BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!).Add(handle, value);
void Set(Type type, string field, object value) => type.GetField(field, BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, value);
void Native(Type type, string field, string method) {
    var target = type.GetField(field, BindingFlags.Static | BindingFlags.NonPublic)!;
    target.SetValue(null, typeof(Originals).GetMethod(method)!.CreateDelegate(target.FieldType));
}
T Callback<T>(Type type, string name) where T : Delegate => type.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!.CreateDelegate<T>();
var live = Callback<Func<IntPtr,IntPtr,byte>>(teams, "Live");
var savedTeam = Callback<Func<IntPtr,IntPtr,byte>>(teams, "Data");
var has = Callback<Func<IntPtr,IntPtr,byte>>(players, "Has");
var direct = Callback<Func<IntPtr,IntPtr,byte>>(players, "Direct");
var get = Callback<Func<IntPtr,IntPtr,IntPtr>>(players, "Get");
var listGet = Callback<Func<IntPtr,IntPtr,IntPtr>>(players, "ListGet");
Register(teams, "liveEffects", 21, ConvertSpeedBonuses);
Register(teams, "liveEffects", 22, RiggedFaceoffs);
Set(teams, "dataMethod", (IntPtr)23);
Set(teams, "yipsMethod", (IntPtr)24);
Set(teams, "dataBenchMethod", (IntPtr)25);
Set(teams, "trainingMethod", (IntPtr)26);
Register(players, "attributes", 31, "Speed");
Register(players, "markers", 32, GoldenHelmet);
Register(players, "markers", 34, GoldenHelmet);
Register(players, "listAttributes", 33, "Speed");
Native(teams, "liveOriginal", "Has"); Native(teams, "dataOriginal", "Has");
Native(players, "cachedOriginal", "Has"); Native(players, "getOriginal", "Get"); Native(players, "listGetOriginal", "Get");
Native(players, "directOriginal", "Has");
var home = new Team(10) { _teamData = new TeamData(11) { relics = new() } };
var away = new Team(12) { _teamData = new TeamData(13) { relics = new() }, otherTeam = home };
home.otherTeam = away;
var player = new SkaterData(14) { powerups = new() };
var ordinary = new Talent { id = "ordinary-talent" };
player.powerups.Add(ordinary);
home._teamData.relics.Add(new Relic { id = "ordinary-relic" });
away._teamData.relics.Add(new Relic { id = "another-ordinary-relic" });
var failures = 0; var checks = 0;
void Check(bool value, string what) { checks++; if (!value) failures++; Console.WriteLine((value ? "PASS " : "FAIL ") + what); }
void Define(string id, string effect, string kind = CustomRewardDefinition.CustomTalents, string target = "Whole team", int amount = 7)
{
    CustomRewardRuntime.Definitions[id] = new CustomRewardDefinition { Id = id, Name = id, Effect = effect, Kind = kind, Target = target, Stat = "Speed", Amount = amount };
    CustomRewardRuntime.DefinitionQueryGeneration++;
}
for (int i = 0; i < 100; i++) { live(10, 21); savedTeam(11, 23); has(14, 31); listGet(player.powerups.Pointer, 33); }
Check(CustomRewardRuntime.AssetQueries == 0, "Repeated absent-effect queries never request full asset registration");
Check(home._teamData.relics.Reads + away._teamData.relics.Reads + player.powerups.Reads == 0, "An absent effect never reads a native loadout element");

Define("faceoff", RiggedFaceoffs, CustomRewardDefinition.CustomRelics, OpposingTeam);
Define("speed", LockAttribute); Define("speed-b", LockAttribute); Define("hat", GoldenHelmet);
live(10, 22); has(14, 31); has(14, 32); listGet(player.powerups.Pointer, 33);
int initialTeamReads = home._teamData.relics.Reads + away._teamData.relics.Reads, initialPlayerReads = player.powerups.Reads;
for (int i = 0; i < 1000; i++) { live(10, 22); has(14, 31); has(14, 32); listGet(player.powerups.Pointer, 33); }
Check(home._teamData.relics.Reads + away._teamData.relics.Reads == initialTeamReads, "Steady matching-catalog team misses avoid repeated loadout scans");
Check(player.powerups.Reads == initialPlayerReads, "Saved-data, marker, and inlined lock misses share one native list scan");
home._teamData.relics.Replace(0, new Relic { id = "faceoff" });
Check(live(10, 22) == 0 && live(12, 22) == 1, "Equal-size relic replacement immediately grants only to the opponent");
home._teamData.relics = new();
Check(live(12, 22) == 0, "Replacing the team's native list drops its old capabilities");
away._teamData.relics.Replace(0, new Relic { id = "faceoff" });
Check(live(10, 22) == 1, "Opposing side's own list and target determine the grant");
var fresh = new Team(16) { _teamData = new TeamData(17) { relics = new() } };
home.otherTeam = fresh;
Check(live(10, 22) == 0, "Switching opponent object cannot inherit the old opponent's relics");

var first = new Talent { id = "speed", PayoffSuppressed = true };
var second = new Talent { id = "speed-b" };
player.powerups.Replace(0, first); player.powerups.Add(second);
var secondValue = get(14, 31);
Check(secondValue != IntPtr.Zero && listGet(player.powerups.Pointer, 33) == secondValue, "First nonsuppressed lock wins identically for data and list callers");
first.PayoffSuppressed = false;
var firstValue = get(14, 31);
Check(firstValue != IntPtr.Zero && firstValue != secondValue, "Unsuppressing an owned talent immediately restores its native priority");
first.PayoffSuppressed = true; second.PayoffSuppressed = true;
Check(get(14, 31) == IntPtr.Zero && has(14, 31) == 0, "In-place payoff suppression immediately removes all lock capabilities");
player.powerups.Replace(0, new Talent { id = "hat" });
Check(has(14, 32) == 1, "Same-count talent replacement exposes a newly owned marker");
CustomRewardRuntime.InMatch = false;
Check(has(14, 32) == 1 && live(12, 22) == 0, "Saved-data previews resolve outside a match while live-only rules stay gated");
player.powerups = new(); player.powerups.Add(new Talent { id = "late" });
Check(has(14, 32) == 0, "A replacement preview loadout has independent identity");
Define("late", GoldenHelmet);
Check(has(14, 32) == 1, "Publishing an embedded definition refreshes an unchanged owned list");

CustomRewardRuntime.InMatch = true; home.otherTeam = away;
Define("fall", BreakawayShotFall, CustomRewardDefinition.CustomRelics);
Define("weak-shot", BreakawayShotPenalty, CustomRewardDefinition.CustomRelics);
home._teamData.relics.Add(new Rogue.Relics.ChaosRelics.YipsRelic { id = "fall" });
Originals.Answer = 1;
Check(live(10, 24) == 0, "A fall-only carrier suppresses the unrelated native shot penalty");
home._teamData.relics.Add(new Rogue.Relics.ChaosRelics.YipsRelic { id = "real-yips" });
Check(live(10, 24) == 1, "A genuine Yips still supplies its native shot penalty");
Define("slot", BenchTalentSlot, CustomRewardDefinition.CustomRelics);
Define("capacity", BenchCapacity, CustomRewardDefinition.CustomRelics);
home._teamData.relics = new(); home._teamData.relics.Add(new RandomTalentBenchBonus { id = "capacity" });
Check(savedTeam(11, 25) == 0, "A capacity-only cushion cannot grant the separate talent reward");
home._teamData.relics.Add(new RandomTalentBenchBonus { id = "real-cushion" });
Check(savedTeam(11, 25) == 1, "A genuine cushion still grants the native bench reward");
Originals.Answer = 0;
home._teamData.relics = new(); home._teamData.relics.Add(new Relic { id = "slot" });
Check(savedTeam(11, 25) == 1, "A separately owned bench reward grants without a genuine cushion");
CustomRewardTrainingGrowth.Current = new(); CustomRewardTrainingGrowth.Preview = true;
savedTeam(11, 26);
Check(!CustomRewardTrainingGrowth.Current.Used, "A training preview never consumes the saved-team choice");
CustomRewardTrainingGrowth.Preview = false;
savedTeam(11, 26);
Check(CustomRewardTrainingGrowth.Current.Used, "The real training choice still observes its native query");
// The real HaveTalentCached calls the direct query and stores its answer on a miss.
// A managed marker must be resolved after the original returns, on every outer query.
var customMarker = new Talent { id = "hat" };
player.powerups = new(); player.powerups.Add(customMarker);
Originals.Direct = direct;
Native(players, "cachedOriginal", "CachedHas");
Check(has(14, 32) == 1, "An eligible marker grants on a cold native cache");
customMarker.PayoffSuppressed = true;
Check(has(14, 32) == 0 && Originals.Cached == 0, "Native cache-fill cannot retain a custom marker after payoff suppression");
// Probe the indexed catalog through the production public-to-runtime methods
// so a pre-index build can still run the same regression harness.
var index = typeof(CustomRewardRuntime);
var effectQuery = index.GetMethod("DefinitionsForEffect", BindingFlags.NonPublic | BindingFlags.Static);
var triggerQuery = index.GetMethod("DefinitionsForTrigger", BindingFlags.NonPublic | BindingFlags.Static);
var pairQuery = index.GetMethod("DefinitionsForEffects", BindingFlags.NonPublic | BindingFlags.Static);
Check(effectQuery != null && triggerQuery != null && pairQuery != null, "Effect and trigger queries have ordered, reusable catalog groups");
if (effectQuery != null && triggerQuery != null && pairQuery != null) {
    var effects = effectQuery.CreateDelegate<Func<string,CustomRewardDefinition[]>>();
    var triggers = triggerQuery.CreateDelegate<Func<string,CustomRewardDefinition[]>>();
    var both = pairQuery.CreateDelegate<Func<string,string,CustomRewardDefinition[]>>();
    void NewRule(string id, string effect, string trigger, bool retired = false) {
        CustomRewardRuntime.Definitions[id] = new() { Id = id, Effect = effect, Trigger = trigger, NeedsRedesign = retired };
        CustomRewardRuntime.DefinitionQueryGeneration++;
    }
    NewRule("goal-1", "a", "g"); NewRule("irrelevant", "c", "g"); NewRule("goal-2", "b", "g");
    NewRule("retired", "b", "g", true); NewRule("goal-3", "a", "s");
    var pair = both("a", "b"); var singles = effects("a"); var events = triggers("g");
    Check(pair.Select(d => d.Id).SequenceEqual(new[] { "goal-1", "goal-2", "goal-3" })
        && singles.Select(d => d.Id).SequenceEqual(new[] { "goal-1", "goal-3" })
        && events.Select(d => d.Id).SequenceEqual(new[] { "goal-1", "irrelevant", "goal-2" }),
        "Single, paired, and trigger groups preserve authored order and exclude retired definitions");
    Check(ReferenceEquals(pair, both("a", "b")) && ReferenceEquals(singles, effects("a")) && ReferenceEquals(events, triggers("g"))
        && effects("absent").Length == 0, "Repeated catalog queries reuse their generation's arrays, including absent effects");
    NewRule("goal-1", "b", "s"); NewRule("goal-4", "a", "g");
    Check(effects("a").Select(d => d.Id).SequenceEqual(new[] { "goal-3", "goal-4" })
        && triggers("g").Select(d => d.Id).SequenceEqual(new[] { "irrelevant", "goal-2", "goal-4" })
        && !ReferenceEquals(pair, both("a", "b")), "Publishing or replacing definitions invalidates every related group together");
}
Console.WriteLine($"{checks} checks, {failures} failures; initial warm-up read {initialTeamReads} team and {initialPlayerReads} player elements.");
return failures == 0 ? 0 : 1;

public static class Originals
{
    public static byte Answer;
    public static byte? Cached;
    public static Func<IntPtr,IntPtr,byte> Direct;
    public static byte CachedHas(IntPtr owner, IntPtr method) => Cached ??= Direct(owner, 34);
    public static byte Has(IntPtr owner, IntPtr method) => Answer;
    public static IntPtr Get(IntPtr owner, IntPtr method) => IntPtr.Zero;
}
