using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CampaignCreator;

AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
string root = Path.Combine(Path.GetFullPath(args[0]), Guid.NewGuid().ToString("N"));
string Write(string relative, string text)
{
    string path = Path.Combine(root, relative);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, text);
    return path;
}
void Check(bool value, string label)
{
    if (!value) throw new InvalidOperationException(label);
    Console.WriteLine("PASS " + label);
}
void PumpUntil(Func<bool> done)
{
    var timer = System.Diagnostics.Stopwatch.StartNew();
    while (!done() && timer.Elapsed < TimeSpan.FromSeconds(10))
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Thread.Sleep(1);
    }
    if (!done()) throw new TimeoutException("Studio did not reach the expected UI state.");
}
T Find<T>(Window window, string name) where T : Control =>
    window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

Write("campaigns/Season/campaign.txt", "Name = Season\n");
Write("library/players/Skater.txt", "Name = Library skater\nPosition = Center\n");
Write("library/players/Netminder.txt", "Import Player = Native Netminder\nCatching = 42\n");
string active = Write("active.txt", "# custom selection\nActive Campaign = Season\n");
string squad = Write("campaigns/Season/player_teams/Home/team.txt", "Team Name = Home\n");
string unused = Write("campaigns/Season/teams/Visitors/players/Roster.txt", "Name = Visiting player\n");
using var lockedSquad = File.Open(squad, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
using var lockedRoster = File.Open(unused, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
var main = new MainWindow(["--content", root]);
typeof(MainWindow).GetField("releaseCheck", instance)!.SetValue(main, Task.FromResult(false));
main.Show();
Dispatcher.UIThread.RunJobs();
Check(Find<Button>(main, "HomeEditTeams").IsVisible, "Home can offer a saved player team without reading its settings or unrelated rosters");
Check(Find<Button>(main, "HomeEditPlayers").IsVisible, "a library skater is editable");
Check(Find<ComboBox>(main, "HomeSelectCampaign").SelectedItem?.ToString() == "Season"
    && File.ReadAllText(active).StartsWith("# custom selection", StringComparison.Ordinal), "opening Home keeps the existing active selection file intact");
Click(Find<Button>(main, "HomeTypePlayersgoalie"));
PumpUntil(() => Find<Button>(main, "HomeEditPlayers").IsVisible);
Check(Find<Button>(main, "HomeEditPlayers").IsVisible, "legacy goalie fields qualify a library player for goalie editing");
lockedSquad.Dispose();
Click(Find<Button>(main, "HomeEditTeams"));
PumpUntil(() => main.OwnedWindows.Any());
var playerTeams = main.OwnedWindows.Single();
Check(Find<ComboBox>(playerTeams, "HomePlayerTeamPicker").SelectedItem?.ToString() == "Home · Custom squad · Season",
    "the requested player-team list reads team names while an unrelated roster remains locked");
playerTeams.Close();
main.Close();

root = Path.Combine(Path.GetFullPath(args[0]), Guid.NewGuid().ToString("N"));
Write("campaigns/Season/campaign.txt", "Name = Season\n");
Write("campaigns/Season/player_teams/free_agents/team.txt", "Team Name = Reserved\n");
Write("campaigns/Season/player_teams/_removed_entries/.studio-removal-archive", "");
Write("campaigns/Season/player_teams/_removed_entries/team.txt", "Team Name = Removed\n");
Write("campaigns/Season/free_agents/_removed_entries/.studio-removal-archive", "");
Write("campaigns/Season/free_agents/_removed_entries/skater.txt", "Name = Removed skater\n");
Write("library/players/_removed_entries/.studio-removal-archive", "");
Write("library/players/_removed_entries/old.txt", "Name = Former skater\n");
Write("campaigns/Season/rewards/other.txt", "Name = An unrelated reward\n");
main = new MainWindow(["--content", root]);
typeof(MainWindow).GetField("releaseCheck", instance)!.SetValue(main, Task.FromResult(false));
main.Show(); Dispatcher.UIThread.RunJobs();
Check(!Find<Button>(main, "HomeEditTeams").IsVisible && !Find<Button>(main, "HomeEditPlayers").IsVisible,
    "archived content, reserved pool folders and unrelated files do not make editor choices appear");
Write("library/player_teams/legacy.txt", "Team Name = Earlier imported team\n");
main.ShowHome(); Dispatcher.UIThread.RunJobs();
Check(Find<Button>(main, "HomeEditTeams").IsVisible, "returning Home finds a legacy library team with a custom filename");
main.Close();

var noticeType = typeof(App).Assembly.GetType("CampaignCreator.BetaNoticeWindow", true)!;
Window Notice() => (Window)Activator.CreateInstance(noticeType, nonPublic: true)!;
void OnContinue(Window w, Func<CancellationToken, Task> action) =>
    noticeType.GetEvent("ContinueRequested", instance)!.GetAddMethod(true)!.Invoke(w, [action]);
var notice = Notice();
int attempts = 0;
var disk = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
CancellationToken startupToken = default;
OnContinue(notice, token =>
{
    startupToken = token;
    if (++attempts == 1) return disk.Task.WaitAsync(token);
    return Task.Delay(Timeout.Infinite, token);
});
notice.Show(); Dispatcher.UIThread.RunJobs();
var open = Find<Button>(notice, "BetaNoticeContinue");
Check(attempts == 0 && !notice.GetVisualDescendants().OfType<ProgressBar>().Any(p => p.IsEffectivelyVisible),
    "opening the notice starts no preparation before consent");
Click(open);
Check(!open.IsEnabled && notice.GetVisualDescendants().OfType<ProgressBar>().Any(p => p.IsEffectivelyVisible
    && p.IsIndeterminate && !string.IsNullOrWhiteSpace(AutomationProperties.GetName(p))), "acknowledging immediately shows named indeterminate progress");
Click(open);
PumpUntil(() => attempts == 1);
Check(attempts == 1, "repeated acknowledgement starts one preparation");
disk.SetException(new IOException("fixture unavailable"));
PumpUntil(() => open.IsEnabled);
Check(Find<TextBlock>(notice, "StudioStartupError").Text?.Contains("fixture unavailable") == true
    && !notice.GetVisualDescendants().OfType<ProgressBar>().Any(p => p.IsEffectivelyVisible),
    "a preparation error is visible and releases the button for retry");
Click(open);
PumpUntil(() => attempts == 2);
Check(attempts == 2 && !open.IsEnabled, "retry starts a fresh preparation");
notice.Close();
Check(startupToken.IsCancellationRequested, "closing the notice cancels the pending handover");
Dispatcher.UIThread.RunJobs();
