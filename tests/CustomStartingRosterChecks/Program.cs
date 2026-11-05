using EndlessMode;
using Data;
using State;
int count = 0;
void Check(bool ok, string text) { if (!ok) throw new Exception(text); count++; Console.WriteLine("PASS " + text); }
ForwardData F(string name, string id) => new() { firstName = name, id = id };
var cfg = new TeamConfig { LD = new() { Name = "Starter" }, Goalie = new() { Name = "Goalie" } };
Plugin.PlayerTeamConfigs["test"] = cfg;
var run = new TeamData { goalie = new() { id = "g", firstName = "Goalie" } };
for (int i = 0; i < 5; i++) run.forwards.Add(i == 3 ? F("Starter", "starter") : null);
var squad = new RunSquadScriptableObject { id = "Custom_test", startingTeam = run };
var state = CustomStartingRoster.Begin(squad);
try
{
    Check(!CustomStartingRoster.AllowSet(run, 3, F("Native", "native")), "Native creation cannot overwrite authored starter");
    Check(CustomStartingRoster.AllowSet(run, 3, F("Starter", "starter")), "Native deep copy preserves authored ID");
    Check(!CustomStartingRoster.AllowSet(run, 3, null), "Native creation cannot clear authored starter");
    Check(!CustomStartingRoster.AllowSet(run, 0, F("McShaggy", "native")), "Native creation cannot fill an unselected slot");
    CustomStartingRoster.BeginLoad();
    try
    {
        Check(CustomStartingRoster.AllowSet(run, 3, F("Saved player", "saved")), "Save load honors changed player ID and name");
        Check(CustomStartingRoster.AllowSet(run, 3, null), "Saved empty positions are authoritative");
        CustomStartingRoster.BeginLoad();
        CustomStartingRoster.EndLoad();
        Check(CustomStartingRoster.AllowSet(run, 3, F("Nested save", "nested")), "Nested load retains outer scope");
    }
    finally { CustomStartingRoster.EndLoad(); }
    Check(!CustomStartingRoster.AllowSet(run, 3, null), "Load finalizer restores creation scope");
}
finally { CustomStartingRoster.End(state); }
Check(CustomStartingRoster.AllowSet(run, 3, F("Selected", "selected")), "Edit Lineup can change a starter before match one");
Check(CustomStartingRoster.AllowSet(run, 3, null), "Normal roster changes are not held by stale run-pointer guard");
run.forwards.Add(F("Selected bench starter", "chosen-bench"));
state = CustomStartingRoster.Begin(squad);
try
{
    var target = new TeamData();
    Check(CustomStartingRoster.AllowSet(target, 3, F("Starter", "starter")), "Selected starter can be cloned into an initially empty target");
    Check(CustomStartingRoster.AllowSet(target, 5, F("Selected bench starter", "chosen-bench")), "Explicitly benched starter survives native cloning");
    Check(!CustomStartingRoster.AllowSet(target, 5, F("Unselected bench filler", "filler")), "Benched starter cannot be replaced by an unselected player");
}
finally { CustomStartingRoster.End(state); }
run.forwards.RemoveAt(5);
var constructed = new TeamData(); constructed.forwards.Add(F("McShaggy", "native")); constructed.forwards.Add(F("Starter clone", "starter"));
CustomStartingRoster.RemoveUnselectedConstructedPlayers(constructed, squad);
Check(constructed.forwards[0] == null && constructed.forwards[1]?.id == "starter", "Post-construction cleanup removes native direct-list fillers while retaining selected clones");
var starter = run.forwards[3]; var goalie = run.goalie;
CustomStartingRoster.LockAuthoredSkaters(run, cfg);
Check(run.leftDefensemenLineUpCreationLocked && !run.centerLineUpCreationLocked, "Native creation locks cover authored slots only");
Check(ReferenceEquals(starter, run.forwards[3]) && ReferenceEquals(goalie, run.goalie) && run.forwards.Count == 5, "No players are replaced, appended or moved");
var authoredNames = new[] { "Left Wing", "Right Wing", "Center", "Left Defense", "Right Defense" };
var selectedRoster = new[] { "Left Wing", "Right Wing", "Center", "Left Defense", "Superstars 01", "Right Defense", null, null, null, null };
var matched = AuthoredRosterMatcher.Resolve(authoredNames, selectedRoster, liveRun: true);
Check(matched.SequenceEqual(new[] { 0, 1, 2, 3, 5 }), "Authored right defense follows the selected superstar instead of overwriting it");
Check(!matched.Contains(4), "Chosen superstar is never an authored target");
Check(AuthoredRosterMatcher.Resolve(authoredNames, selectedRoster, liveRun: false).SequenceEqual(new[] { 0, 1, 2, 3, 4 }), "Starting-team preview still uses authored positions");
var ambiguous = AuthoredRosterMatcher.Resolve(new[] { "Right Defense" }, new[] { "Right Defense", "Right Defense" }, liveRun: true);
Check(ambiguous[0] == -1, "Ambiguous player names cannot overwrite a chosen skater");
var changed = AuthoredRosterMatcher.Resolve(new[] { "Right Defense" }, new[] { "Selected replacement" }, liveRun: true);
Check(changed[0] == -1, "A replacement chosen in Edit Lineup is left intact");
run.forwards[0] = F("McShaggy", "native");
run.forwards.Add(F("Donor reserve", "reserve"));
run.lines.Add(new Lineup { leftWinger = "native", leftDefensemen = "starter" });
CustomStartingRoster.RemoveTemplateStarters(run, cfg);
Check(run.forwards[0] == null && run.forwards[5] == null && ReferenceEquals(starter, run.forwards[3]), "Donor starters and reserves removed without moving the selected starter");
Check(run.lines[0].leftWinger == "" && run.lines[0].leftDefensemen == "starter", "Line references retain only selected players");
CustomStartingRoster.RemoveTemplateStarters(run, new TeamConfig());
Check(run.forwards.All(p => p == null) && run.goalie == null, "Completely empty authored squad keeps neither skaters nor a donor goalie");
cfg.C = new() { Name = "McShaggy" }; run.forwards[2] = F("McShaggy", "selected-mcshaggy");
state = CustomStartingRoster.Begin(squad);
try { Check(CustomStartingRoster.AllowSet(new TeamData(), 2, F("McShaggy", "selected-mcshaggy")), "Explicitly selected McShaggy remains allowed"); }
finally { CustomStartingRoster.End(state); }
var admission = typeof(CustomStartingRoster).GetMethod("CanStartWithoutSuperstars", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
bool Allows(TeamConfig value) => admission != null && (bool)admission.Invoke(null, new object[] { value });
Check(Allows(new TeamConfig()), "No Effects can register an entirely empty recruit-first roster");
Check(Allows(new TeamConfig { Goalie = new() { Name = "Authored goalie" } }), "A goalie does not require automatic skater starters");
Check(!Allows(new TeamConfig { C = new() { Name = "One starter" } }), "A partial first line still requires its missing skaters");
Check(!Allows(new TeamConfig { LW = new() { Name = "Left wing" }, RW = new() { Name = "Right wing" }, C = new() { Name = "Center" }, LD = new() { Name = "Left defense" } }), "A four-skater No Effects first line remains incomplete");
Check(!Allows(new TeamConfig { L2_C = new() { Name = "Second line only" } }), "A nonempty second line is not mistaken for an empty recruit-first roster");
Check(Allows(new TeamConfig { LW = new() { Name = "Left wing" }, RW = new() { Name = "Right wing" }, C = new() { Name = "Center" }, LD = new() { Name = "Left defense" }, RD = new() { Name = "Right defense" } }), "Complete authored first line remains allowed");
var emptyStart = new TeamData { goalie = new() { id = "donor-goalie" } };
for (int i = 0; i < 10; i++) emptyStart.forwards.Add(F("Donor " + i, "donor-" + i));
emptyStart.lines.Add(new Lineup { leftWinger = "donor-0", rightWinger = "donor-1", center = "donor-2", leftDefensemen = "donor-3", rightDefensemen = "donor-4" });
emptyStart.lines.Add(new Lineup { leftWinger = "donor-5", rightWinger = "donor-6", center = "donor-7", leftDefensemen = "donor-8", rightDefensemen = "donor-9" });
var emptyConfig = new TeamConfig();
Check(Allows(emptyConfig), "Empty two-line campaign passes admission before donor cleanup");
CustomStartingRoster.RemoveTemplateStarters(emptyStart, emptyConfig);
Check(emptyStart.forwards.Count == 10 && emptyStart.forwards.All(p => p == null) && emptyStart.goalie == null, "Empty two-line startup keeps ten vacancies and no default players");
Check(emptyStart.lines.All(l => new[] { l.leftWinger, l.rightWinger, l.center, l.leftDefensemen, l.rightDefensemen }.All(string.IsNullOrEmpty)), "Empty two-line startup leaves no donor lineup references");
Console.WriteLine($"{count} checks passed");
