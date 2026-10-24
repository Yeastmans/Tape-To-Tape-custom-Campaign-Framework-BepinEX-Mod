using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CampaignCore;
using CampaignCreator;
using System.Reflection;

int checks = 0;
void Check(bool ok, string why) { checks++; if (!ok) throw new Exception(why); }
AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
string root = Path.GetFullPath(Path.Combine(args[0], "recovery-" + Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(root);
var main = new MainWindow(["--content", root]);
// Keep this real editor check local: no startup network, extraction or install flow.
typeof(MainWindow).GetField("releaseCheck", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(main, Task.FromResult(false));
main.Show();
void Pump() { for (int i = 0; i < 4; i++) { Dispatcher.UIThread.RunJobs(); main.UpdateLayout(); Thread.Sleep(10); } }
Pump();
string campaign = Path.Combine(root, "campaigns", "Recovery"), a = Path.Combine(campaign, "player_teams", "a"), b = Path.Combine(campaign, "player_teams", "b");
Directory.CreateDirectory(Path.Combine(campaign, "map_layouts"));
File.WriteAllText(Path.Combine(campaign, "campaign.txt"), "Name = Recovery\nAct Sequence = 1\nUse Player Teams = yes\n");
string TeamFile(string squad) => Path.Combine(squad, "team.txt");
foreach (string squad in new[] { a, b }) {
    Directory.CreateDirectory(squad);
    File.WriteAllText(TeamFile(squad), "Team Name = Squad " + Path.GetFileName(squad) + "\nCity = Test\nAbbreviation = TST\nBody = standard\nGloves = standard\nPants = standard\nSquad Template = No Effects\nUse Own Maps = yes\n");
}
var layout = new MapLayout { BaseSquad = "Basic Squad", LookAct = 1, Nodes = [
    new() { Id = "coach", Type = "Coach", X = 3.1, Y = 10.4, Outgoing = ["boss"] },
    new() { Id = "boss", Type = "Boss", Layer = 1, X = 9.5, Y = 10.4 }
] };
MapNodePlacement.ArrangeLayers(layout);
File.WriteAllText(Path.Combine(campaign, "map_layouts", "map-1.txt"), layout.Serialize());
layout.Nodes[1].Team = "x";
foreach (string squad in new[] { a, b }) {
    Directory.CreateDirectory(Path.Combine(squad, SquadMapOverrides.Folder));
    File.WriteAllText(SquadMapOverrides.OverridePath(squad, 1), layout.Serialize());
}
string opponent = Path.Combine(campaign, "teams", "x", "team.txt");
Directory.CreateDirectory(Path.GetDirectoryName(opponent)!); File.WriteAllText(opponent, "Team Name = Opponent X\n");
Check(!CampaignRewardBudget.Calculate(root, campaign).SelectMany(budget => budget.Errors).Any(), "Both saved overrides initially validate");
File.Move(opponent, Path.Combine(root, "removed-opponent.txt"));
Check(CampaignRewardBudget.Calculate(root, campaign).SelectMany(budget => budget.Errors).Any(e => e.Contains("assign a campaign team")), "Removing the assigned opponent invalidates both overrides");
void OpenTeam() {
    typeof(MainWindow).GetMethod("OpenFile", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, [TeamFile(a)]); Pump();
    var tabs = main.GetVisualDescendants().OfType<TabControl>().First();
    tabs.SelectedItem = tabs.Items.Cast<TabItem>().Single(tab => Equals(tab.Header, "Maps")); Pump();
}
Button Launch() => main.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "EditTeamMaps");
CheckBox Toggle() => main.GetVisualDescendants().OfType<CheckBox>().Single(toggle => toggle.Name == "UseOwnMaps");
void Click(Button button) { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(); }
MapEditorWindow MapWindow() => main.OwnedWindows.OfType<MapEditorWindow>().Single();
void ResetAndSave() {
    var window = MapWindow(); var view = (MapEditorView)window.Content!;
    Click(view.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "ResetSquadMap"));
    Check(view.TrySaveMap(out string error), "Repairing A must save despite B's invalid override: " + error);
    window.Close(); Pump();
}
void SaveTeam() { Click(main.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Save player team"))); }
OpenTeam(); string unchanged = File.ReadAllText(TeamFile(a));
Click(Launch());
string LaunchMessages() => string.Join(" | ", main.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).Where(t => !string.IsNullOrWhiteSpace(t)))
    + " | " + ((TextBlock)typeof(MainWindow).GetField("status", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(main)!).Text;
Check(main.OwnedWindows.OfType<MapEditorWindow>().Count() == 1, "An unchanged saved team must open its invalid override for repair through the real save/launch integration: " + LaunchMessages());
Check(File.ReadAllText(TeamFile(a)) == unchanged, "Opening an unchanged team performs no team write");
ResetAndSave();
Check(!File.Exists(SquadMapOverrides.OverridePath(a, 1)) && File.Exists(SquadMapOverrides.OverridePath(b, 1)), "A's repair keeps B's independent map untouched");
// Retained disabled overrides must be repairable before activation validation.
File.WriteAllText(SquadMapOverrides.OverridePath(a, 1), layout.Serialize()); OpenTeam();
Toggle().IsChecked = false; Pump(); SaveTeam();
Check(!SquadMapOverrides.Own(new CampaignDocument(File.ReadAllText(TeamFile(a)))), "Disabling A saves even while another squad has an invalid override");
OpenTeam(); Check(Launch().IsVisible && Equals(Launch().Content, "Edit saved team maps"), "Disabled retained maps expose their repair entry point");
Toggle().IsChecked = true; Pump(); Click(Launch());
Check(!main.OwnedWindows.OfType<MapEditorWindow>().Any() && !SquadMapOverrides.Own(new CampaignDocument(File.ReadAllText(TeamFile(a)))), "Failed activation validation blocks opening and leaves the saved disabled flag intact");
Toggle().IsChecked = false; Pump(); Click(Launch());
Check(main.OwnedWindows.OfType<MapEditorWindow>().Count() == 1 && MapWindow().CurrentLayout!.Nodes.Single(n => n.Id == "boss").Team == "x", "Returning to the saved disabled setting opens the retained invalid map");
ResetAndSave();
Check(!SquadMapOverrides.Own(new CampaignDocument(File.ReadAllText(TeamFile(a)))) && !File.Exists(SquadMapOverrides.OverridePath(a, 1)), "Repairing disabled maps preserves their disabled setting");
Toggle().IsChecked = true; Pump(); Click(Launch());
Check(main.OwnedWindows.OfType<MapEditorWindow>().Count() == 1 && SquadMapOverrides.Own(new CampaignDocument(File.ReadAllText(TeamFile(a)))), "A's repaired maps can activate despite B's invalid map");
MapWindow().Close(); Pump(); OpenTeam();
// Change actual registered fields so launch must run the real required-field save path.
var readers = (Dictionary<string, Func<string>>)typeof(MainWindow).GetField("editors", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(main)!;
readers["City"] = () => "";
typeof(MainWindow).GetMethod("MarkDirty", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, null); Click(Launch());
Check(!main.OwnedWindows.OfType<MapEditorWindow>().Any() && new CampaignDocument(File.ReadAllText(TeamFile(a))).Get("City") == "Test", "Invalid pending team edits block launch without overwriting the saved team");
readers["City"] = () => "Test"; readers["Team Name"] = () => "Saved repaired team";
typeof(MainWindow).GetMethod("MarkDirty", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, null); Click(Launch());
Check(main.OwnedWindows.OfType<MapEditorWindow>().Count() == 1 && MapWindow().Title == "Team maps · Saved repaired team", "Valid pending team edits save before opening even when B remains invalid");
Check(new CampaignDocument(File.ReadAllText(TeamFile(a))).Get("Team Name") == "Saved repaired team", "The pending team name is persisted by the normal save action");
MapWindow().Close(); Pump(); main.Close();
Console.WriteLine($"PASS {checks} squad map recovery checks; evidence: {root}");
