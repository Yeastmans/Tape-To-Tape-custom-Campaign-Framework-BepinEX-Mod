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
void Check(bool ok, string reason) { checks++; if (!ok) throw new Exception(reason); }
AppBuilder.Configure<CampaignCreator.App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
string root = Path.GetFullPath(Path.Combine(args[0], "editor-" + Guid.NewGuid().ToString("N")));
string campaign = Path.Combine(root, "campaigns", "Custom Coach checks");
Directory.CreateDirectory(Path.Combine(campaign, "map_layouts"));
File.WriteAllText(Path.Combine(campaign, "campaign.txt"), "Act Sequence = 1\n");
File.WriteAllText(Path.Combine(root, "_game_maps.txt"), "SQUAD 'Basic Squad' maps=1\nMAP index=0\nTEMPLATE 0\nLAYER 0\nNODE n=0 type=Coach pos=3.1,10 out=[0] group='' team='' gm=1 talents=1 relics=1\nLAYER 1\nNODE n=0 type=Boss pos=9.5,10 out=[] group='' team='' gm=1 talents=1 relics=1\n");
var map = new MapLayout { BaseSquad = "Basic Squad", Nodes = [new() { Id = "coach", Type = "Coach", X = 3.1, Y = 10.4, Outgoing = ["boss"] }, new() { Id = "boss", Type = "Boss", Layer = 1, X = 9.5, Y = 10.4 }] };
MapNodePlacement.ArrangeLayers(map);
File.WriteAllText(Path.Combine(campaign, "map_layouts", "map-1.txt"), map.Serialize());
var view = new MapEditorView(root, campaign);
var owner = new Window { Width = 1380, Height = 860, Content = view }; owner.Show();
void Pump() { Dispatcher.UIThread.RunJobs(); owner.UpdateLayout(); foreach (var w in owner.OwnedWindows) w.UpdateLayout(); }
Pump();
string before = view.CurrentLayout!.Serialize();
typeof(MapEditorView).GetMethod("OpenNodePicker", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(view, [0, false, view.CurrentLayout.Nodes[0]]);
Pump();
var picker = owner.OwnedWindows.Single();
Check(picker.GetVisualDescendants().OfType<Button>().Any(b => Equals(b.Content, "Custom")), "Node picker must offer Custom");
picker.Close(); Pump(); Check(view.CurrentLayout.Serialize() == before, "Cancel node picker cannot change the map");
void Click(Window window, string name) { window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(); }
Window Dialog() => owner.OwnedWindows.Single();
void Custom() {
    typeof(MapEditorView).GetMethod("OpenNodePicker", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(view, [0, false, view.CurrentLayout!.Nodes[0]]); Pump();
    Click(Dialog(), "ChooseCustomNode");
}
Custom(); Click(Dialog(), "CustomCoachCreate");
var editor = Dialog();
Click(editor, "CustomCoachSave");
Check(editor.IsVisible && view.CurrentLayout.Serialize() == before, "Invalid draft must remain editable without changing map");
editor.GetVisualDescendants().OfType<TextBox>().Single(b => b.Name == "CustomCoachName").Text = "  Skating Coach  ";
var selection = editor.GetVisualDescendants().OfType<CheckBox>().First(b => b.Tag is string id && id.StartsWith("coach-relic:"));
string relic = ((string)selection.Tag!)[12..]; selection.IsChecked = true;
Click(editor, "CustomCoachSave");
Check(view.CurrentLayout.Nodes[0].CustomCoach?.Name == "Skating Coach", "Save and use trims name and places a Coach");
Check(view.CurrentLayout.Nodes[0].CustomCoach!.Relics.SetEquals([relic]), "Placed pool equals selected relics");
string libraryFile = Directory.GetFiles(Path.Combine(root, "library", "nodes"), "*.coach").Single();
Check(CustomCoachDefinition.Parse(File.ReadAllText(libraryFile)).Relics.SetEquals([relic]), "Save and use creates reusable entry");
view.Undo(); Pump(); Check(view.CurrentLayout.Nodes[0].CustomCoach == null, "Undo restores ordinary Coach");
Check(File.Exists(libraryFile), "Undoing placement preserves explicitly saved library entry");
Custom(); Click(Dialog(), "CustomCoachLoad");
var load = Dialog(); load.GetVisualDescendants().OfType<Button>().Single(b => b.Tag is string id && id.StartsWith("saved-coach:")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
Check(view.CurrentLayout.Nodes[0].CustomCoach?.Name == "Skating Coach", "Load saved places a fresh snapshot");
typeof(MapEditorView).GetField("selected", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(view, view.CurrentLayout.Nodes[0]);
typeof(MapEditorView).GetMethod("Inspect", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(view, null); Pump();
((StackPanel)typeof(MapEditorView).GetField("inspector", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view)!).Children.OfType<ComboBox>().Single(b => b.Name == "NodeType").SelectedItem = "Coach"; Pump();
Check(view.CurrentLayout.Nodes[0].CustomCoach == null, "Converting custom Coach to ordinary Coach removes snapshot");
view.Undo(); Pump();
Check(view.CurrentLayout.Nodes[0].CustomCoach?.Name == "Skating Coach", "Undo restores custom snapshot");
var placed = view.CurrentLayout.Nodes[0].CustomCoach!;
var edit = new CustomCoachEditor(root, campaign, placed, false); edit.Show(owner); Pump();
edit.GetVisualDescendants().OfType<TextBox>().Single(b => b.Name == "CustomCoachName").Text = "Cancelled edit";
Click(edit, "CustomCoachCancel"); Check(placed.Name == "Skating Coach", "Cancelling editor cannot mutate placement");
Check(view.TrySaveMap(out string error), "Map saves: " + error);
var reopened = MapLayout.Parse(File.ReadAllText(Path.Combine(campaign, "map_layouts", "map-1.txt")));
Check(reopened.Nodes[0].CustomCoach?.Relics.SetEquals([relic]) == true, "Saved map preserves pool on reopen");
Check(CustomCoachDefinition.Parse(File.ReadAllText(libraryFile)).Name == "Skating Coach", "Placement changes never rewrite library entry");
const string png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGMwmPDgPwAFNAKg4ubHmAAAAABJRU5ErkJggg==";
var dependency = new CustomRewardDefinition { Name = "Node-only relic", Kind = CustomRewardDefinition.CustomRelics, Trigger = "While owned", Target = "Whole team", IconPng = png };
var portableCoach = new CustomCoachDefinition { Name = "Portable Coach", ImagePng = png };
portableCoach.Relics.Add(dependency.Id); portableCoach.Rewards.Add(dependency);
reopened.Nodes[0].CustomCoach = portableCoach;
File.WriteAllText(Path.Combine(campaign, "map_layouts", "map-1.txt"), reopened.Serialize());
var sharing = typeof(CampaignWorkspace).Assembly.GetType("CampaignCreator.CampaignSharing")!;
object? Share(string method, params object[] values) => sharing.GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, values);
string zip = Path.Combine(root, "portable.zip"); Share("Zip", campaign, zip, root);
string fresh = Path.Combine(root, "recipient"); Share("Install", zip, fresh, "Imported");
string imported = Path.Combine(fresh, "campaigns", "Imported");
var importedCoach = MapLayout.Parse(File.ReadAllText(Path.Combine(imported, "map_layouts", "map-1.txt"))).Nodes[0].CustomCoach!;
Check(importedCoach.ImagePng == png && importedCoach.Rewards[0].ToDocument().ToString() == dependency.ToDocument().ToString(), "ZIP carries node image and exact custom relic mechanics to a fresh content root");
Check(File.Exists(Path.Combine(imported, CustomRewardDefinition.Folder, dependency.Id + CustomRewardDefinition.Extension)), "Node-only reward is available for runtime lookup after import");
Check(!File.Exists(Path.Combine(imported, "reward_pools.txt")), "Sharing never adds node relics to campaign-wide pools");
// A normal ZIP already carries the matching campaign companion. It must not
// shadow a different recipient library definition during import validation.
string recipient = Path.Combine(root, "conflicting-recipient");
var recipientRule = dependency.Copy(); recipientRule.Amount += 7;
recipientRule.Save(Path.Combine(recipient, "library", "rewards"));
string recipientFile = Path.Combine(recipient, "library", "rewards", dependency.Id + CustomRewardDefinition.Extension);
string recipientBefore = File.ReadAllText(recipientFile);
bool importRejected = false;
try { Share("Install", zip, recipient, "Rejected"); } catch (TargetInvocationException ex) when (ex.InnerException is ArgumentException) { importRejected = true; }
Check(importRejected, "Imported campaign companion must not hide a conflicting recipient library relic");
Check(!Directory.Exists(Path.Combine(recipient, "campaigns", "Rejected")) && File.ReadAllText(recipientFile) == recipientBefore,
    "Rejected import preserves the recipient library and installs no campaign");
dependency.Save(Path.Combine(recipient, "library", "rewards"));
Share("Install", zip, recipient, "Matching");
Check(File.Exists(Path.Combine(recipient, "campaigns", "Matching", "campaign.txt"))
    && File.ReadAllText(recipientFile) == dependency.ToDocument().ToString(), "A matching recipient library definition remains unchanged and permits import");
var conflicting = dependency.Copy(); conflicting.Name = "Conflicting reward"; conflicting.Save(Path.Combine(root, "library", "rewards"));
bool rejected = false;
try { Share("Zip", campaign, Path.Combine(root, "bad.zip"), root); } catch (TargetInvocationException ex) when (ex.InnerException is ArgumentException) { rejected = true; }
Check(rejected && !File.Exists(Path.Combine(root, "bad.zip")), "Conflicting custom IDs reject export before writing a ZIP");
var picture = new CustomCoachEditor(fresh, imported, importedCoach, false); picture.Show(owner); Pump();
Check(picture.GetVisualDescendants().OfType<Image>().Single(i => i.Name == "CustomCoachImage").Source != null, "Embedded map artwork renders without the original image file");
picture.CaptureRenderedFrame()!.Save(Path.Combine(root, "custom-coach-editor.png")); picture.Close();
owner.Close();
Console.WriteLine($"PASS {checks} custom Coach editor checks");
