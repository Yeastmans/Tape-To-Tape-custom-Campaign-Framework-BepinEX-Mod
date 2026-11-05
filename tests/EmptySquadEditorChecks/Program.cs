using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CampaignCore;
using CampaignCreator;

string output = Path.GetFullPath(args[0]);
Directory.CreateDirectory(output);
string root = Path.Combine(output, "fixture-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var assembly = typeof(HeadProject).Assembly;
var dataType = assembly.GetType("CampaignCreator.GameData")!;
var teamType = assembly.GetType("CampaignCreator.TeamEditorView")!;
assembly.GetType("CampaignCreator.StudioArt")!.GetMethod("Use")!.Invoke(null, [null]);
AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
int checks = 0, failures = 0;
void Check(bool pass, string label) { checks++; if (!pass) failures++; Console.WriteLine((pass ? "PASS " : "FAIL ") + label); }
void Save(string file, string text) { Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.WriteAllText(file, text); }
void Pump(Window owner) { Dispatcher.UIThread.RunJobs(); owner.UpdateLayout(); }
(string file, string text) Player(string position, string name) => (position + ".txt", $"Name = {name}\nPosition = {position}\n");
var goalie = Player("Goalie", "Goalkeeper");
string[] skaterPositions = ["Left Wing", "Center", "Right Wing", "Left Defense", "Right Defense"];
var complete = skaterPositions.Concat(skaterPositions.Select(p => "Line 2 " + p)).Select((p, i) => Player(p, "Skater " + i)).Append(goalie).ToArray();

(Window owner, Control editor, string file) Open(string label, string template, params (string file, string text)[] players)
{
    string campaign = Path.Combine(root, label);
    Save(Path.Combine(campaign, "campaign.txt"), "Act Sequence = 1\nSquad Template = " + template + "\n");
    string file = Path.Combine(campaign, "player_teams", "Team", "team.txt");
    Save(file, "Team Name = " + label + "\nSquad Template = " + template + "\nSquad Behavior two_lines = on\nStarting Relics Mode = Custom\n");
    foreach (var player in players) Save(Path.Combine(Path.GetDirectoryName(file)!, "players", player.file), player.text);
    var data = Activator.CreateInstance(dataType, [root])!;
    dataType.GetMethod("LoadCustomRewards", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(data, [file]);
    var owner = new Window { Width = 1320, Height = 960 };
    var editor = (Control)teamType.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single().Invoke([
        owner, new CampaignDocument(File.ReadAllText(file)), data, root, file,
        (Action)(() => { }), (Action<string, Func<string>>)((_, _) => { }), (Action)(() => { }), (Action)(() => { }), (Action)(() => { }),
        (Action<string>)(_ => { }), (Action<string, bool>)((_, _) => { }), (Action)(() => { }), new TextBlock(), null, false, null, null]);
    owner.Content = editor; owner.Show(); Pump(owner);
    var tabs = editor.GetVisualDescendants().OfType<TabControl>().Single(t => t.Items.OfType<TabItem>().Any(x => x.Header?.ToString() == "Team roster"));
    tabs.SelectedItem = tabs.Items.OfType<TabItem>().Single(t => t.Header?.ToString() == "Team roster"); Pump(owner);
    return (owner, editor, file);
}
string? SaveError(Control editor)
{
    try { teamType.GetMethod("SaveEquipment", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(editor, null); return null; }
    catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException) { return ex.InnerException.Message; }
}
string Problems(Control editor) => editor.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "TwoLineRosterProblems").Text ?? "";
void Case(string label, string template, bool accepted, (string file, string text)[] players, string? reason = null)
{
    var view = Open(label, template, players);
    try {
        string problems = Problems(view.editor);
        string? error = SaveError(view.editor);
        Check(accepted ? error == null && problems.Length == 0 : error != null && (reason == null || error.Contains(reason)), label + (accepted ? " saves with no missing-roster warning" : " is rejected") + (error == null ? "" : ": " + error.Split('\n')[0]));
        if (label is "Empty No Effects" or "Goalie-only No Effects") {
            using var frame = view.owner.CaptureRenderedFrame(); frame!.Save(Path.Combine(output, label.Replace(' ', '-') + ".png"));
        }
    } finally { view.owner.Close(); Dispatcher.UIThread.RunJobs(); }
}

// Missing-slot validation must distinguish an absent skater roster from malformed authored entries.
Case("Empty No Effects", "No Effects", true, []);
Case("Goalie-only No Effects", "No Effects", true, [goalie]);
Case("Duplicate goalie entries", "No Effects", false, [goalie, ("extra-goalie.txt", "Name = Other Goalie\nPosition = Goalie\n")], "Fill these Team roster slots");
Case("No Effects identity alias", "No Effects Squad", true, []);
Case("First-line partial", "No Effects", false, [Player("Center", "One Skater"), goalie], "Fill these Team roster slots");
Case("Second-line partial", "No Effects", false, [("second-line/Center.txt", "Name = Second-line Skater\n"), goalie], "Fill these Team roster slots");
Case("Unnamed first-line skater", "No Effects", false, [Player("Center", ""), goalie], "Fill these Team roster slots");
Case("Unnamed second-line skater", "No Effects", false, [Player("Line 2 Center", ""), goalie], "Fill these Team roster slots");
Case("Malformed skater entry", "No Effects", false, [("mystery.txt", "Overall = 50\n"), goalie], "Fill these Team roster slots");
Case("Ten skaters without goalie", "No Effects", false, complete.Where(p => p.file != "Goalie.txt").ToArray(), "Goalie");
Case("Complete two lines", "No Effects", true, complete);
var duplicateNames = complete.Select((p, i) => i == 1 ? (p.file, p.text.Replace("Skater 1", "Skater 0")) : p).ToArray();
Case("Duplicate player names", "No Effects", false, duplicateNames, "must have unique names");
Case("Duplicate assigned slot", "No Effects", false, complete.Append(("extra.txt", "Name = Extra Skater\nPosition = Center\n")).ToArray(), "exactly one player");
Case("Empty Basic Squad", "Basic Squad", false, [], "Fill these Team roster slots");
Case("Goalie-only Basic Squad", "Basic Squad", false, [goalie], "Fill these Team roster slots");
Case("Empty Double Decker", "Double Decker", false, [], "Fill these Team roster slots");

// Pending removals count immediately, even while the player files remain on disk until save.
var removal = Open("Remove final skaters", "No Effects", Player("Center", "First Skater"), Player("Line 2 Center", "Second Skater"), goalie);
try {
    void Click(Control parent, Func<Button, bool> match) {
        parent.GetVisualDescendants().OfType<Button>().Single(match).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(removal.owner);
    }
    void Remove(string name) {
        Click(removal.editor, b => ToolTip.GetTip(b) as string == "Remove " + name + " from roster");
        var dialog = removal.owner.OwnedWindows.Single();
        Click(dialog, b => b.Content?.ToString() == "Remove player"); Pump(removal.owner);
    }
    Remove("First Skater");
    var validate = (Action)teamType.GetField("validateStartingRoster", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(removal.editor)!;
    bool rejected = false; try { validate(); } catch (InvalidOperationException) { rejected = true; }
    Check(rejected, "Removing only the first-line skater still rejects the remaining partial second line");
    Click(removal.editor, b => b.Content?.ToString() == "LINE 2");
    Remove("Second Skater");
    string playerFolder = Path.Combine(Path.GetDirectoryName(removal.file)!, "players");
    Check(File.Exists(Path.Combine(playerFolder, "Center.txt")) && File.Exists(Path.Combine(playerFolder, "Line 2 Center.txt")), "UI removal stays pending before saving");
    string? error = SaveError(removal.editor);
    Check(error == null && Problems(removal.editor).Length == 0, "Removing every skater allows the goalie-only squad to save");
    Check(error == null && !File.Exists(Path.Combine(playerFolder, "Center.txt")) && !File.Exists(Path.Combine(playerFolder, "Line 2 Center.txt")) && File.Exists(Path.Combine(playerFolder, "Goalie.txt")), "Saving empty-start removal applies only the removed skaters");
    foreach (int line in new[] { 1, 2 }) {
        Click(removal.editor, b => b.Content?.ToString() == "LINE " + line);
        var text = removal.editor.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToArray();
        Check(!text.Contains("First Skater") && !text.Contains("Second Skater"), "Saved removals do not return as ghost cards on line " + line);
        Check(text.Contains(line == 1 ? "1 of 6 filled" : "0 of 5 filled") && text.Any(t => t.StartsWith("10 empty skater slots")), "Saved line " + line + " reports the remaining goalie and ten empty skater slots");
    }
    using var savedFrame = removal.owner.CaptureRenderedFrame(); savedFrame!.Save(Path.Combine(output, "post-save-line-two.png"));
} finally { removal.owner.Close(); Dispatcher.UIThread.RunJobs(); }
// Extra goalie rows are cached separately from the regular slot cards.
var overflow = Open("Remove overflow goalie", "No Effects", goalie, ("zz-extra-goalie.txt", "Name = Backup Goalie\nPosition = Goalie\n"));
try {
    void Click(Control parent, Func<Button, bool> match) {
        parent.GetVisualDescendants().OfType<Button>().Single(match).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(overflow.owner);
    }
    Click(overflow.editor, b => b.Content?.ToString() == "LINE 2");
    string extraFile = Path.Combine(Path.GetDirectoryName(overflow.file)!, "players", "zz-extra-goalie.txt");
    var row = overflow.editor.GetVisualDescendants().OfType<Grid>().Single(g => g.Tag as string == extraFile);
    Check(row.IsVisible, "Extra goalie is represented by a visible overflow row before removal");
    Click(overflow.editor, b => ToolTip.GetTip(b) as string == "Remove Backup Goalie from roster");
    Click(overflow.owner.OwnedWindows.Single(), b => b.Content?.ToString() == "Remove player"); Pump(overflow.owner);
    Check(SaveError(overflow.editor) == null && !File.Exists(extraFile), "Removing the extra goalie saves the remaining valid goalie-only squad");
    Click(overflow.editor, b => b.Content?.ToString() == "LINE 1");
    Click(overflow.editor, b => b.Content?.ToString() == "LINE 2");
    Check(!row.IsVisible, "Saved overflow removal stays hidden after switching roster lines");
} finally { overflow.owner.Close(); Dispatcher.UIThread.RunJobs(); }
Console.WriteLine($"{checks - failures}/{checks} empty squad editor checks passed");
return failures == 0 ? 0 : 1;
