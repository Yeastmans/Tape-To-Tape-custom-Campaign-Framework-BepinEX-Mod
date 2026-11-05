using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CampaignCore;
using CampaignCreator;

if (args.Length != 1) throw new ArgumentException("Supply a new isolated fixture directory.");
string root = Path.GetFullPath(args[0]);
if (Directory.Exists(root)) throw new IOException("Use a new fixture directory.");
Directory.CreateDirectory(root);
var assembly = typeof(MainWindow).Assembly;
AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
int passed = 0, total = 0;
void Check(bool ok, string label) { total++; if (ok) passed++; Console.WriteLine((ok ? "PASS " : "FAIL ") + label); }
void Case(string label, Action action) { try { action(); } catch (Exception e) { Check(false, label + ": " + e.GetBaseException().Message); } }
object? Call(string type, string method, params object?[] args)
{
    try { return assembly.GetType("CampaignCreator." + type)!.GetMethod(method, Static)!.Invoke(null, args); }
    catch (TargetInvocationException e) when (e.InnerException != null) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw(); throw; }
}
object Data() => Activator.CreateInstance(assembly.GetType("CampaignCreator.GameData")!, [root])!;
object Report(string policy = "Strict")
{
    var type = assembly.GetType("CampaignCreator.ExportReport")!;
    var policyType = typeof(CustomRewardExport).Assembly.GetType("CampaignCore.PlayNowRewardPolicy");
    return policyType == null ? Activator.CreateInstance(type, true)! : Activator.CreateInstance(type, Instance, null, [Enum.Parse(policyType, policy)], null)!;
}
void Reject(Action action, string label)
{
    try { action(); Check(false, label); } catch (ArgumentException) { Check(true, label); }
}
var icon = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jN1sAAAAASUVORK5CYII=");
CustomRewardDefinition Reward(string name, bool compatible, bool relic = false)
{
    var result = new CustomRewardDefinition { Name = name, Kind = relic ? CustomRewardDefinition.CustomRelics : CustomRewardDefinition.CustomTalents,
        Trigger = "Goal", Duration = compatible ? "Rest of match" : "Rest of run", Target = relic ? "Whole team" : "Triggering player", IconPng = Convert.ToBase64String(icon) };
    result.Validate(); result.Save(Path.Combine(root, "library", "rewards")); return result;
}
var unsupported = Reward("Campaign scorer", false);
var supported = Reward("Match scorer", true);
var relic = Reward("Campaign trophy", false, true);
var matchRelic = Reward("Match trophy", true, true);
var player = new CampaignDocument("Name = Export Person\nFace = Faces/Toronto/Mathieu\nSpeed = 83\nSize = Medium\nNumber = 12\nTalents = " + unsupported.Id + "," + supported.Id);
object BuildPlayer(string policy) => Call("PlayerExport", "Build", player, Data(), "skater", Report(policy))!;
(Dictionary<string, object> Model, CampaignDocument Sidecar) PlayerParts(object value)
    => ((Dictionary<string, object>)value.GetType().GetField("Item2")!.GetValue(value)!, (CampaignDocument)value.GetType().GetField("Item3")!.GetValue(value)!);
Dictionary<string,string> Files() => Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(f => f, f => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))));
var originals = Files();
Case("strict defaults", () => Reject(() => Call("PlayerExport", "Build", player, Data(), "skater", null), "Default player API rejects campaign-only rewards"));
Case("skip player", () => {
    var p = PlayerParts(BuildPlayer("SkipUnsupported")); var embedded = CustomRewardExport.Read(p.Sidecar).ToArray();
    Check(embedded.Length == 1 && embedded[0].Name == supported.Name, "Skip keeps only compatible player snapshots");
    Check(((string[])p.Model["talentIds"]).SequenceEqual(embedded.Select(d => d.Id)) && p.Sidecar.Get("Export Talent IDs") == embedded.Single().Id,
        "Skip model and companion talent IDs match the remaining copied definition");
    Check((int)p.Model["speed"] == 83 && embedded.Single().Id != supported.Id, "Skip preserves unrelated settings and fresh copied reward identities");
});
Case("anyway player", () => {
    var p = PlayerParts(BuildPlayer("ExportAnyway")); var embedded = CustomRewardExport.Read(p.Sidecar).ToArray();
    Check(embedded.Length == 2 && embedded.All(d => d.Id != unsupported.Id && d.Id != supported.Id), "Export anyway preserves all rules with fresh IDs");
    Check(embedded.Single(d => d.Name == unsupported.Name).Duration == "Rest of run", "Export anyway does not convert campaign-only behavior");
    var report = Report("ExportAnyway"); Call("PlayerExport", "Build", player, Data(), "skater", report);
    var warnings = report.GetType().GetProperty("Unsupported", Instance)?.GetValue(report) as System.Collections.IEnumerable;
    Check(warnings != null && warnings.Cast<object>().Any(w => w.ToString()!.Contains(unsupported.Name)), "Warning identifies the authored reward name");
});
string teamFolder = Path.Combine(root, "authored-team"); Directory.CreateDirectory(Path.Combine(teamFolder, "players"));
var team = new CampaignDocument("Team Name = Warning Team\nTeam Talents = " + unsupported.Id + "," + supported.Id + "\nTeam Relics = " + relic.Id + "," + matchRelic.Id);
string teamFile = Path.Combine(teamFolder, "team.txt"); team.Save(teamFile);
string[] positions = ["Left Wing", "Right Wing", "Center", "Left Defense", "Right Defense", "Goalie"];
for (int i = 0; i < positions.Length; i++)
    new CampaignDocument("Name = Roster " + i + "\nPosition = " + positions[i] + "\nFace = Faces/Toronto/Mathieu\nSpeed = 71\n" + (i == 5 ? "Catching = 60\n" : ""))
        .Save(Path.Combine(teamFolder, "players", i + ".txt"));
object BuildTeam(string policy, CampaignDocument? source = null) => Call("ExportTranslation", "BuildTeam", source ?? team, teamFile, Data(), root, Report(policy))!;
(Dictionary<string,string> Saves, Dictionary<string,string> Overrides) TeamParts(object package)
    => ((Dictionary<string,string>)package.GetType().GetProperty("Saves")!.GetValue(package)!, (Dictionary<string,string>)package.GetType().GetProperty("Overrides")!.GetValue(package)!);
foreach (string policy in new[] { "SkipUnsupported", "ExportAnyway" }) Case("team " + policy, () => {
    var package = TeamParts(BuildTeam(policy)); var teamSidecar = new CampaignDocument(package.Overrides.Single(p => p.Key.EndsWith(".team.txt")).Value);
    var definitions = CustomRewardExport.Read(teamSidecar).ToArray();
    Check(definitions.Length == (policy == "SkipUnsupported" ? 2 : 4), policy + " applies to inherited talents and team relic snapshots");
    using var native = JsonDocument.Parse(package.Saves.Single(p => p.Key.StartsWith("CustomTeam-")).Value);
    var relicIds = native.RootElement.GetProperty("relics").EnumerateArray().Select(n => n.GetString()).ToArray();
    Check(relicIds.SequenceEqual(definitions.Where(d => d.IsRelic).Select(d => d.Id)), policy + " relic IDs agree with copied embedded definitions");
    foreach (var pair in package.Saves.Where(p => p.Key.StartsWith("CustomForward-"))) {
        using var model = JsonDocument.Parse(pair.Value); string id = model.RootElement.GetProperty("id").GetString()!;
        var sidecar = new CampaignDocument(package.Overrides[id + ".txt"]); var items = CustomRewardExport.Read(sidecar).ToArray();
        Check(items.Length == (policy == "SkipUnsupported" ? 1 : 2) && model.RootElement.GetProperty("talentIds").EnumerateArray().Select(v => v.GetString()).SequenceEqual(items.Select(d => d.Id)),
            policy + " inherited talent IDs and snapshots match each skater");
    }
});
Case("validation survives policy", () => {
    var wrong = new CampaignDocument(player.ToString()); wrong.Set("Goalie Talents", unsupported.Id);
    foreach (string policy in new[] { "SkipUnsupported", "ExportAnyway" })
        Reject(() => Call("PlayerExport", "Build", wrong, Data(), "goalie", Report(policy)), policy + " still rejects the wrong reward role");
    var incomplete = new CampaignDocument(team.ToString()); incomplete.Set("Team Name", "");
    Reject(() => BuildTeam("ExportAnyway", incomplete), "Export anyway still rejects an unnamed team");
});
Case("document-specific runtime opt in", () => {
    var doc = new CampaignDocument(""); CustomRewardExport.Embed(doc, [unsupported]);
    Reject(() => CustomRewardExport.ReadPlayNow(doc), "Runtime ingestion stays strict without the document marker");
    CustomRewardExport.EmbedPlayNow(doc, [unsupported], PlayNowRewardPolicy.ExportAnyway);
    Check(CustomRewardExport.ReadPlayNow(doc).Single().Id == unsupported.Id, "Explicit opt in permits the unchanged campaign rule at ingestion");
    doc.Set(CustomRewardExport.AllowCampaignRewardsKey, "true");
    Reject(() => CustomRewardExport.ReadPlayNow(doc), "An unrecognized opt-in value does not weaken runtime validation");
    var excluded = unsupported.Copy(); excluded.NeedsRedesign = true;
    CustomRewardExport.Embed(doc, [excluded]); doc.Set(CustomRewardExport.AllowCampaignRewardsKey, "yes");
    Reject(() => CustomRewardExport.ReadPlayNow(doc), "Opt in cannot load a redesign-excluded definition");
    var invalid = unsupported.Copy(); invalid.Threshold = 0;
    doc.Set(CustomRewardExport.Prefix + invalid.Id, Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(invalid.ToDocument().ToString())));
    Reject(() => CustomRewardExport.ReadPlayNow(doc), "Opt in cannot bypass embedded schema validation");
    CustomRewardExport.EmbedPlayNow(doc, [supported]);
    Check(doc.Get(CustomRewardExport.AllowCampaignRewardsKey).Length == 0, "A normal snapshot clears an inherited opt-in marker");
});
Case("prepared team choices", () => {
    var source = BuildTeam("ExportAnyway"); var originals = TeamParts(source);
    string originalPayload = JsonSerializer.Serialize(new { originals.Saves, originals.Overrides });
    var filtered = Call("PlayNowRewardExport", "ApplyTeam", source, PlayNowRewardPolicy.SkipUnsupported)!;
    var package = TeamParts(filtered);
    var sourceAfter = TeamParts(source);
    Check(JsonSerializer.Serialize(new { sourceAfter.Saves, sourceAfter.Overrides }) == originalPayload, "Applying Skip leaves the prepared Export anyway candidate untouched");
    Check(package.Saves.Keys.SequenceEqual(originals.Saves.Keys), "Applying Skip preserves all prepared player and team identities");
    foreach (var pair in package.Overrides) {
        var doc = new CampaignDocument(pair.Value); var items = CustomRewardExport.ReadPlayNow(doc).ToArray();
        Check(items.All(d => d.Name != unsupported.Name && d.Name != relic.Name) && doc.Get(CustomRewardExport.AllowCampaignRewardsKey).Length == 0,
            "Skip removes unsupported snapshots and opt in from every companion");
        if (!pair.Key.EndsWith(".team.txt")) {
            using var nested = JsonDocument.Parse(doc.Get("Export Model"));
            var talentIds = nested.RootElement.GetProperty("talentIds").EnumerateArray().Select(v => v.GetString()).ToArray();
            Check(talentIds.SequenceEqual(doc.Get("Export Talent IDs").Split(',', StringSplitOptions.RemoveEmptyEntries)), "Skip updates nested Export Model talent IDs too");
        }
    }
});
foreach (string choice in new[] { "SkipUnsupported", "ExportAnyway", "Cancel", "Close" }) Case("prepared player dialog " + choice, () => {
    var report = Report("ExportAnyway");
    var prepared = Call("PlayerExport", "Prepare", player, Data(), root, "skater", null, report)!;
    var before = Files(); string authored = player.ToString();
    var owner = new Window(); owner.Show(); Dispatcher.UIThread.RunJobs();
    try {
        var pending = (Task<PlayNowRewardPolicy?>)Call("PlayNowRewardExport", "Choose", owner, report)!;
        Dispatcher.UIThread.RunJobs(); var dialog = owner.OwnedWindows.Single();
        Check(dialog.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains(unsupported.Name) == true), "Warning dialog lists the authored unsupported name");
        Check(dialog.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("may not function in Play Now") == true), "Warning explains the retained campaign behavior limitation");
        Check(before.Count == Files().Count && before.All(p => Files()[p.Key] == p.Value), "Warning appears before any export file is written");
        if (choice == "SkipUnsupported") { using var frame = dialog.CaptureRenderedFrame(); frame!.Save(Path.Combine(Directory.GetParent(root)!.FullName, "reward-warning.png")); }
        if (choice == "Close") dialog.Close();
        else dialog.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PlayNowRewards" + choice).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Check(pending.IsCompletedSuccessfully && (choice is "Cancel" or "Close" ? pending.Result == null : pending.Result?.ToString() == choice), "Dialog returns the explicit " + choice + " decision");
        if (pending.Result is { } policy) {
            var parts = PlayerParts(prepared);
            Call("PlayNowRewardExport", "ApplyPlayer", parts.Model, parts.Sidecar, policy);
            Check(CustomRewardExport.ReadPlayNow(parts.Sidecar).Length == (policy == PlayNowRewardPolicy.SkipUnsupported ? 1 : 2), choice + " applies to the exact prepared player snapshot");
        }
        Check(before.Count == Files().Count && before.All(p => Files()[p.Key] == p.Value) && player.ToString() == authored, "Review and cancellation preserve all files and authored content");
    }
    finally { foreach (var dialog in owner.OwnedWindows.ToArray()) dialog.Close(); owner.Close(); }
});
Case("inherited goalie rules", () => {
    var goalieRule = new CustomRewardDefinition { Name = "Goalie campaign bonus", Kind = CustomRewardDefinition.CustomGoalieTalents,
        Trigger = "Shutout", Target = CustomRewardEffects.Team, Duration = "Rest of run", IconPng = Convert.ToBase64String(icon) };
    goalieRule.Validate(); goalieRule.Save(Path.Combine(root, "library", "rewards"));
    var mixed = new CampaignDocument(team.ToString()); mixed.Set("Team Talents", supported.Id + "," + goalieRule.Id);
    var report = Report("ExportAnyway");
    var package = TeamParts(Call("ExportTranslation", "BuildTeam", mixed, teamFile, Data(), root, report)!);
    var goalieModel = package.Saves.Single(p => p.Key.StartsWith("CustomGoalie-"));
    using var model = JsonDocument.Parse(goalieModel.Value);
    string id = model.RootElement.GetProperty("id").GetString()!;
    var goalieSidecar = new CampaignDocument(package.Overrides[id + ".txt"]);
    Check(CustomRewardExport.ReadPlayNow(goalieSidecar).Single().Name == goalieRule.Name, "Export anyway retains the inherited campaign-only goalie talent on the goalie");
    Check(package.Saves.Where(p => p.Key.StartsWith("CustomForward-")).All(p => {
        using var skater = JsonDocument.Parse(p.Value);
        var sidecar = new CampaignDocument(package.Overrides[skater.RootElement.GetProperty("id").GetString()! + ".txt"]);
        return CustomRewardExport.ReadPlayNow(sidecar).All(d => !d.IsGoalie);
    }), "Goalie-only team talents never leak onto skaters");
    var warnings = (IEnumerable<string>)report.GetType().GetProperty("Unsupported", Instance)!.GetValue(report)!;
    Check(warnings.Any(s => s.Contains(goalieRule.Name)), "The warning includes inherited goalie talent names");
});
Case("re-export opted-in source", () => {
    var first = PlayerParts(BuildPlayer("ExportAnyway")); string file = Path.Combine(root, "re-export-player.txt"); first.Sidecar.Save(file);
    var before = File.ReadAllBytes(file);
    var prepared = PlayerParts(Call("PlayerExport", "Prepare", first.Sidecar, Data(), root, "skater", file, Report("SkipUnsupported"))!);
    Check(CustomRewardExport.ReadPlayNow(prepared.Sidecar).Single().Name == supported.Name && prepared.Sidecar.Get(CustomRewardExport.AllowCampaignRewardsKey).Length == 0,
        "Re-exporting with Skip clears the source's earlier opt-in and unsupported snapshots");
    Check(File.ReadAllBytes(file).SequenceEqual(before), "Re-exporting leaves the opted-in source file untouched");
    prepared.Sidecar.Set(CustomRewardExport.AllowCampaignRewardsKey, "yes"); prepared.Sidecar.Save(file);
    var normalReport = Report();
    var normal = PlayerParts(Call("PlayerExport", "Prepare", prepared.Sidecar, Data(), root, "skater", file, normalReport)!);
    Check(normal.Sidecar.Get(CustomRewardExport.AllowCampaignRewardsKey).Length == 0 && CustomRewardExport.ReadPlayNow(normal.Sidecar).Length == 1,
        "Normal re-export clears a stale opt-in marker too");
});
Case("structural failures survive every policy", () => {
    var excluded = supported.Copy(); excluded.NeedsRedesign = true; excluded.Name = "Excluded item"; excluded.Id = Guid.NewGuid().ToString(); excluded.Save(Path.Combine(root, "library", "rewards"));
    var source = new CampaignDocument(player.ToString()); source.Set("Talents", excluded.Id);
    foreach (string policy in new[] { "SkipUnsupported", "ExportAnyway" })
        Reject(() => Call("PlayerExport", "Build", source, Data(), "skater", Report(policy)), policy + " cannot export a redesign-excluded assignment");
    string rosterFile = Path.Combine(teamFolder, "players", "0.txt"), original = File.ReadAllText(rosterFile);
    try {
        var invalidSlot = new CampaignDocument(original); invalidSlot.Set("Position", "Coach"); invalidSlot.Save(rosterFile);
        Reject(() => BuildTeam("ExportAnyway"), "Export anyway still rejects invalid roster slots");
        var wrongRole = new CampaignDocument(original); wrongRole.Set("Export Role", "goalie"); wrongRole.Save(rosterFile);
        Reject(() => BuildTeam("SkipUnsupported"), "Skip still rejects a roster role mismatch");
        File.Move(rosterFile, rosterFile + ".hold");
        Reject(() => BuildTeam("ExportAnyway"), "Export anyway still requires a complete starting roster");
    }
    finally { if (File.Exists(rosterFile + ".hold")) File.Move(rosterFile + ".hold", rosterFile); File.WriteAllText(rosterFile, original); }
});
Case("write prepared player and team copies", () => {
    string folder = Path.Combine(root, "written"), native = Path.Combine(folder, "native"), companions = Path.Combine(folder, "companions"), library = Path.Combine(folder, "workspace");
    Directory.CreateDirectory(native); Directory.CreateDirectory(companions);
    var originalFiles = Directory.GetFiles(teamFolder, "*", SearchOption.AllDirectories).ToDictionary(f => f, File.ReadAllBytes);
    var prepared = BuildTeam("ExportAnyway");
    string target = (string)Call("ExportTranslation", "Write", prepared, native, companions, library)!;
    var first = (System.Collections.IEnumerable)Call("ExportTranslation", "ExistingTeams", native, companions, library)!;
    object selection = first.Cast<object>().Single();
    var skipped = Call("PlayNowRewardExport", "ApplyTeam", BuildTeam("ExportAnyway"), PlayNowRewardPolicy.SkipUnsupported)!;
    string updated = (string)Call("ExportTranslation", "WriteUpdate", skipped, selection, native, companions, library)!;
    Check(target == updated && Directory.GetFiles(native, "CustomTeam-*.json").Length == 1, "Skip can update an existing opted-in team without creating a duplicate");
    foreach (string file in Directory.GetFiles(companions, "*.txt")) {
        var doc = new CampaignDocument(File.ReadAllText(file));
        Check(doc.Get(CustomRewardExport.AllowCampaignRewardsKey).Length == 0 && CustomRewardExport.ReadPlayNow(doc).All(d => d.Name != unsupported.Name && d.Name != relic.Name),
            "Updated runtime companions remove prior unsupported rules and opt in");
    }
    foreach (string file in Directory.GetFiles(library, "*.txt", SearchOption.AllDirectories)) {
        var doc = new CampaignDocument(File.ReadAllText(file));
        Check(doc.Get(CustomRewardExport.AllowCampaignRewardsKey).Length == 0 && CustomRewardExport.ReadPlayNow(doc).All(d => d.Name != unsupported.Name && d.Name != relic.Name),
            "Updated library snapshots also omit skipped definitions");
    }
    var p = PlayerParts(BuildPlayer("ExportAnyway")); Call("PlayNowRewardExport", "ApplyPlayer", p.Model, p.Sidecar, PlayNowRewardPolicy.SkipUnsupported);
    string playerTarget = Path.Combine(native, "standalone.json"), playerCompanion = Path.Combine(companions, "standalone.txt"), playerSnapshot = Path.Combine(library, "standalone.txt");
    Call("PlayerExport", "Write", p.Model, p.Sidecar, playerTarget, playerCompanion, playerSnapshot);
    using var saved = JsonDocument.Parse(File.ReadAllText(playerTarget)); using var nested = JsonDocument.Parse(new CampaignDocument(File.ReadAllText(playerCompanion)).Get("Export Model"));
    Check(saved.RootElement.GetProperty("talentIds").EnumerateArray().Select(n => n.GetString()).SequenceEqual(nested.RootElement.GetProperty("talentIds").EnumerateArray().Select(n => n.GetString())),
        "Written standalone model and nested companion retain the same talent selection");
    Check(File.ReadAllBytes(playerCompanion).SequenceEqual(File.ReadAllBytes(playerSnapshot)), "Written standalone companion and library snapshot match");
    Check(originalFiles.All(p => File.ReadAllBytes(p.Key).SequenceEqual(p.Value)), "New and update writes preserve every authored team and roster file");
});
Check(originals.All(p => Files().TryGetValue(p.Key, out var hash) && hash == p.Value), "Preparation preserves original definitions and authored player values");
Console.WriteLine($"{passed}/{total} Play Now reward export checks passed.");
return passed == total ? 0 : 1;
