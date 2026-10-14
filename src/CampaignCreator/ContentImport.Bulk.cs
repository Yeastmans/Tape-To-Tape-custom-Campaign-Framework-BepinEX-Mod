using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace CampaignCreator;

public static partial class ContentImport
{
    /// <summary>Choose skaters across import sources in one batch; commit only on Import.</summary>
    public static async Task<bool> PickPlayers(Window owner, string root, int? maximum, Action<string[]> importPlayers)
    {
        if (maximum is <= 0) throw new ArgumentException("Remove a player to make room before importing.");
        var catalogs = new Dictionary<string, List<Entry>>();
        var chosen = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rows = new List<CheckBox>();
        var dialog = new Window { Title = "Bulk import players", Width = 680, Height = 690,
            MinWidth = 480, MinHeight = 480, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var source = new ComboBox { Name = "BulkPlayerSource", ItemsSource = new[] { PlayerLibrary, BaseGame, InGame, CampaignCustom },
            SelectedItem = PlayerLibrary, HorizontalAlignment = HorizontalAlignment.Stretch };
        var search = new TextBox { Name = "BulkPlayerSearch", Watermark = "Search players in this source" };
        var list = new StackPanel { Spacing = 4 };
        var count = new TextBlock { Name = "BulkPlayerCount", FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
        var message = new TextBlock { Name = "BulkPlayerMessage", Foreground = Brush.Parse("#FFD18C"), TextWrapping = TextWrapping.Wrap };
        var import = new Button { Name = "BulkPlayerConfirm", Content = "Import selected", IsEnabled = false };
        StudioTheme.Primary(import);
        var clear = new Button { Content = "Clear selection" };
        var selectShown = new Button { Content = "Select shown", IsVisible = maximum == null, Margin = new Thickness(0, 0, 8, 0) };
        bool updating = false;
        void Update()
        {
            updating = true;
            foreach (var row in rows)
            {
                bool included = selected.Contains((string)row.Tag!);
                row.IsChecked = included;
                row.IsEnabled = included || maximum == null || selected.Count < maximum;
            }
            updating = false;
            count.Text = maximum is int limit ? $"{selected.Count} / {limit} selected · empty slots" : $"{selected.Count} selected";
            if (maximum == selected.Count) count.Text += " · Limit reached";
            import.IsEnabled = selected.Count > 0;
            clear.IsEnabled = selected.Count > 0;
        }
        void Refresh()
        {
            list.Children.Clear(); rows.Clear();
            string currentSource = source.SelectedItem as string ?? PlayerLibrary;
            List<Entry> entries;
            try
            {
                if (!catalogs.TryGetValue(currentSource, out entries!))
                    catalogs[currentSource] = entries = Entries(root, "skater", currentSource);
            }
            catch (Exception ex) { message.Text = ex.Message; selectShown.IsEnabled = false; Update(); return; }
            foreach (var entry in entries.Where(e => e.Name.Contains(search.Text ?? "", StringComparison.OrdinalIgnoreCase)))
            {
                var row = new CheckBox { Name = "BulkPlayerChoice", Tag = entry.File,
                    Content = new TextBlock { Text = entry.Name, TextWrapping = TextWrapping.Wrap },
                    HorizontalAlignment = HorizontalAlignment.Stretch };
                ToolTip.SetTip(row, entry.File);
                row.IsCheckedChanged += (_, _) =>
                {
                    if (updating) return;
                    if (row.IsChecked == true && (maximum == null || selected.Count < maximum))
                    { selected.Add(entry.File); chosen[entry.File] = entry; }
                    else selected.Remove(entry.File);
                    message.Text = ""; Update();
                };
                rows.Add(row); list.Children.Add(row);
            }
            if (rows.Count == 0) list.Children.Add(new TextBlock { Text = entries.Count == 0
                ? currentSource == BaseGame ? "No base-game players available. Use Read game artwork on Home first."
                    : "No skaters in this source yet. Choose another source above."
                : "No matching players in this source.", TextWrapping = TextWrapping.Wrap });
            selectShown.IsEnabled = rows.Count > 0;
            Update();
        }
        search.TextChanged += (_, _) => Refresh();
        source.SelectionChanged += (_, _) => { message.Text = ""; Refresh(); };
        clear.Click += (_, _) => { selected.Clear(); message.Text = ""; Update(); };
        selectShown.Click += (_, _) =>
        {
            foreach (var row in rows)
            {
                string file = (string)row.Tag!;
                selected.Add(file);
                chosen[file] = catalogs[(string)source.SelectedItem!].First(e => e.File == file);
            }
            message.Text = ""; Update();
        };
        import.Click += (_, _) =>
        {
            try
            {
                if (selected.Count == 0 || maximum is int limit && selected.Count > limit) return;
                // Selections survive changing sources and search; one cap applies to the whole batch.
                importPlayers(selected.Select(file => chosen[file]).OrderBy(e => e.Name).ThenBy(e => e.File, StringComparer.OrdinalIgnoreCase).Select(e => e.File).ToArray());
                dialog.Close(true);
            }
            catch (Exception ex) { message.Text = ex.Message; }
        };
        var cancel = new Button { Content = "Cancel" }; cancel.Click += (_, _) => dialog.Close(false);
        var layout = new Grid { Margin = new Thickness(24), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,*,Auto,Auto"), RowSpacing = 12 };
        void Add(Control control, int row) { Grid.SetRow(control, row); layout.Children.Add(control); }
        Add(new TextBlock { Text = "Bulk import players", FontSize = 24, TextWrapping = TextWrapping.Wrap }, 0);
        Add(new TextBlock { Text = maximum == null
            ? "Check skaters from any source to add as free agents. New copies are checked in the pool; originals stay unchanged."
            : "Check skaters from any source to fill empty slots. Filled slots stay as they are. The limit includes selections from every source; originals stay unchanged.", TextWrapping = TextWrapping.Wrap }, 1);
        Add(source, 2); Add(search, 3);
        Add(new StackPanel { Spacing = 8, Children = { count, new WrapPanel { Children = { selectShown, clear } } } }, 4);
        Add(new ScrollViewer { Content = list, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled }, 5);
        Add(message, 6);
        Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { import, cancel } }, 7);
        dialog.Content = layout; Refresh();
        return await dialog.ShowDialog<bool>(owner);
    }
}
