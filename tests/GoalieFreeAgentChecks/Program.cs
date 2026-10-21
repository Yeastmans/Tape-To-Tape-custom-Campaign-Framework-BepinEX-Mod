using CampaignCore;

int checks = 0;
void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; Console.WriteLine("PASS " + message); }
void Reject(Action action, string message) { bool rejected = false; try { action(); } catch (Exception e) when (e is ArgumentException or IOException) { rejected = true; } Check(rejected, message); }
var root = Path.GetFullPath(Path.Combine("artifacts", "four-features", "goalie-free-agent-checks", Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(root);
var gm = new MapLayoutNode { Id = "gm", Type = "GeneralManager", Outgoing = ["boss"] };
var map = new MapLayout { BaseSquad = "The Maple Leafs", Nodes = [gm, new MapLayoutNode { Id = "boss", Type = "Boss", Layer = 1 }] };
Check(gm.GmRole == GoalieFreeAgents.Skaters, "new recruitment nodes default to skaters");
Check(!map.Serialize().Contains("Recruit role"), "default keeps old map format clean");
Check(MapLayout.Parse(map.Serialize()).Nodes[0].GmRole == GoalieFreeAgents.Skaters, "old maps retain skater recruitment");
gm.GmRole = GoalieFreeAgents.Goalies;
string saved = map.Serialize();
Check(MapLayout.Parse(saved).Nodes[0].GmRole == GoalieFreeAgents.Goalies, "goalie role round trips");
Check(new CampaignDocument(saved).Get("Recruit role gm") == "Goalies", "role metadata is attached to stable node identity");
Check(new CampaignDocument(saved).Get("Node gm").Split('|').Length == 13, "existing node row stays compatible");
Check(map.Validate([]).Count == 0, "goalie recruitment map structure validates");
Check(GoalieFeatureOptions.ValidateNodes(false, [map]).Any(e => e.Contains("Layer 1, node 1") && e.Contains("enable", StringComparison.OrdinalIgnoreCase)), "disabled recruitment names incompatible goalie node");
Check(GoalieFeatureOptions.ValidateNodes(true, [map]).Count == 0, "enabled recruitment accepts goalie node");
Check(GoalieFeatureOptions.ValidateNodes(false, [map], 2).Single().StartsWith("Map 2, Layer 1, node 1"), "map-specific validation keeps its actual map number");
foreach (bool recruitment in new[] { false, true })
foreach (bool reserve in new[] { false, true })
{
    var campaign = new CampaignDocument(""); var squad = new CampaignDocument("");
    if (recruitment) campaign.Set(GoalieFeatureOptions.RecruitmentKey, "On");
    if (reserve) squad.Set(GoalieFeatureOptions.ReserveKey, "On");
    Check(GoalieFeatureOptions.Recruitment(campaign) == recruitment && GoalieFeatureOptions.Reserve(squad) == reserve,
        $"campaign recruitment {recruitment} and squad reserve {reserve} remain independent");
}
Check(!GoalieFeatureOptions.Recruitment(new CampaignDocument("")) && !GoalieFeatureOptions.Reserve(new CampaignDocument("")), "missing feature settings default off");
foreach (string enabled in new[] { "on", "yes", "true", "1" })
    Check(GoalieFeatureOptions.Recruitment(new CampaignDocument("Goalie Recruitment = " + enabled))
        && GoalieFeatureOptions.Reserve(new CampaignDocument("Squad Behavior reserve_goalie = " + enabled))
        && SquadBehaviors.Enabled("Basic Squad", GoalieFeatureOptions.ReserveBehaviorId,
            key => new CampaignDocument("Squad Behavior reserve_goalie = " + enabled).Get(key)), "legacy enabled spelling " + enabled);
Check(!GoalieFeatureOptions.Recruitment(new CampaignDocument("Goalie Recruitment = maybe"))
    && !GoalieFeatureOptions.Reserve(new CampaignDocument("Squad Behavior reserve_goalie = maybe")), "invalid values default off");
Check(!GoalieFeatureOptions.Reserve(new CampaignDocument("")) && !SquadBehaviors.All.Single(b => b.Id == "reserve_goalie").Templates.Any(), "reserve has no native template defaults");
gm.GmRole = "Any";
Check(map.Validate([]).Any(e => e.Contains("recruitment role")), "unknown recruitment role rejected");
gm.GmRole = GoalieFreeAgents.Goalies;
Check(GoalieFreeAgents.SelectedFiles(root).Length == 0, "unconfigured goalie pool is empty");
string directory = GoalieFreeAgents.DirectoryPath(root);
Directory.CreateDirectory(directory);
string a = Path.Combine(directory, "alpha.txt"), b = Path.Combine(directory, "beta.txt");
File.WriteAllText(a, "Name = Alpha\nPosition = Goalie\nSkill = 72\n");
File.WriteAllText(b, "Name = Beta\nPosition = Goalie\nSkill = 61\n");
Check(GoalieFreeAgents.SelectedFiles(root).Length == 2, "legacy authored pool includes its goalies");
SelectedPlayerPool.Save(directory, ["beta.txt"]);
Check(GoalieFreeAgents.SelectedFiles(root).SequenceEqual([b]), "explicit pool respects excluded goalie");
SelectedPlayerPool.Save(directory, []);
Check(GoalieFreeAgents.SelectedFiles(root).Length == 0, "explicit empty pool has no native fallback");
SelectedPlayerPool.Save(directory, ["alpha.txt"]);
string original = File.ReadAllText(a);
GoalieFreeAgents.SelectedFiles(root);
Check(File.ReadAllText(a) == original, "pool resolution does not mutate goalie");
File.WriteAllText(a, "Name = Wrong role\nPosition = Center\n");
Reject(() => GoalieFreeAgents.SelectedFiles(root), "selected skater rejected from goalie pool");
File.WriteAllText(Path.Combine(directory, SelectedPlayerPool.Manifest), "../outside.txt\n");
Reject(() => GoalieFreeAgents.SelectedFiles(root), "pool path escape rejected");
File.WriteAllText(Path.Combine(directory, SelectedPlayerPool.Manifest), "missing.txt\n");
Reject(() => GoalieFreeAgents.SelectedFiles(root), "missing selected goalie rejected");
var snapshot = new CampaignDocument("Number Style = Rounded\nPass Read = 80\nAway Primary = 2, 4, 6\n");
string snapshotSource = snapshot.ToString();
GoalieRecruitSnapshots.Save(root, "run-one", "goalie-one", snapshot);
Check(snapshot.ToString() == snapshotSource, "snapshot does not mutate author document");
Check(GoalieRecruitSnapshots.Read(root, "run-one", "goalie-one").Get("Pass Read") == "80", "supplemental goalie fields round trip");
Check(GoalieRecruitSnapshots.Read(root, "run-two", "goalie-one") == null, "snapshot cannot leak across runs");
Check(GoalieRecruitSnapshots.Read(root, "run-one", "goalie-two") == null, "snapshot cannot leak to another recruit");
Check(GoalieRecruitSnapshots.Read(Path.Combine(root, "other-campaign"), "run-one", "goalie-one") == null, "snapshot cannot leak across campaigns");
File.WriteAllText(b, "Name = Edited Beta\nPosition = Goalie\n");
Check(GoalieRecruitSnapshots.Read(root, "run-one", "goalie-one").Get("Away Primary") == "2, 4, 6", "pool edits cannot change signed snapshot");
snapshot.Set("Pass Read", "90");
GoalieRecruitSnapshots.Save(root, "run-one", "goalie-one", snapshot);
Check(GoalieRecruitSnapshots.Read(root, "run-one", "goalie-one").Get("Pass Read") == "90", "atomic snapshot replacement updates exact identity");
Reject(() => GoalieRecruitSnapshots.Save(root, "", "goalie-one", snapshot), "empty run identity rejected");
Reject(() => GoalieRecruitSnapshots.Save(root, "run-one", "", snapshot), "empty recruit identity rejected");
Check(!Directory.EnumerateFiles(root, "*.tmp", SearchOption.AllDirectories).Any(), "no temporary snapshot left behind");
Console.WriteLine($"PASS {checks} goalie recruitment data assertions; fixtures {root}");
