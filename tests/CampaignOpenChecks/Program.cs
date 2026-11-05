using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CampaignCore;
using CampaignCreator;

AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting();
using var fixture = new TestContent();
string root = fixture.Folder.FullName;
string campaign = Path.Combine(root, "campaigns", "Test Cup");
string team = Path.Combine(campaign, "player_teams", "Storm");
string players = Path.Combine(team, "players");
Directory.CreateDirectory(players);
string settings = Path.Combine(campaign, "campaign.txt"), player = Path.Combine(players, "LW.txt");
File.WriteAllText(settings, "Act Sequence = 1\nOffside = no\n");
File.WriteAllText(Path.Combine(team, "team.txt"), "Team Name = The Storm\nSquad Template = Basic Squad\n");
File.WriteAllText(player, "Name = Left Wing\nPosition = Left Wing\n");
var window = new MainWindow(["--content", root]);
typeof(MainWindow).GetField("releaseCheck", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
    .SetValue(window, Task.FromResult(false));
window.Show();
Dispatcher.UIThread.RunJobs();
var home = window.Content;
Window Picker()
{
    Click(window, "HomeEditCampaigns");
    PumpUntil(() => window.OwnedWindows.Any());
    return window.OwnedWindows.Single();
}
var picker = Picker();
var open = Button(picker, "Open campaign");
open.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
Check(picker.GetVisualDescendants().OfType<ProgressBar>().Any(bar => bar.IsEffectivelyVisible && bar.IsIndeterminate)
    && picker.GetVisualDescendants().OfType<TextBlock>().Any(t => t.IsEffectivelyVisible && t.Text?.Contains("Test Cup") == true
        && t.Text.Contains("Opening", StringComparison.OrdinalIgnoreCase)), "Open immediately displays progress naming the selected campaign");
Check(!open.IsEnabled, "duplicate open is disabled while loading");
open.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
Check(picker.GetVisualDescendants().OfType<ProgressBar>().Count(bar => bar.IsEffectivelyVisible) == 1,
    "a repeated Open event starts only one campaign handoff");
Click(picker, "Cancel");
PumpUntil(() => !window.OwnedWindows.Any());
Check(ReferenceEquals(window.Content, home), "cancelling keeps the user on Home");

picker = Picker();
// Reading this player body belongs to the player editor. Squad count needs only the filename.
using (var locked = new FileStream(player, FileMode.Open, FileAccess.Read, FileShare.None))
{
    Click(picker, "Open campaign");
    PumpUntil(() => window.Content is CampaignWorkspace && !window.OwnedWindows.Any());
    Check(window.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == "The Storm")
        && window.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == "1 player"),
        "overview shows squad name and size without reading the roster entry");
}
var workspace = (CampaignWorkspace)window.Content!;
Check(!workspace.HasUnsavedChanges, "opening an existing campaign starts clean");
var offside = window.GetVisualDescendants().OfType<CheckBox>().Single(box => box.Name == "CampaignOffside");
offside.IsChecked = true;
Click(window, "CampaignOverviewSection3");
Dispatcher.UIThread.RunJobs();
Check(workspace.HasUnsavedChanges, "opening another section preserves an unsaved overview edit");
var useTeams = window.GetVisualDescendants().OfType<CheckBox>().Single(box => box.Name == "UsePlayerTeams");
useTeams.IsChecked = false;
Click(window, "BackToCampaign");
Dispatcher.UIThread.RunJobs();
Click(window, "CampaignOverviewSection3");
Dispatcher.UIThread.RunJobs();
Check(window.GetVisualDescendants().OfType<CheckBox>().Single(box => box.Name == "UsePlayerTeams").IsChecked == false,
    "revisiting a section preserves its own unsaved edit");
Click(window, "Save campaign");
Check(new CampaignDocument(File.ReadAllText(settings)).Get("Offside") == "yes"
    && new CampaignDocument(File.ReadAllText(settings)).Get("Use Player Teams") == "no", "saving keeps edits from both sections");
Check(!workspace.HasUnsavedChanges, "saving resets the edit baseline");
Click(window, "BackToCampaign");
Dispatcher.UIThread.RunJobs();
Click(window, "BackToCampaign");
Dispatcher.UIThread.RunJobs();
picker = Picker();
using (var locked = new FileStream(settings, FileMode.Open, FileAccess.Read, FileShare.None))
{
    Click(picker, "Open campaign");
    PumpUntil(() => Button(picker, "Open campaign").IsEnabled && picker.GetVisualDescendants().OfType<TextBlock>()
        .Any(t => t.IsEffectivelyVisible && t.Text?.Contains("campaign.txt", StringComparison.OrdinalIgnoreCase) == true));
    Check(window.Content is not CampaignWorkspace, "failed open preserves Home and allows retry");
}
Click(picker, "Open campaign");
PumpUntil(() => window.Content is CampaignWorkspace && !window.OwnedWindows.Any());
Check(true, "retry opens the repaired campaign");
window.Close();

// Saving from the overview must still run the existing defaults and validation of unopened pages.
var legacy = Path.Combine(root, "campaigns", "Legacy");
string bench = Path.Combine(legacy, "player_teams", "draft_pool");
string stars = Path.Combine(legacy, "player_teams", "superstars");
Directory.CreateDirectory(bench);
Directory.CreateDirectory(stars);
File.WriteAllText(Path.Combine(bench, "Center.txt"), "Name = Legacy center\nPosition = Center\n");
File.WriteAllText(Path.Combine(stars, "Star.txt"), "Name = Legacy star\nPosition = Left Wing\n");
string legacyFile = Path.Combine(legacy, "campaign.txt");
const string header = "# Earlier campaign\nAct Sequence = 1\nOffside = true\nAuthor Setting = keep\n";
File.WriteAllText(legacyFile, header);
var owner = new Window();
var unopened = new CampaignWorkspace(owner, root, legacy, () => { }, _ => { }, () => Task.CompletedTask);
owner.Content = unopened; owner.Show(); Dispatcher.UIThread.RunJobs();
Check(!unopened.HasUnsavedChanges && File.ReadAllText(legacyFile) == header && !File.Exists(Path.Combine(bench, SelectedPlayerPool.Manifest)),
    "opening a legacy overview leaves its config and unconfigured pools untouched");
Click(owner, "Save campaign");
var saved = new CampaignDocument(File.ReadAllText(legacyFile));
Check(saved.Get("Use Player Teams") == "yes" && saved.Get(GoalieFeatureOptions.RecruitmentKey) == "Off"
    && saved.Get("Offside") == "true" && saved.Get("Author Setting") == "keep" && !unopened.HasUnsavedChanges,
    "overview-only save applies the existing team and goalie defaults and preserves authored fields");
Check(SelectedPlayerPool.Read(bench)!.Select(Path.GetFileName).SequenceEqual(["Center.txt"])
    && SelectedPlayerPool.Read(stars)!.Select(Path.GetFileName).SequenceEqual(["Star.txt"]),
    "overview-only save retains legacy bench and superstar choices in their new manifests");
string goalies = GoalieFreeAgents.DirectoryPath(legacy);
Directory.CreateDirectory(goalies);
string goaliePicks = Path.Combine(goalies, SelectedPlayerPool.Manifest);
File.WriteAllText(goaliePicks, "Unavailable.txt\n");
Click(owner, "CampaignOverviewSection4");
Dispatcher.UIThread.RunJobs();
Check(!unopened.HasUnsavedChanges && owner.GetVisualDescendants().OfType<TextBlock>()
    .Any(t => t.Text?.Contains("selected goalie free agent is missing") == true),
    "opening a damaged section reports the failed read and keeps the clean overview");
File.WriteAllText(goaliePicks, "");
Click(owner, "CampaignOverviewSection4");
Dispatcher.UIThread.RunJobs();
Check(!unopened.HasUnsavedChanges && owner.GetVisualDescendants().OfType<CheckBox>().Any(box => box.Name == "GoalieRecruitment"),
    "retry opens the repaired section with a clean baseline");
owner.Close();

const string invalidWeights = header + "Free Agent Talent Multipliers = invalid\n";
File.WriteAllText(legacyFile, invalidWeights);
owner = new Window();
unopened = new CampaignWorkspace(owner, root, legacy, () => { }, _ => { }, () => Task.CompletedTask);
owner.Content = unopened; owner.Show(); Dispatcher.UIThread.RunJobs();
owner.GetVisualDescendants().OfType<CheckBox>().Single(box => box.Name == "CampaignOffside").IsChecked = false;
Click(owner, "Save campaign");
Check(unopened.HasUnsavedChanges && File.ReadAllText(legacyFile) == invalidWeights
    && owner.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.StartsWith("Free-agent talent weights:") == true),
    "unvisited free-agent settings still block an invalid save and preserve pending edits");
owner.Close();

static Button Button(Control parent, string identifier) => parent.GetVisualDescendants().OfType<Button>()
    .Single(b => b.Name == identifier || b.Content as string == identifier);
static void Click(Control parent, string identifier) => Button(parent, identifier).RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
static void Check(bool success, string description)
{
    if (!success) throw new Exception(description);
    Console.WriteLine("PASS " + description);
}
static void PumpUntil(Func<bool> condition)
{
    var timer = Stopwatch.StartNew();
    while (!condition() && timer.Elapsed < TimeSpan.FromSeconds(30))
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Thread.Sleep(5);
    }
    if (!condition()) throw new TimeoutException("Expected Studio UI did not appear.");
}
sealed class TestContent : IDisposable
{
    internal DirectoryInfo Folder { get; } = Directory.CreateTempSubdirectory("studio-campaign-open-");
    public void Dispose() => Folder.Delete(true);
}
