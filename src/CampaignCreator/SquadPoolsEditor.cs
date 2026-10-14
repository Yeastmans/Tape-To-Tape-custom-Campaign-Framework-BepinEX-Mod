using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CampaignCore;

namespace CampaignCreator;

internal sealed class SquadPoolsEditor : UserControl
{
    private readonly CheckBox own;
    private StartingPoolEditor? bench, stars;
    private SquadFreeAgentEditor? agents;
    internal bool HasChanges => bench?.HasChanges == true || stars?.HasChanges == true || agents?.HasChanges == true;
    internal SquadPoolsEditor(Window owner, string root, string squad, CampaignDocument team, Action<string> edit,
        Action changed, Action<string, Func<string>> register)
    {
        Name = "SquadPlayerPools";
        own = new CheckBox { Name = "UseOwnPlayerPools", Content = "Use own pools", IsChecked = SquadPlayerPools.Own(team) };
        register(SquadPlayerPools.ModeKey, () => own.IsChecked == true ? "yes" : team.Get(SquadPlayerPools.ModeKey).Length == 0 ? "" : "no");
        var shared = new TextBlock { Text = "This squad uses the campaign's shared bench, superstars and free agents.", TextWrapping = TextWrapping.Wrap };
        var content = new ContentControl();
        void Refresh()
        {
            bool enabled = own.IsChecked == true;
            shared.IsVisible = !enabled; content.IsVisible = enabled;
            if (!enabled || bench != null) return;
            bench = new StartingPoolEditor(owner, root, () => squad, "draft_pool", edit, changed, emptyByDefault: true);
            stars = new StartingPoolEditor(owner, root, () => squad, "superstars", edit, changed, emptyByDefault: true);
            agents = new SquadFreeAgentEditor(owner, root, squad, edit, changed);
            content.Content = new TabControl { Name = "SquadPoolKinds", ItemsSource = new[] {
                new TabItem { Header = "Bench", Content = new ScrollViewer { Content = bench } },
                new TabItem { Header = "Superstars", Content = new ScrollViewer { Content = stars } },
                new TabItem { Header = "Free agents", Content = agents }
            }};
        }
        own.IsCheckedChanged += (_, _) => { Refresh(); changed(); };
        var grid = new Grid { Margin = new Thickness(12, 8), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*"), RowSpacing = 12 };
        grid.Children.Add(own); Grid.SetRow(shared, 1); grid.Children.Add(shared);
        var hint = new TextBlock { Text = "Own pools start empty. Only selected players are offered. Empty bench and superstar pools skip their starting screens; an empty free-agent pool offers no recruits. Turning this off keeps your own choices for later. Save the squad before editing a player.", TextWrapping = TextWrapping.Wrap, Foreground = StudioTheme.Muted };
        Grid.SetRow(hint, 2); grid.Children.Add(hint); Grid.SetRow(content, 3); grid.Children.Add(content);
        Content = grid; Refresh();
    }
    internal void Save()
    {
        if (own.IsChecked != true && !HasChanges) return;
        bench?.Save(); stars?.Save(); agents?.Save();
    }
}

internal sealed class SquadFreeAgentEditor : UserControl
{
    private readonly string directory;
    private readonly HashSet<string> selected;
    private readonly StackPanel list = new() { Spacing = 8 };
    private readonly TextBlock message = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Action changed;
    private readonly Action<string> edit;
    internal bool HasChanges { get; private set; }
    internal SquadFreeAgentEditor(Window owner, string root, string squad, Action<string> edit, Action changed)
    {
        this.changed = changed; this.edit = edit;
        directory = Path.Combine(squad, "player_teams", "free_agents");
        selected = (SelectedPlayerPool.Read(directory) ?? (Directory.Exists(directory) ? Directory.GetFiles(directory, "*.txt") : []))
            .Select(f => Path.GetFileName(f)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var import = new Button { Name = "BulkImportSquadFreeAgents", Content = "Import players", Padding = StudioTheme.ButtonPadding };
        import.Click += async (_, _) => {
            try
            {
                await ContentImport.PickPlayers(owner, root, null, sources => {
                    Save();
                    foreach (string file in PlayerPoolImport.IntoFreeAgents(root, squad, sources)) selected.Add(Path.GetFileName(file));
                    HasChanges = true; changed(); Refresh();
                });
            }
            catch (Exception ex) { message.Text = ex.Message; }
        };
        var make = new Button { Name = "NewSquadFreeAgent", Content = "New player", Padding = StudioTheme.ButtonPadding };
        make.Click += async (_, _) => {
            var dialog = new Window { Title = "New free agent", Width = 430, Height = 250, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var name = new TextBox { Watermark = "Player name" };
            var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
            var create = new Button { Content = "Create player" };
            create.Click += (_, _) => {
                try { Save(); string file = FreeAgentContent.Create(root, squad, name.Text ?? ""); selected.Add(Path.GetFileName(file)); HasChanges = true; changed(); Refresh(); dialog.Close(); }
                catch (Exception ex) { error.Text = ex.Message; }
            };
            var cancel = new Button { Content = "Cancel" }; cancel.Click += (_, _) => dialog.Close();
            dialog.Content = new StackPanel { Margin = new Thickness(24), Spacing = 12, Children = { name, error, create, cancel } };
            await dialog.ShowDialog(owner);
        };
        var panel = new DockPanel { LastChildFill = true };
        var header = new StackPanel { Spacing = 12, Children = {
            new TextBlock { Text = "Import from Base game, Player library, In-game custom or Campaign custom. Check the players this squad can recruit; unchecked copies stay available to edit.", TextWrapping = TextWrapping.Wrap },
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { import, make } }, message } };
        DockPanel.SetDock(header, Dock.Top); panel.Children.Add(header); panel.Children.Add(new ScrollViewer { Content = list });
        Content = panel; Refresh();
    }
    private void Refresh()
    {
        list.Children.Clear();
        foreach (string file in (Directory.Exists(directory) ? Directory.GetFiles(directory, "*.txt") : []).Order(StringComparer.OrdinalIgnoreCase))
        {
            string key = Path.GetFileName(file);
            var check = new CheckBox { Name = "SquadFreeAgentSelection", Tag = key, Content = new CampaignDocument(File.ReadAllText(file)).Get("Name", key), IsChecked = selected.Contains(key) };
            check.IsCheckedChanged += (_, _) => { if (check.IsChecked == true) selected.Add(key); else selected.Remove(key); HasChanges = true; changed(); Count(); };
            var button = new Button { Content = "Edit player", Padding = StudioTheme.ButtonPadding };
            button.Click += (_, _) => edit(file);
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
            row.Children.Add(check); Grid.SetColumn(button, 1); row.Children.Add(button); list.Children.Add(row);
        }
        Count();
    }
    private void Count() => message.Text = selected.Count + " free agents selected";
    internal void Save()
    {
        SelectedPlayerPool.Save(directory, selected.Order(StringComparer.OrdinalIgnoreCase)); HasChanges = false;
    }
}
