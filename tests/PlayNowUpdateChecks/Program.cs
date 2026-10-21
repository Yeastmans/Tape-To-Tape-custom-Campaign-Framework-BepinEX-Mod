using System.Collections;
using System.Reflection;
using System.Text.Json;
using CampaignCreator;
using CampaignCore;

// All native saves, sidecars and snapshots are passed explicitly below. These
// checks never call RuntimeRoot, UserContentPaths.EnsureRoot, or launch Studio.
string output = Path.GetFullPath(args.Length == 1 ? args[0] : "artifacts/four-features/play-now-update-checks");
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
object Package(string id, string prefix, string name, bool twoLines = false)
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
    if (twoLines)
    {
        var roster = new PlayNowRoster(
            [prefix + "0", prefix + "2", prefix + "1", prefix + "3", prefix + "4"],
            Enumerable.Range(6, 5).Select(i => prefix + i).ToArray(), prefix + "5", true);
        for (int i = 6; i < 11; i++)
        {
            string pid = prefix + i;
            string json = JsonSerializer.Serialize(new { id = pid, firstName = name, lastName = "Second", speed = 89 });
            saves["CustomForward-" + pid + ".json"] = json;
            sidecars[pid + ".txt"] = "Name = " + name + " Second\nExport Version = 3\nExport Model = " + json + "\n";
        }
        var teamSidecar = new CampaignDocument("Team Name = " + name + "\nExport Version = 3\n");
        PlayNowRosterCodec.Write(teamSidecar, roster);
        sidecars[id + ".team.txt"] = teamSidecar.ToString();
    }
    saves["CustomTeam-" + id + ".json"] = JsonSerializer.Serialize(model);
    if (!twoLines) sidecars[id + ".team.txt"] = "Team Name = " + name + "\nExport Version = 3\n";
    return Activator.CreateInstance(packageType, id, saves, sidecars)!;
}
(string Save, string Overrides, string Library) Fixture(string label, bool twoLines = false)
{
    string folder = Path.Combine(root, label), save = Path.Combine(folder, "native", "TeamDataModels"), sidecars = Path.Combine(folder, "runtime", "play_now_overrides"), library = Path.Combine(folder, "workspace");
    Directory.CreateDirectory(save);
    Call("Write", Package("old-team", "old-player-", "Before", twoLines), save, sidecars, library);
    return (save, sidecars, library);
}
object Target((string Save, string Overrides, string Library) f)
    => ((IEnumerable)Call("ExistingTeams", f.Save, f.Overrides, f.Library)!).Cast<object>()
        .Single(item => (string)item.GetType().GetProperty("Id")!.GetValue(item)! == "old-team");
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

var lineShared = Fixture("line-two-shared", twoLines: true);
Call("Write", Package("other-team", "other-player-", "Other", true), lineShared.Save, lineShared.Overrides, lineShared.Library);
string otherSidecarFile = Path.Combine(lineShared.Overrides, "other-team.team.txt");
var otherSidecarDoc = new CampaignDocument(File.ReadAllText(otherSidecarFile));
Check(PlayNowRosterCodec.TryRead(otherSidecarDoc, out var otherRoster, out _), "Second team's roster extension reads");
otherRoster.Line2[0] = "old-player-6";
PlayNowRosterCodec.Write(otherSidecarDoc, otherRoster);
File.WriteAllText(otherSidecarFile, otherSidecarDoc.ToString());
var sharedLineBefore = Files(Path.GetDirectoryName(lineShared.Library)!);
Update(Package("incoming", "incoming-player-", "After", true), lineShared);
var updatedLineDoc = new CampaignDocument(File.ReadAllText(Path.Combine(lineShared.Overrides, "old-team.team.txt")));
Check(PlayNowRosterCodec.TryRead(updatedLineDoc, out var updatedLine, out _)
    && updatedLine.Line2[0] == "incoming-player-6" && updatedLine.Line2[1] == "old-player-7", "LineTwoSharedOwnership: shared Line 2 ID is remapped and unshared Line 2 ID is reused");
var sharedLineAfter = Files(Path.GetDirectoryName(lineShared.Library)!);
Check(sharedLineBefore.Where(p => (p.Key.Contains("old-player-6") && !p.Key.Contains(Path.Combine("Exported Teams", "old-team")))
        || p.Key.Contains("other-team") || p.Key.Contains("other-player"))
    .All(p => sharedLineAfter.TryGetValue(p.Key, out var hash) && hash == p.Value)
    && sharedLineAfter.Keys.Where(p => p.Contains("other-team") || p.Contains("other-player")).All(sharedLineBefore.ContainsKey),
    "LineTwoSharedOwnership: shared and other-team files stay byte-for-byte unchanged with no outside writes");

var expand = Fixture("one-to-two");
Update(Package("expanded", "expanded-player-", "Expanded", true), expand);
var expandedSidecar = new CampaignDocument(File.ReadAllText(Path.Combine(expand.Overrides, "old-team.team.txt")));
Check(PlayNowRosterCodec.TryRead(expandedSidecar, out var expandedRoster, out _)
    && expandedRoster.Line1[1] == "old-player-2" && expandedRoster.Line2.SequenceEqual(Enumerable.Range(6, 5).Select(i => "expanded-player-" + i))
    && Directory.GetFiles(expand.Save, "Custom*.json").Length == 12, "OneToTwoAddsLineTwo: first line keeps owned IDs and new second line receives independent records");

var malformedExtension = Fixture("malformed-other", twoLines: true);
File.WriteAllText(Path.Combine(malformedExtension.Save, "CustomTeam-other.json"), "{\"Id\":\"other\",\"TeamName\":\"Other\"}");
File.WriteAllText(Path.Combine(malformedExtension.Overrides, "other.team.txt"), "Export Roster Version = 999\n");
var malformedBefore = Files(Path.GetDirectoryName(malformedExtension.Library)!);
Reject(() => Update(Package("next", "next-player-", "After", true), malformedExtension), "Malformed other-team extension prevents uncertain ownership reuse");
SameFiles(malformedBefore, Path.GetDirectoryName(malformedExtension.Library)!, "Malformed extension leaves every file unchanged");

var mismatchedIdentity = Fixture("mismatched-team-identity", twoLines: true);
Call("Write", Package("other", "other-player-", "Other", true), mismatchedIdentity.Save, mismatchedIdentity.Overrides, mismatchedIdentity.Library);
string mismatchedSidecarFile = Path.Combine(mismatchedIdentity.Overrides, "other.team.txt");
var mismatchedSidecar = new CampaignDocument(File.ReadAllText(mismatchedSidecarFile));
Check(PlayNowRosterCodec.TryRead(mismatchedSidecar, out var mismatchedRoster, out _), "Mismatched identity fixture's extension reads");
mismatchedRoster.Line2[0] = "old-player-6";
PlayNowRosterCodec.Write(mismatchedSidecar, mismatchedRoster);
File.WriteAllText(mismatchedSidecarFile, mismatchedSidecar.ToString());
string mismatchedNativeFile = Path.Combine(mismatchedIdentity.Save, "CustomTeam-other.json");
var mismatchedNative = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(mismatchedNativeFile))!;
mismatchedNative["Id"] = "different";
File.WriteAllText(mismatchedNativeFile, mismatchedNative.ToJsonString());
var mismatchedBefore = Files(Path.GetDirectoryName(mismatchedIdentity.Library)!);
Reject(() => Update(Package("next", "next-player-", "After", true), mismatchedIdentity),
    "Mismatched filename and JSON team identity rejects uncertain sidecar ownership");
SameFiles(mismatchedBefore, Path.GetDirectoryName(mismatchedIdentity.Library)!,
    "Mismatched identity rejection preserves the shared Line 2 player and every other file exactly");

var duplicateIdentity = Fixture("duplicate-team-identity", twoLines: true);
File.WriteAllText(Path.Combine(duplicateIdentity.Save, "CustomTeam-duplicate.json"), "{\"Id\":\"old-team\",\"TeamName\":\"Duplicate\"}");
var duplicateBefore = Files(Path.GetDirectoryName(duplicateIdentity.Library)!);
Reject(() => Update(Package("next", "next-player-", "After", true), duplicateIdentity),
    "Duplicate JSON team identity rejects ownership decisions");
SameFiles(duplicateBefore, Path.GetDirectoryName(duplicateIdentity.Library)!,
    "Duplicate identity rejection leaves all native and companion bytes unchanged");

var shrink = Fixture("two-to-one", twoLines: true);
File.WriteAllText(Path.Combine(shrink.Save, "CustomTeam-other.json"), "{\"Id\":\"other\",\"TeamName\":\"Other\",\"centerId\":\"old-player-6\"}");
var shrinkBefore = Files(Path.GetDirectoryName(shrink.Library)!);
Update(Package("single", "single-player-", "Single"), shrink);
Check(File.Exists(Path.Combine(shrink.Save, "CustomForward-old-player-6.json"))
    && File.Exists(Path.Combine(shrink.Overrides, "old-player-6.txt"))
    && !File.Exists(Path.Combine(shrink.Save, "CustomForward-old-player-7.json"))
    && !File.Exists(Path.Combine(shrink.Overrides, "old-player-7.txt")), "TwoToOneRetainsSharedFiles: shared Line 2 files survive and unshared retired files are removed");
Check(Files(Path.GetDirectoryName(shrink.Library)!).Where(p => p.Key.Contains("old-player-6") || p.Key.Contains("CustomTeam-other"))
    .All(p => shrinkBefore.TryGetValue(p.Key, out var hash) && hash == p.Value), "TwoToOneRetainsSharedFiles: retained bytes are exact");

var standalone = Fixture("standalone-retained", twoLines: true);
string standaloneCopy = Path.Combine(standalone.Library, "library", "Exported Players", "old-player-7.txt");
Directory.CreateDirectory(Path.GetDirectoryName(standaloneCopy)!);
File.Copy(Path.Combine(standalone.Overrides, "old-player-7.txt"), standaloneCopy);
var standaloneBefore = Files(Path.GetDirectoryName(standalone.Library)!);
Update(Package("single", "single-player-", "Single"), standalone);
var standaloneAfter = Files(Path.GetDirectoryName(standalone.Library)!);
Check(new[] { Path.Combine("native", "TeamDataModels", "CustomForward-old-player-7.json"),
        Path.Combine("runtime", "play_now_overrides", "old-player-7.txt"),
        Path.Combine("workspace", "library", "Exported Players", "old-player-7.txt") }
    .All(path => standaloneAfter.TryGetValue(path, out var hash) && standaloneBefore[path] == hash),
    "TwoToOneRetainsStandaloneFiles: separately exported player copies remain byte-for-byte unchanged");

var activeStandalone = Fixture("active-standalone");
string activeCopy = Path.Combine(activeStandalone.Library, "library", "Exported Players", "old-player-2.txt");
Directory.CreateDirectory(Path.GetDirectoryName(activeCopy)!);
File.Copy(Path.Combine(activeStandalone.Overrides, "old-player-2.txt"), activeCopy);
var activeBefore = Files(Path.GetDirectoryName(activeStandalone.Library)!);
Update(Package("next", "next-player-", "After"), activeStandalone);
var activeAfter = Files(Path.GetDirectoryName(activeStandalone.Library)!);
using (var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(activeStandalone.Save, "CustomTeam-old-team.json"))))
    Check(native.RootElement.GetProperty("centerId").GetString() == "next-player-2",
        "Active standalone identity is remapped instead of overwritten");
Check(new[] { Path.Combine("native", "TeamDataModels", "CustomForward-old-player-2.json"),
        Path.Combine("runtime", "play_now_overrides", "old-player-2.txt"),
        Path.Combine("workspace", "library", "Exported Players", "old-player-2.txt") }
    .All(path => activeAfter.TryGetValue(path, out var hash) && activeBefore[path] == hash),
    "Active standalone native, companion and library bytes remain exact");

var nativeEdit = Fixture("native-edit", twoLines: true);
var nativeChoice = Target(nativeEdit);
File.AppendAllText(Path.Combine(nativeEdit.Save, "CustomTeam-old-team.json"), " ");
var nativeBefore = Files(Path.GetDirectoryName(nativeEdit.Library)!);
Reject(() => Call("WriteUpdate", Package("next", "next-player-", "After", true), nativeChoice, nativeEdit.Save, nativeEdit.Overrides, nativeEdit.Library), "NativeEditInvalidatesChooser: changed native JSON rejects stale selection");
SameFiles(nativeBefore, Path.GetDirectoryName(nativeEdit.Library)!, "NativeEditInvalidatesChooser: every preexisting byte remains");

var sidecarEdit = Fixture("sidecar-edit", twoLines: true);
var sidecarChoice = Target(sidecarEdit);
File.AppendAllText(Path.Combine(sidecarEdit.Overrides, "old-team.team.txt"), "# concurrent edit\n");
var sidecarBefore = Files(Path.GetDirectoryName(sidecarEdit.Library)!);
Reject(() => Call("WriteUpdate", Package("next", "next-player-", "After", true), sidecarChoice, sidecarEdit.Save, sidecarEdit.Overrides, sidecarEdit.Library), "Concurrent sidecar mutation invalidates chooser");
SameFiles(sidecarBefore, Path.GetDirectoryName(sidecarEdit.Library)!, "Concurrent sidecar mutation preserves all bytes");

var bothRollback = Fixture("both-lines-rollback", twoLines: true);
var bothBefore = Files(Path.GetDirectoryName(bothRollback.Library)!);
var bothChoice = Target(bothRollback);
using (var locked = new FileStream(Path.Combine(bothRollback.Save, "CustomTeam-old-team.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
    Reject(() => Call("WriteUpdate", Package("next", "next-player-", "After", true), bothChoice, bothRollback.Save, bothRollback.Overrides, bothRollback.Library), "UpdateRollbackRestoresBothLines: final native publication fails");
SameFiles(bothBefore, Path.GetDirectoryName(bothRollback.Library)!, "UpdateRollbackRestoresBothLines: all native, sidecar and snapshot bytes restore");

var importFixture = Fixture("import-live", twoLines: true);
string liveSidecarFile = Path.Combine(importFixture.Overrides, "old-team.team.txt");
var liveSidecar = new CampaignDocument(File.ReadAllText(liveSidecarFile));
Check(PlayNowRosterCodec.TryRead(liveSidecar, out var liveRoster, out _), "Import fixture's live sidecar reads");
(liveRoster.Line2[0], liveRoster.Line2[1]) = (liveRoster.Line2[1], liveRoster.Line2[0]);
PlayNowRosterCodec.Write(liveSidecar, liveRoster);
File.WriteAllText(liveSidecarFile, liveSidecar.ToString());
Check((bool)Target(importFixture).GetType().GetProperty("LineupChanged")!.GetValue(Target(importFixture))!,
    "LineupChanged exposes current-sidecar versus saved-library roster difference");
var contentType = typeof(MainWindow).Assembly.GetType("CampaignCreator.GameCustomContent", true)!;
var load = contentType.GetMethod("Load", flags, [typeof(string), typeof(string)])!;
var imported = ((IEnumerable)load.Invoke(null, [importFixture.Library, importFixture.Save])!).Cast<object>().ToArray();
string ItemFile(object item) => (string)item.GetType().GetProperty("File")!.GetValue(item)!;
string importedTeam = ItemFile(imported.Single(item => ItemFile(item).EndsWith("team.txt")));
string importedPlayers = Path.Combine(Path.GetDirectoryName(importedTeam)!, "players");
Check(new CampaignDocument(File.ReadAllText(Path.Combine(importedPlayers, "Line 2 Left Wing - old-player-7.txt"))).Get("Position") == "Line 2 Left Wing",
    "ImportUsesCurrentSavedLineup: reimport follows the live sidecar position instead of the library snapshot");
Check(File.Exists(Path.Combine(importedPlayers, "Line 2 Center - old-player-6.txt")) && File.Exists(Path.Combine(importedPlayers, "Line 2 Right Wing - old-player-8.txt")), "Reimport maps canonical LW,C,RW wire order exactly");
string editedPath = Path.Combine(importFixture.Save, "CustomForward-old-player-7.json");
var editedNative = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(editedPath))!;
editedNative["firstName"] = "Edited"; editedNative["speed"] = 42; editedNative["abilityId"] = ""; editedNative["talentIds"] = new System.Text.Json.Nodes.JsonArray();
File.WriteAllText(editedPath, editedNative.ToJsonString());
var editedDoc = new CampaignDocument(File.ReadAllText(Path.Combine(importFixture.Overrides, "old-player-7.txt")));
editedDoc.Set("Override Team Settings", "yes"); editedDoc.Set("Pass Read", "0.731"); editedDoc.Set("Stick Color", "17, 23, 31");
File.WriteAllText(Path.Combine(importFixture.Overrides, "old-player-7.txt"), editedDoc.ToString());
string oldSnapshot = Path.Combine(importFixture.Library, "library", "Exported Players", "old-player-7.txt");
Directory.CreateDirectory(Path.GetDirectoryName(oldSnapshot)!); File.WriteAllText(oldSnapshot, "Name = Stale Standalone\n");
var teamNativePath = Path.Combine(importFixture.Save, "CustomTeam-old-team.json");
var teamNative = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(teamNativePath))!;
teamNative["TeamName"] = "Edited Team"; teamNative["relics"] = new System.Text.Json.Nodes.JsonArray(); File.WriteAllText(teamNativePath, teamNative.ToJsonString());
liveSidecar.Set("Goal Sound Data", "retained-audio"); liveSidecar.Set("Team Relics", "stale-relic"); File.WriteAllText(liveSidecarFile, liveSidecar.ToString());
imported = ((IEnumerable)load.Invoke(null, [importFixture.Library, importFixture.Save])!).Cast<object>().ToArray();
var currentPlayer = new CampaignDocument(File.ReadAllText(Path.Combine(importedPlayers, "Line 2 Left Wing - old-player-7.txt")));
Check(currentPlayer.Get("Name") == "Edited Second" && currentPlayer.Get("Speed") == "42" && currentPlayer.Get("Ability") == "" && currentPlayer.Get("Talents") == "", "Reimport uses native edited values and explicit reward clears");
Check(currentPlayer.Get("Override Team Settings") == "yes" && currentPlayer.Get("Pass Read") == "0.731" && currentPlayer.Get("Stick Color") == "17, 23, 31", "Reimport retains omitted personal fields and own policy");
var currentTeam = new CampaignDocument(File.ReadAllText(importedTeam));
Check(currentTeam.Get("Team Name") == "Edited Team" && currentTeam.Get("Team Relics") == "" && currentTeam.Get("Goal Sound Data") == "retained-audio", "Reimport merges current team edits with supplemental fields");
string loosePlayer = ItemFile(imported.Single(item => ItemFile(item).EndsWith(Path.Combine("players", "old-player-7.txt"))));
Check(new CampaignDocument(File.ReadAllText(loosePlayer)).Get("Name") == "Edited Second" && File.ReadAllText(oldSnapshot) == "Name = Stale Standalone\n", "Standalone reimport uses current persisted player without rewriting historical snapshot");
var incompatible = new CampaignDocument(File.ReadAllText(liveSidecarFile));
PlayNowRosterCodec.TryRead(incompatible,out var incompatibleRoster,out _);
(incompatibleRoster.Line1[1],incompatibleRoster.Line1[2])=(incompatibleRoster.Line1[2],incompatibleRoster.Line1[1]);
PlayNowRosterCodec.Write(incompatible,incompatibleRoster);File.WriteAllText(liveSidecarFile,incompatible.ToString());
try { load.Invoke(null,[importFixture.Library,importFixture.Save]); throw new Exception("Old candidate mismatched v1 positions were silently imported"); }
catch(TargetInvocationException ex) when(ex.InnerException is IOException) {Check(true,"Incompatible pre-release candidate v1 order requires re-export instead of guessed migration");}
Reject(()=>Update(Package("newer","newer-player-","Replacement",true),importFixture),"Updating an incompatible candidate order refuses guessed second-line remapping");
File.WriteAllText(liveSidecarFile,liveSidecar.ToString());
string pendingJournal = Path.Combine(importFixture.Overrides, "save-journal");Directory.CreateDirectory(pendingJournal);File.WriteAllText(Path.Combine(pendingJournal,"team-old-team.json"),"pending");
try { load.Invoke(null, [importFixture.Library, importFixture.Save]); throw new Exception("Pending pair was reimported"); }
catch (TargetInvocationException ex) when (ex.InnerException is IOException) { Check(true, "Reimport rejects an interrupted pair until runtime recovery"); }
Console.WriteLine($"{checks} Play Now checks passed. Isolated evidence: {root}");
