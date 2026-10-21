using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CampaignCore;
using CampaignCreator;
using System.Reflection;

AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
int checks = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks++; Console.WriteLine("PASS " + name); }
void Pump() { for (int i = 0; i < 4; i++) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(20); } }
void Click(Control parent, string label) { parent.GetVisualDescendants().OfType<Button>().Single(b => b.Content?.ToString() == label).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(); }
var root = Path.Combine(Path.GetTempPath(), "goalie-feature-editor-" + Guid.NewGuid().ToString("N"));
var campaign = Path.Combine(root, "campaigns", "Feature check");
Directory.CreateDirectory(Path.Combine(campaign, "map_layouts"));
string configFile = Path.Combine(campaign, "campaign.txt"), mapFile = Path.Combine(campaign, "map_layouts", "map-1.txt");
File.WriteAllText(configFile, "Act Sequence = 1\nMaps = Basic\nGoalie Recruitment = Off\n");
var gm = new MapLayoutNode { Id = "gm", Type = "GeneralManager", Layer = 0, X = MapLayout.NativeStartX, Y = 10, GmRole = GoalieFreeAgents.Goalies, Outgoing = ["boss"] };
var layout = new MapLayout { BaseSquad = "Basic Squad", Nodes = [gm, new() { Id = "boss", Type = "Boss", Layer = 1, X = MapLayout.NativeStartX + MapLayout.NativeLayerPitch, Y = 10 }] };
File.WriteAllText(mapFile, layout.Serialize());
var owner = new Window { Width = 1100, Height = 760 }; owner.Show(); Pump();
var standalone = new MapEditorView(root, campaign); owner.Content = standalone; Pump();
var imported = standalone.CurrentLayout!.Nodes.Single(n => n.Id == "gm");
typeof(MapEditorView).GetMethod("OpenNodeDetails", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(standalone, [imported]); Pump();
var dialog = owner.OwnedWindows.Single();
ComboBox Role() => dialog.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "RecruitRole");
Check(Role().Items.Cast<string>().SequenceEqual([GoalieFreeAgents.Skaters]), "disabled campaign hides Goalies choice");
Check((string?)ToolTip.GetTip(Role()) == "Enable Goalie recruitment in campaign settings to choose Goalies for this node.", "disabled choice explains how to enable Goalies");
Check(imported.GmRole == GoalieFreeAgents.Goalies && Role().SelectedItem == null, "hidden option does not coerce imported Goalies node");
Check(!standalone.TrySaveMap(out string invalid) && invalid.Contains("Layer 1, node 1") && invalid.Contains("Enable recruitment"), "invalid imported node blocks map save with named guidance");
Check(new CampaignDocument(File.ReadAllText(mapFile)).Get("Recruit role gm") == GoalieFreeAgents.Goalies, "rejected save preserves imported node file");
dialog.Close(); Pump();
var compatibleMap = MapLayout.Parse(File.ReadAllText(mapFile));
compatibleMap.Nodes.Single(n => n.Id == "gm").GmRole = GoalieFreeAgents.Skaters;
File.WriteAllText(mapFile, compatibleMap.Serialize());

CampaignWorkspace OpenWorkspace()
{
    var view = new CampaignWorkspace(owner, root, campaign, () => { }, _ => { }, () => Task.CompletedTask);
    owner.Content = view; Pump();
    typeof(CampaignWorkspace).GetMethod("ShowSection", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(view, [4]); Pump();
    return view;
}
var workspace = OpenWorkspace();
var recruitment = workspace.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "GoalieRecruitment");
Check(recruitment.IsChecked == false && !workspace.HasUnsavedChanges, "saved Off opens as a clean campaign draft");
recruitment.IsChecked = true; Pump();
Check(workspace.HasUnsavedChanges && !GoalieFeatureOptions.Recruitment(new CampaignDocument(File.ReadAllText(configFile))), "unsaved toggle is draft only");
typeof(CampaignWorkspace).GetMethod("ShowSection", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(workspace, [2]); Pump();
var editor = workspace.GetVisualDescendants().OfType<MapEditorView>().Single();
var node = editor.CurrentLayout!.Nodes.Single(n => n.Id == "gm");
typeof(MapEditorView).GetMethod("OpenNodeDetails", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(editor, [node]); Pump();
dialog = owner.OwnedWindows.Single();
Check(Role().Items.Cast<string>().SequenceEqual([GoalieFreeAgents.Skaters, GoalieFreeAgents.Goalies]), "unsaved On immediately exposes Goalies choice");
Role().SelectedItem = GoalieFreeAgents.Goalies; Pump();
Check(node.GmRole == GoalieFreeAgents.Goalies && editor.HasUnsavedChanges, "Goalies role remains an unsaved map draft");
recruitment.IsChecked = false; Pump();
Check(Role().Items.Cast<string>().SequenceEqual([GoalieFreeAgents.Skaters]) && node.GmRole == GoalieFreeAgents.Goalies, "draft Off refreshes choices without mutating unsaved node");
Check(!editor.TrySaveMap(out invalid) && invalid.Contains("Goalies"), "map save reads current campaign draft");
recruitment.IsChecked = true; Pump();
Check(!editor.TrySaveMap(out string valid) && valid.Contains("Save campaign", StringComparison.OrdinalIgnoreCase), "Save maps rejects unsaved-On campaign gate: " + valid);
Check(!GoalieFeatureOptions.Recruitment(new CampaignDocument(File.ReadAllText(configFile)))
    && MapLayout.Parse(File.ReadAllText(mapFile)).Nodes.Single(n => n.Id == "gm").GmRole == GoalieFreeAgents.Skaters,
    "rejected map-only save leaves compatible Off and Skaters files");
dialog.Close(); Pump();
node.Outgoing.Clear();
Click(workspace, "Save campaign");
Check(GoalieFeatureOptions.Recruitment(new CampaignDocument(File.ReadAllText(configFile)))
    && MapLayout.Parse(File.ReadAllText(mapFile)).Nodes.Single(n => n.Id == "gm").GmRole == GoalieFreeAgents.Skaters
    && node.Outgoing.Count == 0 && workspace.HasUnsavedChanges,
    "failed map write leaves compatible On and Skaters files while retaining the draft");
node.Outgoing.Add("boss");
Click(workspace, "Save campaign");
Check(GoalieFeatureOptions.Recruitment(new CampaignDocument(File.ReadAllText(configFile)))
    && MapLayout.Parse(File.ReadAllText(mapFile)).Nodes.Single(n => n.Id == "gm").GmRole == GoalieFreeAgents.Goalies,
    "campaign Save writes compatible On and Goalies pair");
typeof(CampaignWorkspace).GetMethod("ShowSection", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(workspace, [4]); Pump();
Check(GoalieFeatureOptions.Recruitment(new CampaignDocument(File.ReadAllText(configFile))), "campaign setting saves and reopens");
workspace = OpenWorkspace();
recruitment = workspace.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "GoalieRecruitment");
Check(recruitment.IsChecked == true, "saved campaign checkbox reopens On");
recruitment.IsChecked = false; Pump(); Click(workspace, "Save campaign");
Check(GoalieFeatureOptions.Recruitment(new CampaignDocument(File.ReadAllText(configFile))), "campaign save rejects incompatible saved goalie node");
typeof(CampaignWorkspace).GetMethod("ShowSection", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(workspace, [2]); Pump();
editor = workspace.GetVisualDescendants().OfType<MapEditorView>().Single();
node = editor.CurrentLayout!.Nodes.Single(n => n.Id == "gm");
typeof(MapEditorView).GetMethod("OpenNodeDetails", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(editor, [node]); Pump();
dialog = owner.OwnedWindows.Single();
Role().SelectedItem = GoalieFreeAgents.Skaters; Pump();
dialog.Close(); Pump();
Click(workspace, "Save campaign");
Check(!GoalieFeatureOptions.Recruitment(new CampaignDocument(File.ReadAllText(configFile)))
    && MapLayout.Parse(File.ReadAllText(mapFile)).Nodes.Single(n => n.Id == "gm").GmRole == GoalieFreeAgents.Skaters,
    "ordinary Skaters node saves with recruitment Off");
owner.Close();
string squadA = Path.Combine(campaign, "player_teams", "A", "team.txt"), squadB = Path.Combine(campaign, "player_teams", "B", "team.txt");
string opponent = Path.Combine(campaign, "teams", "Opp", "team.txt");
foreach (string file in new[] { squadA, squadB, opponent })
{
    Directory.CreateDirectory(Path.GetDirectoryName(file)!);
    File.WriteAllText(file, "Team Name = " + Path.GetFileName(Path.GetDirectoryName(file)) + "\nCity = Test\nAbbreviation = TST\nBody = standard\nGloves = standard\nPants = standard\n");
}
var main = new MainWindow(["--content", root]); main.Show(); Pump();
foreach (var prompt in main.OwnedWindows.Where(window => window.Name == "DefaultLookPrompt").ToArray()) prompt.Close(false);
void OpenTeam(string file) { typeof(MainWindow).GetMethod("OpenFile", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, [file]); Pump(); }
OpenTeam(squadA);
var teamTabs = main.GetVisualDescendants().OfType<TabControl>().First();
teamTabs.SelectedItem = teamTabs.Items.Cast<TabItem>().Single(tab => Equals(tab.Header, "Squad behaviors")); Pump();
var reserve = main.GetVisualDescendants().OfType<ComboBox>().Single(combo => combo.Name == "Behavior_reserve_goalie");
Check(reserve.SelectedIndex == 0, "playable squad reserve starts inherited Off");
reserve.SelectedIndex = 1; Pump(); Click(main, "Save player team");
Check(GoalieFeatureOptions.Reserve(new CampaignDocument(File.ReadAllText(squadA))), "reserve setting saves to playable squad A");
OpenTeam(squadB);
Check(!GoalieFeatureOptions.Reserve(new CampaignDocument(File.ReadAllText(squadB))), "squad B remains independently Off");
OpenTeam(squadA);
teamTabs = main.GetVisualDescendants().OfType<TabControl>().First();
teamTabs.SelectedItem = teamTabs.Items.Cast<TabItem>().Single(tab => Equals(tab.Header, "Squad behaviors")); Pump();
Check(main.GetVisualDescendants().OfType<ComboBox>().Single(combo => combo.Name == "Behavior_reserve_goalie").SelectedIndex == 1, "squad A reserve reopens On");
var opponentDoc = new CampaignDocument(File.ReadAllText(opponent)); opponentDoc.Set(GoalieFeatureOptions.ReserveKey, "On"); opponentDoc.Save(opponent);
OpenTeam(opponent);
Check(!main.GetVisualDescendants().OfType<Control>().Any(control => control.Name == "Behavior_reserve_goalie" || control.Tag?.ToString() == GoalieFeatureOptions.ReserveKey),
    "non-playable team editor has no reserve control even for imported key");
main.Close();
var shareConfig = new CampaignDocument(File.ReadAllText(configFile));
shareConfig.Set(GoalieFeatureOptions.RecruitmentKey, "On"); shareConfig.Save(configFile);
var sharing = typeof(App).Assembly.GetType("CampaignCreator.CampaignSharing")!;
string package = Path.Combine(root, "goalie-features.zip");
sharing.GetMethod("Zip", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [campaign, package, root]);
sharing.GetMethod("Install", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [package, root, "Imported feature check"]);
string importedCampaign = Path.Combine(root, "campaigns", "Imported feature check");
Check(GoalieFeatureOptions.Recruitment(new CampaignDocument(File.ReadAllText(Path.Combine(importedCampaign, "campaign.txt"))))
    && GoalieFeatureOptions.Reserve(new CampaignDocument(File.ReadAllText(Path.Combine(importedCampaign, "player_teams", "A", "team.txt"))))
    && !GoalieFeatureOptions.Reserve(new CampaignDocument(File.ReadAllText(Path.Combine(importedCampaign, "player_teams", "B", "team.txt")))),
    "campaign share and import preserve scoped settings");
Console.WriteLine($"PASS {checks} goalie feature editor assertions");
