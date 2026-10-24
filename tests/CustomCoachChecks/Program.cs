using CampaignCore;
using System.Text;

int checks = 0;
void Check(bool ok, string reason) { checks++; if (!ok) throw new Exception(reason); }
var map = new MapLayout { BaseSquad = "Basic Squad", Nodes = [
    new() { Id = "coach", Type = "Coach", Outgoing = ["boss"] },
    new() { Id = "boss", Type = "Boss", Layer = 1, X = 9.5 }
] };
string legacy = map.Serialize();
Check(MapLayout.Parse(legacy).Serialize() == legacy, "Legacy maps must roundtrip unchanged");
string definition = "Format = 1\nCoach ID = 87d52c13-638a-4391-a5e3-659851876d41\nName = Defense | Coach = A\nRelics = relic-a\nImage PNG = \n";
var document = new CampaignDocument(legacy);
document.Set("Custom coach coach", Convert.ToBase64String(Encoding.UTF8.GetBytes(definition)));
var restored = new CampaignDocument(MapLayout.Parse(document.ToString()).Serialize());
Check(restored.Get("Custom coach coach").Length > 0, "Custom Coach snapshot must survive a map save/reopen");
Check(Encoding.UTF8.GetString(Convert.FromBase64String(restored.Get("Custom coach coach"))).Contains("Defense | Coach = A"), "Custom name must retain delimiters");
void Reject(Action action, string reason) { bool failed = false; try { action(); } catch (ArgumentException) { failed = true; } Check(failed, reason); }
var coach = MapLayout.Parse(document.ToString()).Nodes[0].CustomCoach;
Check(coach.Name == "Defense | Coach = A" && coach.Relics.SetEquals(["relic-a"]), "Definition retains exact allowed IDs and name");
var copy = coach.Copy(); copy.Name = "Different"; copy.Relics.Add("relic-b");
Check(coach.Name == "Defense | Coach = A" && coach.Relics.SetEquals(["relic-a"]), "Placed copies must be independent");
var choices = CustomCoachPool.Eligible(coach, new[] { "relic-a", "relic-b", "relic-a", "relic-a-plus" }, id => id, _ => true);
Check(choices.SequenceEqual(["relic-a"]), "One-relic pool cannot fill cards with outsiders, duplicates or unchecked upgrades");
Check(CustomCoachPool.Eligible(coach, choices, id => id, _ => false).Count == 0, "Owned/ineligible selections exhaust rather than restore outsiders");
Reject(() => CustomCoachPool.ValidateAvailable(coach, ["relic-b"]), "Unavailable selected IDs must prevent saving");
Check(coach.Relics.Contains("relic-a"), "Availability validation never erases authored selections");
CustomCoachPool.ValidateAvailable(coach, ["RELIC-A"]);
var empty = coach.Copy(); empty.Relics.Clear(); Reject(empty.Validate, "Empty authored pool must be rejected");
var blank = coach.Copy(); blank.Name = "   "; Reject(blank.Validate, "Blank names rejected");
var longName = coach.Copy(); longName.Name = new string('a', 81); Reject(longName.Validate, "Overlong names rejected");
var control = coach.Copy(); control.Name = "bad\nname"; Reject(control.Validate, "Multiline names rejected");
var exactly80 = coach.Copy(); exactly80.Name = new string('a', 80); Check(exactly80.Copy().Name.Length == 80, "80-character names supported");
var padded = coach.Copy(); padded.Name = "  Trim me  "; Check(padded.Copy().Name == "Trim me", "Saved names are trimmed");
var invalidId = coach.Copy(); invalidId.Id = "../../elsewhere"; Reject(invalidId.Validate, "Library identity cannot escape its folder");
Reject(() => CustomCoachDefinition.Parse(definition.Replace("Format = 1", "Format = 9")), "Unsupported formats rejected");
Reject(() => CustomCoachDefinition.Parse(definition + "Behavior = GeneralManager\n"), "Only Coach behavior supported");
const string png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aNn8AAAAASUVORK5CYII=";
coach.ImagePng = png; Check(coach.Copy().ImagePng == png, "Map art survives without a source file");
var badPng = coach.Copy(); badPng.ImagePng = Convert.ToBase64String([1, 2, 3]); Reject(badPng.Validate, "Incomplete PNG rejected");
var reward = new CustomRewardDefinition { Name = "Defensive relic", Kind = CustomRewardDefinition.CustomRelics,
    Trigger = "While owned", Target = "Whole team", IconPng = png };
reward.Validate(); coach.Relics.Add(reward.Id); coach.Rewards.Add(reward);
var dependentCopy = coach.Copy(); dependentCopy.Rewards[0].Name = "Changed";
Check(coach.Rewards[0].Name == "Defensive relic", "Custom relic dependencies are deep copied");
var existing = new Dictionary<string, CustomRewardDefinition>(StringComparer.OrdinalIgnoreCase) { [reward.Id] = reward.Copy() };
CustomCoachPool.AddDependencies(existing, coach); Check(existing.Count == 1, "Identical reward definitions merge safely");
var before = existing[reward.Id].ToDocument().ToString();
Reject(() => CustomCoachPool.AddDependencies(existing, dependentCopy), "Conflicting custom relic identity rejected");
Check(existing[reward.Id].ToDocument().ToString() == before, "Conflict leaves caller data unchanged");
var invalidDependency = coach.Copy(); invalidDependency.Rewards[0].NeedsRedesign = true;
Reject(invalidDependency.Validate, "Unsupported custom relic cannot enter Coach pool");
map.Nodes[0].CustomCoach = coach;
var snapshot = MapLayout.Parse(map.Serialize());
coach.Relics.Clear();
Check(snapshot.Nodes[0].CustomCoach.Relics.Contains(reward.Id), "Applied map snapshot cannot follow later author edits");
Check(snapshot.Nodes[0].CustomCoach.Rewards[0].IconPng == png, "Map snapshot embeds dependency art too");
snapshot.Nodes[0].Type = "Nurse"; snapshot.Nodes[0].CustomCoach = null;
Check(!snapshot.Serialize().Contains("Custom coach coach"), "Normal conversion discards active custom metadata");
string dir = Path.Combine(Path.GetTempPath(), "custom-coach-checks-" + Guid.NewGuid().ToString("N"));
try {
    var first = copy.SaveNew(dir); var second = copy.SaveNew(dir);
    Check(first.Id != second.Id && first.Id != copy.Id, "Save as new creates independent library identities");
    Check(Directory.GetFiles(dir, "*.coach").Length == 2, "New saves cannot overwrite library entries");
    Check(CustomCoachDefinition.Parse(File.ReadAllText(Path.Combine(dir, first.Id + ".coach"))).Relics.SetEquals(["relic-a", "relic-b"]), "Library copy reloads exact pool");
} finally { Directory.Delete(dir, true); }
Console.WriteLine($"PASS {checks} custom Coach checks");
