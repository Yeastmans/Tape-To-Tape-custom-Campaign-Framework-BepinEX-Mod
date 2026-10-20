using System.Collections;
using System.Reflection;
using System.Text.Json;
using CampaignCreator;

// All native saves, sidecars and snapshots are passed explicitly below. These
// checks never call RuntimeRoot, UserContentPaths.EnsureRoot, or launch Studio.
string output = Path.GetFullPath(args.Length == 1 ? args[0] : "artifacts/community-management-20261017/play-now-checks");
string root = Path.Combine(output, Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
Type exporter = typeof(MainWindow).Assembly.GetType("CampaignCreator.ExportTranslation", true)!;
Type packageType = exporter.GetNestedType("Package", BindingFlags.NonPublic)!;
const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
string[] slots = ["leftWingerId", "rightWingerId", "centerId", "leftDefensemenId", "rightDefensemenId", "goalieId"];
int checks = 0;
void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; Console.WriteLine("PASS " + message); }
object? Call(string name, params object?[] values)
{
    try { return exporter.GetMethod(name, flags)!.Invoke(null, values); }
    catch (TargetInvocationException e) when (e.InnerException != null) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw(); throw; }
}
object Package(string id, string prefix, string name)
{
    var model = new Dictionary<string, object> { ["Id"] = id, ["TeamName"] = name, ["City"] = "Test City" };
    var saves = new Dictionary<string, string>();
    var sidecars = new Dictionary<string, string>();
    for (int i = 0; i < slots.Length; i++)
    {
        string pid = prefix + i;
        model[slots[i]] = pid;
        string json = JsonSerializer.Serialize(new { id = pid, firstName = name, lastName = "Player", speed = 89 });
        saves[(i == 5 ? "CustomGoalie-" : "CustomForward-") + pid + ".json"] = json;
        sidecars[pid + ".txt"] = "Name = " + name + " Player\nExport Version = 3\nExport Model = " + json + "\n";
    }
    saves["CustomTeam-" + id + ".json"] = JsonSerializer.Serialize(model);
    sidecars[id + ".team.txt"] = "Team Name = " + name + "\nExport Version = 3\n";
    return Activator.CreateInstance(packageType, id, saves, sidecars)!;
}
(string Save, string Overrides, string Library) Fixture(string label)
{
    string folder = Path.Combine(root, label), save = Path.Combine(folder, "native", "TeamDataModels"), sidecars = Path.Combine(folder, "runtime", "play_now_overrides"), library = Path.Combine(folder, "workspace");
    Directory.CreateDirectory(save);
    Call("Write", Package("old-team", "old-player-", "Before"), save, sidecars, library);
    return (save, sidecars, library);
}
object Target((string Save, string Overrides, string Library) f)
    => ((IEnumerable)Call("ExistingTeams", f.Save, f.Overrides, f.Library)!).Cast<object>().Single();
string Update(object package, (string Save, string Overrides, string Library) f)
{
    return (string)Call("WriteUpdate", package, Target(f), f.Save, f.Overrides, f.Library)!;
}
Dictionary<string, string> Files(string folder) => Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
    .ToDictionary(f => Path.GetRelativePath(folder, f), f => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(f))));
void SameFiles(Dictionary<string, string> before, string folder, string label)
{
    var after = Files(folder);
    Check(before.Count == after.Count && before.All(p => after.TryGetValue(p.Key, out var hash) && hash == p.Value), label);
}
void Reject(Action action, string label)
{
    try { action(); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Check(true, label); return; }
    throw new Exception(label);
}
var first = Fixture("update");
string result = Update(Package("new-team", "new-player-", "After"), first);
Check(Path.GetFileName(result) == "CustomTeam-old-team.json" && Directory.GetFiles(first.Save, "CustomTeam-*.json").Length == 1,
    "Updating an explicitly selected export retains its team ID without adding another team");
using (var team = JsonDocument.Parse(File.ReadAllText(result)))
{
    Check(team.RootElement.GetProperty("TeamName").GetString() == "After", "Update publishes the edited team fields");
    Check(team.RootElement.GetProperty("centerId").GetString() == "old-player-2", "Unshared roster IDs remain stable");
}
Check(Directory.GetFiles(first.Save, "Custom*.json").Length == 7, "Updating an unshared roster adds no native player copies");
using (var player = JsonDocument.Parse(File.ReadAllText(Path.Combine(first.Save, "CustomForward-old-player-2.json"))))
    Check(player.RootElement.GetProperty("firstName").GetString() == "After", "The retained roster ID receives the edited player fields");
var sidecar = new CampaignCore.CampaignDocument(File.ReadAllText(Path.Combine(first.Overrides, "old-player-2.txt")));
using (var model = JsonDocument.Parse(sidecar.Get("Export Model")))
    Check(model.RootElement.GetProperty("id").GetString() == "old-player-2" && model.RootElement.GetProperty("firstName").GetString() == "After", "Runtime companion data uses the retained identity and edited model");
Check(File.ReadAllText(Path.Combine(first.Library, "library", "Exported Teams", "old-team", "team.txt")).Contains("Team Name = After"), "The import-library snapshot receives updated team data");
Check(!Directory.GetFiles(Path.GetDirectoryName(first.Save)!, ".t2t-export-*", SearchOption.AllDirectories).Any(), "Successful update leaves no transaction files");

// A shared player must remain byte-for-byte unchanged, including its sidecar.
var shared = Fixture("shared");
File.WriteAllText(Path.Combine(shared.Save, "CustomTeam-other.json"), "{\"Id\":\"other\",\"TeamName\":\"Other\",\"centerId\":\"old-player-2\"}");
string sharedNative = File.ReadAllText(Path.Combine(shared.Save, "CustomForward-old-player-2.json"));
string sharedSidecar = File.ReadAllText(Path.Combine(shared.Overrides, "old-player-2.txt"));
string other = File.ReadAllText(Path.Combine(shared.Save, "CustomTeam-other.json"));
Update(Package("fresh-team", "fresh-player-", "Shared edit"), shared);
using (var team = JsonDocument.Parse(File.ReadAllText(Path.Combine(shared.Save, "CustomTeam-old-team.json"))))
    Check(team.RootElement.GetProperty("centerId").GetString() == "fresh-player-2" && team.RootElement.GetProperty("leftWingerId").GetString() == "old-player-0", "Only shared roster identities are replaced with independent players");
Check(File.ReadAllText(Path.Combine(shared.Save, "CustomForward-old-player-2.json")) == sharedNative && File.ReadAllText(Path.Combine(shared.Overrides, "old-player-2.txt")) == sharedSidecar && File.ReadAllText(Path.Combine(shared.Save, "CustomTeam-other.json")) == other, "Shared player, its companion data and unrelated team are unchanged");
string sharedSnapshot = Path.Combine(shared.Library, "library", "Exported Teams", "old-team", "players");
Check(!File.Exists(Path.Combine(sharedSnapshot, "old-player-2.txt")) && File.Exists(Path.Combine(sharedSnapshot, "fresh-player-2.txt")), "Updated import snapshot contains the current roster without a stale extra slot");

// Removing evidence excludes ordinary game-editor teams; legacy sidecars suffice.
var legacy = Fixture("legacy");
Directory.Move(Path.Combine(legacy.Library, "library", "Exported Teams"), Path.Combine(legacy.Library, "prior-snapshots"));
File.WriteAllText(Path.Combine(legacy.Save, "CustomTeam-native.json"), "{\"Id\":\"native\",\"TeamName\":\"Before\"}");
var choices = ((IEnumerable)Call("ExistingTeams", legacy.Save, legacy.Overrides, legacy.Library)!).Cast<object>().ToArray();
Check(choices.Length == 1 && choices[0].ToString()!.Contains("old-team"), "Legacy Studio exports remain selectable while ordinary game-editor teams are excluded");
Update(Package("legacy-new", "legacy-player-", "Legacy edit"), legacy);
Check(File.Exists(Path.Combine(legacy.Library, "library", "Exported Teams", "old-team", "team.txt")), "Updating a legacy export creates its missing library snapshot");
using (var team = JsonDocument.Parse(File.ReadAllText(Path.Combine(legacy.Save, "CustomTeam-old-team.json"))))
    Check(team.RootElement.GetProperty("centerId").GetString() == "legacy-player-2", "A legacy export without roster ownership evidence receives independent players");
using (var oldPlayer = JsonDocument.Parse(File.ReadAllText(Path.Combine(legacy.Save, "CustomForward-old-player-2.json"))))
    Check(oldPlayer.RootElement.GetProperty("firstName").GetString() == "Before", "An existing player with only generic sidecar evidence is preserved");

// Hold the final native team open without delete sharing: earlier writes must roll back.
var rollback = Fixture("rollback");
string rollbackRoot = Path.GetDirectoryName(rollback.Library)!;
var rollbackBefore = Files(rollbackRoot);
var rollbackTarget = Target(rollback);
using (var locked = new FileStream(Path.Combine(rollback.Save, "CustomTeam-old-team.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
    Reject(() => Call("WriteUpdate", Package("rollback-new", "rollback-player-", "Rejected edit"), rollbackTarget, rollback.Save, rollback.Overrides, rollback.Library), "A failed final native publication reports failure");
SameFiles(rollbackBefore, rollbackRoot, "Failure restores all native saves, runtime sidecars and library snapshots exactly");

var stale = Fixture("stale");
var staleTarget = Target(stale);
File.AppendAllText(Path.Combine(stale.Save, "CustomTeam-old-team.json"), " ");
var staleBefore = Files(Path.GetDirectoryName(stale.Library)!);
Reject(() => Call("WriteUpdate", Package("stale-new", "stale-player-", "Rejected"), staleTarget, stale.Save, stale.Overrides, stale.Library), "A target changed after selection is rejected");
SameFiles(staleBefore, Path.GetDirectoryName(stale.Library)!, "Stale selection rejection leaves every export file unchanged");

var unreadable = Fixture("unreadable");
File.WriteAllText(Path.Combine(unreadable.Save, "CustomTeam-broken.json"), "invalid-json");
var unreadableBefore = Files(Path.GetDirectoryName(unreadable.Library)!);
try { Update(Package("broken-new", "broken-player-", "Rejected"), unreadable); throw new Exception("Unreadable sharing information was accepted"); }
catch (JsonException) { Check(true, "Unreadable other teams prevent unsafe player replacement"); }
SameFiles(unreadableBefore, Path.GetDirectoryName(unreadable.Library)!, "Unreadable team rejection preserves all export files");

var create = Fixture("create-new");
var original = Files(Path.GetDirectoryName(create.Library)!);
string created = (string)Call("Write", Package("second-team", "second-player-", "New copy"), create.Save, create.Overrides, create.Library)!;
Check(Path.GetFileName(created) == "CustomTeam-second-team.json" && Directory.GetFiles(create.Save, "Custom*.json").Length == 14, "Export as new retains the existing independent-copy behavior");
var createAfter = Files(Path.GetDirectoryName(create.Library)!);
Check(original.All(p => createAfter[p.Key] == p.Value), "Export as new preserves every earlier export file");
Console.WriteLine($"{checks} Play Now checks passed. Isolated evidence: {root}");
