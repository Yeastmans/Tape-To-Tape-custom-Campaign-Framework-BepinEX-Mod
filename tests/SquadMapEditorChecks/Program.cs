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
AppBuilder.Configure<CampaignCreator.App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
var constructor = typeof(MapEditorView).GetConstructor([typeof(string), typeof(string), typeof(Func<string>), typeof(string)]);
Check(constructor != null, "Map editor must support a squad context");
string root = Path.GetFullPath(Path.Combine(args[0], "editor-" + Guid.NewGuid().ToString("N")));
string campaign = Path.Combine(root, "campaigns", "Squad maps"), squad = Path.Combine(campaign, "player_teams", "a");
Directory.CreateDirectory(squad); Directory.CreateDirectory(Path.Combine(campaign, "map_layouts"));
File.WriteAllText(Path.Combine(campaign, "campaign.txt"), "Act Sequence = 1,2,3\n");
File.WriteAllText(Path.Combine(squad, "team.txt"), "Team Name = Squad A\nUse Own Maps = yes\n");
File.WriteAllText(Path.Combine(root, "_game_maps.txt"), "SQUAD 'Basic Squad' maps=1\nMAP index=0\nTEMPLATE 0\nLAYER 0\nNODE n=0 type=Coach pos=3.1,10 out=[0] group='' team='' gm=1 talents=1 relics=1\nLAYER 1\nNODE n=0 type=Boss pos=9.5,10 out=[] group='' team='' gm=1 talents=1 relics=1\n");
var template = new MapLayout { BaseSquad = "Basic Squad", Nodes = [new() { Id = "coach", Type = "Coach", X = 3.1, Y = 10.4, Outgoing = ["boss"] }, new() { Id = "boss", Type = "Boss", Layer = 1, X = 9.5, Y = 10.4 }] };
MapNodePlacement.ArrangeLayers(template);
string Shared(int stage) => Path.Combine(campaign, "map_layouts", $"map-{stage}.txt");
string Own(int stage) => SquadMapOverrides.OverridePath(squad, stage);
for (int i = 1; i <= 2; i++) { template.Name = "Campaign " + i; File.WriteAllText(Shared(i), template.Serialize()); }
File.WriteAllText(Path.Combine(campaign, "assignments.txt"), "# Keep exactly\n");
var shared = new[] { Shared(1), Shared(2), Path.Combine(campaign, "campaign.txt"), Path.Combine(campaign, "assignments.txt") }.ToDictionary(f => f, File.ReadAllText);
MapEditorView view = null!;
var window = new Window { Width = 1380, Height = 860 }; window.Show();
void Pump() { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
void Open() { view = (MapEditorView)constructor!.Invoke([root, campaign, null, squad]); window.Content = view; Pump(); }
IEnumerable<T> Controls<T>() where T : Control => view.GetVisualDescendants().OfType<T>();
void Rename(string value) { Controls<TextBox>().Single(b => b.Name == "MapName").Text = value; Pump(); }
void Reset() { Controls<Button>().Single(b => b.Name == "ResetSquadMap").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(); }
string State() => Controls<TextBlock>().Single(t => t.Name == "SquadMapStatus").Text!;
void Save() { Check(view.TrySaveMap(out string error), "Team maps should save: " + error); Pump(); }
Open(); Check(!view.HasUnsavedChanges && State() == "Following campaign", "Opening normalized previews stays inherited and clean");
Check(!Controls<Button>().Any(b => Equals(b.Content, "+ Add map") || b.Classes.Contains("map-close")), "Squad mode has no sequence controls");
view.SelectMap(2); Pump(); Save();
Check(!File.Exists(Own(1)) && !File.Exists(Own(2)) && !File.Exists(Own(3)) && !File.Exists(Shared(3)), "Navigating and saving never freezes native map preview");
view.SelectMap(1); Pump(); Rename("Only A's second map");
Check(view.HasUnsavedChanges && State() == "Custom for this squad", "First intentional edit detaches current map");
view.Undo(); Pump(); Check(!view.HasUnsavedChanges && State() == "Following campaign", "Undo all first edits restores inheritance");
view.Redo(); Pump(); Check(State() == "Custom for this squad", "Redo restores independent draft");
Save(); Check(File.Exists(Own(2)) && !File.Exists(Own(1)) && !File.Exists(Own(3)), "Save writes only edited stage");
Check(shared.All(p => File.ReadAllText(p.Key) == p.Value), "Squad save leaves campaign settings, maps and assignments byte-identical");
Open(); view.SelectMap(1); Pump(); Check(view.CurrentLayout!.Name == "Only A's second map" && State() == "Custom for this squad", "Reopen loads saved override");
Rename("Campaign 2"); Save(); Check(File.Exists(Own(2)), "Matching campaign content does not implicitly reset a saved override");
Reset(); Check(State() == "Following campaign" && view.HasUnsavedChanges, "Reset previews inherited map as unsaved change");
view.Undo(); Pump(); Check(State() == "Custom for this squad" && !view.HasUnsavedChanges, "Undo reset restores saved override");
view.Redo(); Pump(); Save(); Check(!File.Exists(Own(2)), "Saving reset removes override");
Rename("Unsaved after reset"); view.Undo(); Pump(); Save(); Check(!File.Exists(Own(2)), "Undo first post-reset edit preserves inheritance");
view.SelectMap(0); Pump(); Rename("Preserve stale draft");
template.Name = "Campaign changed elsewhere"; File.WriteAllText(Shared(2), template.Serialize());
Check(!view.TrySaveMap(out string stale) && stale.Contains("changed", StringComparison.OrdinalIgnoreCase), "Concurrent campaign map edit rejects stale squad save");
Check(view.CurrentLayout!.Name == "Preserve stale draft" && view.HasUnsavedChanges && !File.Exists(Own(1)), "Rejected save preserves draft without writing");
Open(); view.SelectMap(1); Pump(); Check(view.CurrentLayout!.Name == "Campaign changed elsewhere", "Untouched map rereads current campaign after reopening");
Rename("Surviving second map"); Save();
// A campaign deletion must remap saved overrides with the surviving original map.
var campaignView = new MapEditorView(root, campaign); window.Content = campaignView; Pump();
typeof(MapEditorView).GetMethod("RemoveMap", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(campaignView, [0]);
Check(campaignView.TrySaveMap(out string remapError), "Shared campaign removal should save: " + remapError);
Check(MapLayout.Parse(File.ReadAllText(Own(1))).Name == "Surviving second map" && !File.Exists(Own(2)), "Campaign deletion remaps override to surviving original stage");
Check(!view.TrySaveMap(out stale), "Open squad editor rejects save after campaign renumbering");
Open(); Check(view.MapCount == 2 && view.CurrentLayout!.Name == "Surviving second map", "Reopened squad shares updated count/order");
var settingsType = typeof(MapEditorView).Assembly.GetType("CampaignCreator.SquadMapsEditor");
Check(settingsType != null, "Campaign player squads need map toggle and launch controls");
var registered = new Dictionary<string, Func<string>>(); bool saveAllowed = false; int saved = 0, changed = 0;
var settings = (Control)Activator.CreateInstance(settingsType!, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
    [window, root, campaign, squad, new CampaignDocument("Team Name = Squad A\nUse Own Maps = yes\n"),
        (Action)(() => changed++), (Action<string, Func<string>>)((k, read) => registered[k] = read),
        (Func<Task<bool>>)(() => { saved++; if (saveAllowed) File.WriteAllText(Path.Combine(squad, "team.txt"), "Team Name = Saved new name\nUse Own Maps = " + registered[SquadMapOverrides.ModeKey]()); return Task.FromResult(saveAllowed); })], null)!;
window.Content = settings; Pump();
var toggle = settings.GetVisualDescendants().OfType<CheckBox>().Single(b => b.Name == "UseOwnMaps");
var launch = settings.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "EditTeamMaps");
toggle.IsChecked = false; Pump(); Check(launch.IsVisible && Equals(launch.Content, "Edit saved team maps") && registered[SquadMapOverrides.ModeKey]() == "no" && File.Exists(Own(1)), "Toggle off retains files and exposes saved-map repair");
toggle.IsChecked = true; Pump(); Check(launch.IsVisible && changed == 2, "Toggle on reveals Edit team maps");
launch.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
Check(saved == 1 && !window.OwnedWindows.Any(), "Failed team save must block map editing even when disk toggle was already on");
saveAllowed = true; launch.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
Check(saved == 2 && window.OwnedWindows.Single().Title == "Team maps · Saved new name", "Successful existing team save precedes opening maps with saved squad name");
window.OwnedWindows.Single().Close(); Pump();
Console.WriteLine($"PASS {checks} squad map editor checks; evidence: {root}");
window.Content = null; window.Close();
