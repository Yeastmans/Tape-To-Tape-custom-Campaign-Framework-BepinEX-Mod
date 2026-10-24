using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using CampaignCore;

namespace CampaignCreator;

public sealed class CustomCoachEditor : Window
{
    private readonly CustomCoachDefinition draft;
    private readonly TextBox coachName = new() { Name = "CustomCoachName", MaxLength = 80, Watermark = "Name this Coach" };
    private readonly TextBox search = new() { Name = "CustomCoachSearch", Watermark = "Search relic names and descriptions" };
    private readonly CheckBox special = new() { Content = "Include special and Chaos relics", Name = "CustomCoachSpecial" };
    private readonly TextBlock status = new() { Name = "CustomCoachStatus", TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock count = new() { TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel rows = new() { Spacing = 4 };
    private readonly Image preview = new() { Name = "CustomCoachImage", Height = 120, Stretch = Stretch.Uniform };
    private readonly Dictionary<string, RewardCatalog.Entry> entries;
    private readonly Dictionary<string, CustomRewardDefinition> custom;
    private readonly string root, campaign;

    public CustomCoachEditor(string contentRoot, string campaignDirectory, CustomCoachDefinition? existing = null, bool createNew = true)
    {
        root = contentRoot; campaign = campaignDirectory;
        draft = existing?.Copy() ?? new CustomCoachDefinition();
        entries = CustomCoachCatalog.Entries(root, campaign, draft);
        custom = CustomRewardCatalog.Items(root, campaign);
        foreach (var item in draft.Rewards) custom[item.Id] = item;
        Title = createNew ? "Create Custom Coach" : "Edit Custom Coach";
        Width = 760; Height = 790; MinWidth = 540; MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        coachName.Text = draft.Name;
        var import = new Button { Content = "Import PNG…", Name = "CustomCoachImport" };
        import.Click += async (_, _) => {
            try {
                var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Choose world-map artwork", AllowMultiple = false,
                    FileTypeFilter = new[] { new FilePickerFileType("PNG image") { Patterns = new[] { "*.png" } } } });
                if (files.Count == 0) return;
                string? path = files[0].TryGetLocalPath();
                if (path == null) throw new ArgumentException("Choose a local PNG file.");
                draft.ImagePng = Convert.ToBase64String(CustomRewardIcon.ReadPng(path)); RefreshImage(); status.Text = "";
            } catch (Exception ex) { status.Text = ex.Message; }
        };
        var clear = new Button { Content = "Use normal Coach art", Name = "CustomCoachClearImage" };
        clear.Click += (_, _) => { draft.ImagePng = ""; RefreshImage(); };
        var header = new StackPanel { Spacing = 8, Children = {
            new TextBlock { Text = "Custom Coach", FontSize = 25, FontWeight = FontWeight.Bold },
            new TextBlock { Text = "Name" }, coachName, preview,
            new WrapPanel { Children = { import, clear } },
            new TextBlock { Text = "World-map artwork only · PNG up to 4 MB and 2048 × 2048. Without an image, the normal Coach art is used.", TextWrapping = TextWrapping.Wrap },
            new TextBlock { Text = "Relic pool", FontSize = 19, FontWeight = FontWeight.SemiBold },
            new TextBlock { Text = "Only these relics can be offered by this Coach. Choose at least one; small pools show fewer cards. If no relic is eligible, the player can leave. Stat-only modes still require stat relics.", TextWrapping = TextWrapping.Wrap },
            search, special, count
        } };
        var save = new Button { Name = "CustomCoachSave", Content = createNew ? "Save and use" : "Apply to this node" };
        StudioTheme.Primary(save);
        save.Click += (_, _) => Submit(createNew);
        var saveAs = new Button { Name = "CustomCoachSaveAs", Content = "Save as new custom node", IsVisible = !createNew };
        saveAs.Click += (_, _) => Submit(true);
        var cancel = new Button { Name = "CustomCoachCancel", Content = "Cancel" }; cancel.Click += (_, _) => Close(null);
        var footer = new StackPanel { Spacing = 8, Children = {
            status,
            new TextBlock { Text = createNew ? "Save and use also saves a reusable library entry, even if you later discard this map draft." : "Edits affect this placement only. Save as new also creates a reusable library entry.", TextWrapping = TextWrapping.Wrap },
            new WrapPanel { Children = { save, saveAs, cancel } }
        } };
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(20), RowSpacing = 12 };
        layout.Children.Add(header);
        var scroll = new ScrollViewer { Content = rows, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 1); layout.Children.Add(scroll); Grid.SetRow(footer, 2); layout.Children.Add(footer);
        layout.Height = 750;
        Content = new ScrollViewer { Content = layout, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        SizeChanged += (_, _) => layout.Height = Math.Max(700, ClientSize.Height - 40);
        search.TextChanged += (_, _) => RefreshRows(); special.IsCheckedChanged += (_, _) => RefreshRows();
        Closed += (_, _) => { if (preview.Source is IDisposable bitmap) bitmap.Dispose(); preview.Source = null; };
        RefreshImage(); RefreshRows();
    }
    private void RefreshImage()
    {
        if (preview.Source is IDisposable previous) previous.Dispose(); preview.Source = null;
        if (draft.ImagePng.Length > 0) { using var stream = new MemoryStream(Convert.FromBase64String(draft.ImagePng)); preview.Source = new Bitmap(stream); }
        preview.IsVisible = preview.Source != null;
    }
    private void RefreshRows()
    {
        rows.Children.Clear(); string query = search.Text?.Trim() ?? "";
        var visible = entries.Values.Where(e => special.IsChecked == true || draft.Relics.Contains(e.Key) || custom.ContainsKey(e.Key) || RewardCatalog.OfferedInBasePool("Relics", e.Key))
            .Where(e => e.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || e.Description.Contains(query, StringComparison.OrdinalIgnoreCase)).OrderBy(e => e.Name).ToArray();
        foreach (string missing in draft.Relics.Where(id => !entries.ContainsKey(id)).ToArray()) Add(missing, "Unavailable relic: " + missing, "Remove or replace this selection before saving.");
        foreach (var entry in visible) Add(entry.Key, entry.Name, entry.Description);
        if (rows.Children.Count == 0) rows.Children.Add(new TextBlock { Text = "No matching relics. Read game data from Home if the catalog is empty.", TextWrapping = TextWrapping.Wrap });
        Counts();
        void Add(string id, string name, string description) {
            var checkbox = new CheckBox { Tag = "coach-relic:" + id, IsChecked = draft.Relics.Contains(id), HorizontalAlignment = HorizontalAlignment.Stretch,
                Content = RewardRow.Create(name, description) };
            checkbox.IsCheckedChanged += (_, _) => { if (checkbox.IsChecked == true) draft.Relics.Add(id); else draft.Relics.Remove(id); Counts(); };
            rows.Children.Add(checkbox);
        }
    }
    private void Counts() => count.Text = $"{draft.Relics.Count} selected · up to 3 eligible choices per offer · upgrades must also be selected";
    private void Submit(bool saveLibrary)
    {
        try {
            draft.Name = coachName.Text?.Trim() ?? "";
            draft.Rewards.Clear();
            foreach (string id in draft.Relics) if (custom.TryGetValue(id, out var item)) draft.Rewards.Add(item.Copy());
            CustomCoachCatalog.Validate(root, campaign, draft);
            Close(saveLibrary ? draft.SaveNew(CustomCoachCatalog.Library(root)) : draft.Copy());
        } catch (Exception ex) { status.Text = ex.Message; }
    }
    internal static async Task<CustomCoachDefinition?> Choose(Window owner, string root, string campaign)
    {
        var dialog = new Window { Title = "Custom node", Width = 430, Height = 250, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var create = new Button { Content = "Create new", Name = "CustomCoachCreate" };
        var load = new Button { Content = "Load saved", Name = "CustomCoachLoad" };
        var cancel = new Button { Content = "Cancel" };
        create.Click += (_, _) => dialog.Close("create"); load.Click += (_, _) => dialog.Close("load"); cancel.Click += (_, _) => dialog.Close(null);
        dialog.Content = new StackPanel { Margin = new Thickness(24), Spacing = 12, Children = {
            new TextBlock { Text = "Custom nodes", FontSize = 25 }, new TextBlock { Text = "Create or reuse a Coach with its own relic pool.", TextWrapping = TextWrapping.Wrap }, create, load, cancel
        } };
        string? action = await dialog.ShowDialog<string?>(owner);
        if (action == "create") return await new CustomCoachEditor(root, campaign).ShowDialog<CustomCoachDefinition?>(owner);
        if (action != "load") return null;
        var saved = CustomCoachCatalog.Load(root, out string errors);
        var picker = new Window { Title = "Saved Custom Coaches", Width = 550, Height = 620, MinHeight = 400, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var rows = new StackPanel { Spacing = 10 }; var bitmaps = new List<IDisposable>();
        foreach (var coach in saved) {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
            if (coach.ImagePng.Length > 0) {
                try { using var stream = new MemoryStream(Convert.FromBase64String(coach.ImagePng)); var bitmap = new Bitmap(stream); bitmaps.Add(bitmap); row.Children.Add(new Image { Source = bitmap, Width = 70, Height = 60, Stretch = Stretch.Uniform }); }
                catch (Exception ex) { errors += "\n" + coach.Name + ": " + ex.Message; continue; }
            }
            row.Children.Add(new TextBlock { Text = coach.Name + $"\n{coach.Relics.Count} selected relics", TextWrapping = TextWrapping.Wrap, MaxWidth = 340 });
            var use = new Button { Content = row, Tag = "saved-coach:" + coach.Id, HorizontalAlignment = HorizontalAlignment.Stretch };
            use.Click += (_, _) => picker.Close(coach.Copy()); rows.Children.Add(use);
        }
        if (rows.Children.Count == 0) rows.Children.Add(new TextBlock { Text = "No saved Custom Coaches yet.", TextWrapping = TextWrapping.Wrap });
        if (errors.Length > 0) rows.Children.Add(new TextBlock { Text = errors, TextWrapping = TextWrapping.Wrap });
        bool createInstead = false;
        var newButton = new Button { Content = "Create new", Name = "CustomCoachCreate" }; newButton.Click += (_, _) => { createInstead = true; picker.Close(null); };
        var close = new Button { Content = "Cancel" }; close.Click += (_, _) => picker.Close(null);
        rows.Children.Add(newButton); rows.Children.Add(close); picker.Content = new ScrollViewer { Content = rows, Margin = new Thickness(20) };
        CustomCoachDefinition? result;
        try { result = await picker.ShowDialog<CustomCoachDefinition?>(owner); }
        finally { foreach (var bitmap in bitmaps) bitmap.Dispose(); }
        return createInstead ? await new CustomCoachEditor(root, campaign).ShowDialog<CustomCoachDefinition?>(owner) : result;
    }
}
