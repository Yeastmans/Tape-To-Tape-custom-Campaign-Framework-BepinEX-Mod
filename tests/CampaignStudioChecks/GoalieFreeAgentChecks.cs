using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CampaignCore;
using CampaignCreator;
using System.Reflection;

static class GoalieFreeAgentChecks
{
    public static void Run(string output)
    {
        output = Path.GetFullPath(output); Directory.CreateDirectory(output);
        string root = Path.Combine(output, "fixture-" + Guid.NewGuid().ToString("N"));
        string campaign = Path.Combine(root, "campaigns", "Goalie recruitment");
        string pool = Path.Combine(campaign, "player_teams", "goalie_free_agents");
        Directory.CreateDirectory(pool); Directory.CreateDirectory(Path.Combine(campaign, "map_layouts"));
        File.WriteAllText(Path.Combine(campaign, "campaign.txt"), "Act Sequence = 1\nMaps = Basic\nGoalie Recruitment = On\n");
        string goalie = Path.Combine(pool, "Keeper.txt");
        const string authored = "Name = Keeper\nPosition = Goalie\nCatching = 71\nFuture Field = preserve\n";
        File.WriteAllText(goalie, authored);
        var nav = CampaignNavigation.Load(campaign);
        Check(nav.SingleOrDefault(n => n.DisplayName == "Goalie Free Agents")?.Children.Single().FilePath == goalie,
            "Goalie pool appears in navigation as its own player collection");
        Check(!nav.Any(n => n.DisplayName == "Custom Squads" && n.Children.Any(c => c.DisplayName.Contains("goalie", StringComparison.OrdinalIgnoreCase))),
            "Goalie pool is not discovered as a custom squad");
        bool rejected = false; try { CreatorContent.ValidateName("goalie_free_agents"); } catch (ArgumentException) { rejected = true; }
        Check(rejected, "Goalie pool folder is reserved from authored team names");
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var owner = new Window { Width = 1100, Height = 760 }; string? opened = null;
        CampaignWorkspace Open()
        {
            var view = new CampaignWorkspace(owner, root, campaign, () => { }, file => opened = file, () => Task.CompletedTask);
            owner.Content = view; owner.Show();
            typeof(CampaignWorkspace).GetMethod("ShowSection", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(view, [4]); Pump(); return view;
        }
        var workspace = Open();
        CheckBox Selection(string file) => workspace.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "GoalieFreeAgentSelection" && c.Tag?.ToString() == Path.GetFileName(file));
        Check(Selection(goalie).IsChecked == true && !workspace.HasUnsavedChanges, "Authored goalie files start selected without making the campaign dirty");
        Selection(goalie).IsChecked = false;
        Check(workspace.HasUnsavedChanges && !File.Exists(Path.Combine(pool, SelectedPlayerPool.Manifest)), "Goalie selection remains a draft until Save campaign");
        Click(workspace, "Save campaign");
        Check(!workspace.HasUnsavedChanges && SelectedPlayerPool.Read(pool) is { Length: 0 } && File.ReadAllText(goalie) == authored,
            "Saving an empty goalie pool excludes the goalie without changing or deleting the authored file");
        workspace = Open();
        Check(Selection(goalie).IsChecked == false && !workspace.HasUnsavedChanges, "Excluded goalie remains unchecked on reopening");
        Click(workspace, "Edit goalie");
        Check(opened == goalie && Selection(goalie).IsChecked == false, "Excluded goalie remains editable without selecting it");
        var content = typeof(CampaignWorkspace).Assembly.GetType("CampaignCreator.GoalieFreeAgentContent")!;
        string Create(string name, string source = "") => (string)content.GetMethod("Create")!.Invoke(null, [root, campaign, name, source])!;
        string created = Create("New keeper");
        Check(new CampaignDocument(File.ReadAllText(created)).Get("Position") == "Goalie", "Make New creates a goalie for the existing goalie editor");
        Check(SelectedPlayerPool.Read(pool).SequenceEqual([created]), "Creating a goalie includes it and preserves existing exclusions");
        const string exported = "Name = Keeper\nExport Role = goalie\nCatching = 71\n";
        string source = Path.Combine(root, "source.txt"); File.WriteAllText(source, exported);
        string imported = Create("Keeper", source);
        var importedDoc = new CampaignDocument(File.ReadAllText(imported));
        Check(imported != goalie && importedDoc.Get("Name") != "Keeper" && importedDoc.Get("Catching") == "71" && importedDoc.Get("Position") == "Goalie" && File.ReadAllText(source) == exported,
            "Import copies goalie stats with a distinct name and preserves its source");
        string skater = Path.Combine(root, "skater.txt"); File.WriteAllText(skater, "Name = Forward\nPosition = Center\n");
        rejected = false; try { Create("Forward", skater); } catch (TargetInvocationException ex) when (ex.InnerException is ArgumentException) { rejected = true; }
        Check(rejected && Directory.GetFiles(pool, "*.txt").Length == 3, "Goalie import rejects skaters without publishing a file");
        workspace = Open();
        Check(Selection(created).IsChecked == true && Selection(imported).IsChecked == true && Selection(goalie).IsChecked == false,
            "New and imported goalies reopen selected while earlier exclusions remain");
        var scroll = workspace.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Name == "FreeAgentsPageScroll");
        scroll.Offset = new Vector(0, scroll.Extent.Height); Pump();
        using (var frame = owner.CaptureRenderedFrame()) frame!.Save(Path.Combine(output, "goalie-free-agents.png"));
        var gm = new MapLayoutNode { Id = "gm", Type = "GeneralManager", Layer = 0, X = MapLayout.NativeStartX, Y = 10, Outgoing = ["boss"] };
        var layout = new MapLayout { BaseSquad = "Basic Squad", Nodes = [gm, new() { Id = "boss", Type = "Boss", Layer = 1, X = MapLayout.NativeStartX + MapLayout.NativeLayerPitch, Y = 10 }] };
        File.WriteAllText(Path.Combine(campaign, "map_layouts", "map-1.txt"), layout.Serialize());
        var map = new MapEditorView(root, campaign); owner.Content = map; Pump();
        typeof(MapEditorView).GetMethod("OpenNodeDetails", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(map, [map.CurrentLayout!.Nodes[0]]); Pump();
        var dialog = owner.OwnedWindows.Single();
        var role = dialog.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "RecruitRole");
        Check(role.SelectedItem?.ToString() == "Skaters", "Existing GM nodes default to skater recruitment");
        role.SelectedItem = "Goalies"; Pump();
        Check(map.HasUnsavedChanges && new CampaignDocument(map.CurrentLayout.Serialize()).Get("Recruit role gm") == "Goalies", "Goalie role is a map draft setting");
        dialog.Close(); Click(map, "Undo");
        Check(new CampaignDocument(map.CurrentLayout!.Serialize()).Get("Recruit role gm") != "Goalies", "Undo restores the previous recruitment role");
        Click(map, "Redo");
        Check(new CampaignDocument(map.CurrentLayout!.Serialize()).Get("Recruit role gm") == "Goalies", "Redo restores goalie recruitment");
        Check(map.TrySaveMap(out string error), "Goalie recruitment map saves: " + error);
        Check(new CampaignDocument(File.ReadAllText(Path.Combine(campaign, "map_layouts", "map-1.txt"))).Get("Recruit role gm") == "Goalies", "Saving persists the node recruitment role");
        owner.Close();
    }
    private static void Click(Control parent, string text) { parent.GetVisualDescendants().OfType<Button>().Single(b => b.Content?.ToString() == text).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(); }
    private static void Pump() { for (int i = 0; i < 4; i++) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(20); } }
    private static void Check(bool ok, string label) { if (!ok) throw new Exception(label); Console.WriteLine("PASS " + label); }
}
