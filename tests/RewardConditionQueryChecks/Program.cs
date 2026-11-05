using System.Reflection;
using CampaignCore;
using Data;
using EndlessMode;
using Rogue;
using Skaters;
using E = CampaignCore.CustomRewardEffects;

int checks = 0, failures = 0;
void Check(bool pass, string name) {
    Console.WriteLine($"{(pass ? "PASS" : "FAIL")} {name}");
    checks++;
    if (!pass) failures++;
}
void Measure(string name) => Console.WriteLine($"WORK {name}: owner power checks={Work.PowerChecks}, native team reads={Work.TeamReads}, live condition checks={Work.Conditions}");
CustomRewardDefinition Rule(string id, string effect, int amount = 1, string trigger = "While owned", string target = E.Team, string stat = "Speed")
    => new() { Id = id, Effect = effect, Amount = amount, Trigger = trigger, Target = target, Stat = stat };
Talent Grant(Skater owner, CustomRewardDefinition rule) {
    var talent = new Talent { id = rule.Id };
    owner._skaterData.powerups.Add(talent);
    return talent;
}
var home = new Team { _teamData = new TeamData { id = "home" } };
var away = new Team { _teamData = new TeamData { id = "away" }, otherTeam = home };
home.otherTeam = away;
var players = Enumerable.Range(0, 12).Select(i => new Forward {
    Side = i < 6 ? home : away, _skaterData = new SkaterData { id = $"player{i}" }
}).ToArray();
var recipient = players[3];
var unrelated = Enumerable.Range(0, 32).Select(i => Rule($"other{i}", E.StatChange)).ToList();
foreach (var rule in unrelated) Grant(players[0], rule);
CustomRewardRuntime.Setup(players, unrelated);
var stats = new ForwardStats { _forward = recipient };
var averagePrefix = typeof(CustomRewardAverageAttributes).GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic);
var average = averagePrefix.CreateDelegate<Average>();
var restoreAverage = typeof(CustomRewardAverageAttributes).GetMethod("Finalizer", BindingFlags.Static | BindingFlags.NonPublic).CreateDelegate<Action<ForwardStats, bool>>();
var modifiers = new Il2CppSystem.Collections.Generic.List<Il2CppSystem.Func<int, int>>();
CustomRewardScaling.Bind(recipient, "Checking", modifiers);
for (int i = 0; i < 64; i++) {
    average(stats, out var balance);
    restoreAverage(stats, balance);
    CustomRewardScaling.Scale(10, 15, modifiers);
    CustomRewardRandomShots.Stat = "Power";
    CustomRewardRandomShots.Rule(recipient);
}
Measure("192 hot getter queries with 32 unrelated owned team auras, 12 skaters");
Check(Work.PowerChecks == 0 && Work.Conditions == 0 && Work.TeamReads == 0, "unrelated rules touch no native owners, recipient teams or conditions");
Console.WriteLine($"WORK native stat getter scaffolding: reads={Work.StatReads}, writes={Work.StatWrites}, referee queries={Work.RefereeReads}");
Check(Work.StatReads == 0 && Work.StatWrites == 0 && Work.RefereeReads == 0, "absent averaging and scaling bypass native getter state entirely");
var balanced = Rule("balanced", E.BalanceAttributes);
Grant(recipient, balanced);
CustomRewardRuntime.Setup(players, unrelated.Append(balanced));
average(stats, out var outerBalance);
average(stats, out var innerBalance);
restoreAverage(stats, innerBalance);
Check(stats._isBalanced, "nested getter preserves the outer custom balance override");
restoreAverage(stats, outerBalance);
Check(!stats._isBalanced, "outer finalizer restores the native flag after the override");
stats._isBalanced = true;
average(stats, out var nativeBalance);
restoreAverage(stats, nativeBalance);
Check(stats._isBalanced, "a native balanced skater stays balanced");

var speed = Rule("aura", E.BonusMultiplier, 200, "While holding puck");
var defense = Rule("zone", E.BonusMultiplier, 150, "While in defensive zone");
var checking = Rule("checking", E.BonusMultiplier, 400, stat: "Checking");
var newRule = Rule("new", E.BonusMultiplier, 500);
var rolledPower = Rule("power", E.RandomShotPower, 7);
var tiedAccuracy = Rule("accuracy", E.RandomShotAccuracy, 7);
var excluded = Rule("excluded", E.BonusMultiplier, 1000);
excluded.NeedsRedesign = true;
var eventOnly = Rule("event", E.BonusMultiplier, 1000, "Hat trick");
var owner = players[1];
var carrier = Grant(owner, speed);
Grant(players[2], speed);
Grant(owner, defense);
Grant(owner, checking);
Grant(owner, rolledPower);
Grant(owner, tiedAccuracy);
Grant(owner, excluded);
Grant(owner, eventOnly);
var all = unrelated.Concat([speed, defense, checking, newRule, rolledPower, tiedAccuracy, excluded, eventOnly]).ToList();
CustomRewardRuntime.Setup(players, all);
double Speed() => CustomRewardScaling.Multiplier(recipient, E.BonusMultiplier, "Speed");
Puck.Instance.skaterPossession = owner;
owner.DefensiveZone = true;
Check(Speed() == 3, "two different eligible rules multiply and the requested attribute excludes Checking");
Measure("matching Speed query including two possible puck owners and one zone owner");
Check(Work.PowerChecks <= 3 && Work.Conditions == 3, "only three matching candidate owners are visited");
Puck.Instance.skaterPossession = players[2]; owner.DefensiveZone = false;
Check(Speed() == 2, "puck and zone changes between consecutive queries take effect immediately");
players[2].Benched = true;
Check(Speed() == 1, "benched talent owner stops lending immediately");
players[2].Benched = false; Puck.Instance.skaterPossession = owner; carrier.PayoffSuppressed = true;
Check(Speed() == 1, "live payoff suppression disables an indexed talent");
carrier.PayoffSuppressed = false; owner.PowersEnabled = false;
Check(Speed() == 1, "power disablement is live");
owner.PowersEnabled = true; Puck.Instance.skaterPossession = null;
players[0].DefensiveZone = true; owner.DefensiveZone = true;
var acquisition = typeof(CustomRewardAcquisitionOrder).GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic);
Grant(players[0], defense); Grant(players[0], defense); // duplicate carriers on one owner still contribute once
Grant(players[0], newRule);
acquisition.Invoke(null, [players[0]]);
Check(Speed() == 11.25, "acquisition adds another owner and a newly in-play rule with one contribution per owner");
players[0]._skaterData.powerups.RemoveAll(t => t.id == defense.Id);
Check(Speed() == 7.5, "a removed talent stops lending even before another roster rebuild");

CustomRewardRandomShots.Stat = "Power"; Work.Reset();
Check(CustomRewardRandomShots.Rule(recipient) == rolledPower && Work.Conditions == 1, "scoped random power visits only power");
CustomRewardRandomShots.Stat = "Accuracy"; Work.Reset();
Check(CustomRewardRandomShots.Rule(recipient) == tiedAccuracy && Work.Conditions == 1, "scoped random accuracy visits only accuracy");
CustomRewardRandomShots.Stat = null; Work.Reset();
Check(CustomRewardRandomShots.Rule(recipient) == rolledPower && Work.Conditions == 2, "unscoped tied random effects preserve authored order");
tiedAccuracy.Amount = 12; CustomRewardRandomShots.Stat = "Power"; Work.Reset();
var gambler = (Gambler)CustomRewardRandomShots.Controller(recipient).Talent;
Check(gambler.minShotPower == 7 && gambler.minAccuracy == 12 && Work.Conditions == 2, "controller initializes both authored random attributes in a power scope");
CustomRewardRandomShots.Stat = "unknown"; Work.Reset();
Check(CustomRewardRandomShots.Rule(recipient) == null && Work.Conditions == 0, "unknown stat scope does no rule work");
var last = players[^1];
last._skaterData.powerups.Add(new Talent { id = "legacy-unlisted" });
away._teamData.relics.Add(new Relic { id = "old-relic" });
CustomRewardRuntime.Setup(players, all);
string key = CustomRewardProgress.ScopedRecipient("player:player11", "team:away");
Check(CustomRewardRuntime.StillOwned("legacy-unlisted", key), "saved effects recognize still-equipped legacy IDs after catalog changes");
Measure("saved opposing stat effect on the last roster member");
Check(Work.TeamReads <= 1, "saved player bonus checks only the owning skater");
last._skaterData.powerups[^1].PayoffSuppressed = true;
Check(!CustomRewardRuntime.StillOwned("legacy-unlisted", key), "saved opposing bonus immediately observes suppression");
Work.Reset();
Check(CustomRewardRuntime.StillOwned("old-relic", "team:away"), "saved legacy relic ownership is found on the opposing team");
Check(Work.TeamReads <= 1, "saved relic bonus checks only owning teams");
away._teamData.relics.Clear();
Check(!CustomRewardRuntime.StillOwned("old-relic", "team:away"), "removed relic stops supplying a saved bonus immediately");
Work.Reset();
for (int i = 0; i < 32; i++) {
    CustomRewardRuntime.Signal("Pass sent", recipient);
    CustomRewardRuntime.SignalOpponent("Opponent pass", recipient);
    CustomRewardRuntime.SignalAll("Period started");
}
Measure("96 unwatched player, opposing-team and match signals");
Check(Work.TeamReads == 0 && Work.PowerChecks == 0 && Work.Observations == 0, "unwatched signals skip native teams, bindings and ownership");
var first = Rule("first-pass", E.StatChange, 2, "Pass sent", E.Actor);
first.ActivationsPerMatch = 10;
var reset = Rule("reset-only", E.StatChange, 3, "Shot", E.Actor);
reset.ResetOn = "Pass sent";
var both = Rule("pass-and-reset", E.StatChange, 5, "Pass sent", E.Actor);
both.ResetOn = "Pass sent"; both.ActivationsPerMatch = 10;
Grant(owner, first); Grant(owner, reset); Grant(owner, both);
CustomRewardRuntime.Setup(players, all.Concat([first, reset, both]));
CustomRewardRuntime.Signal("Shot", owner);
Check(CustomRewardRuntime.progress.Effects.Any(e => e.Reward == reset.Id), "shot creates the event bonus to be reset");
CustomRewardRuntime.activations.Clear();
CustomRewardRuntime.Signal("Pass sent", owner);
CustomRewardRuntime.Signal("Pass sent", owner);
Check(CustomRewardRuntime.activations.SequenceEqual(new[] {first.Id, both.Id, first.Id, both.Id}), "event candidates preserve definition order and simultaneous trigger/reset dispatches once");
Check(CustomRewardRuntime.progress.Effects.All(e => e.Reward != reset.Id)
    && CustomRewardRuntime.progress.Effects.Single(e => e.Reward == both.Id).Stacks == 1, "reset-only and same-event reset run before the new activation");
Work.Reset();
for (int i = 0; i < 8; i++) { CustomRewardRuntime.Bind(owner); _ = Speed(); }
Check(Work.Observations == 0, "rebinding an unchanged skater does not rebuild or observe its roster");
var change = Rule("replacement", E.BonusMultiplier, 600);
CustomRewardRuntime.Setup(players, all.Append(change));
owner._skaterData.powerups[0] = new Talent { id = change.Id };
CustomRewardRuntime.Bind(owner);
Check(Speed() == 45, "same-length in-place inventory replacement is detected on bind");
CustomRewardRuntime.InMatch = false; Work.Reset();
Check(Speed() == 1 && Work.PowerChecks == 0 && Work.TeamReads == 0, "out-of-match hooks stay native without reading players");
Console.WriteLine($"{checks} continuous reward query checks; {failures} failures.");
return failures == 0 ? 0 : 1;

delegate void Average(ForwardStats stats, out bool previous);
