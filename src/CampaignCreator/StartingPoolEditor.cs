using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CampaignCore;

namespace CampaignCreator;

/// <summary>Authored starting choices, with separate saved native/custom bench tabs.</summary>
public sealed partial class StartingPoolEditor : UserControl
{
    public const string BaseMarker = "Studio Base Pool Player";
    private const string SourceMarker = "Studio Player Source";
    private readonly string root, folder;
    private readonly Func<string> campaign;
    private readonly Window owner;
    private readonly Action<string> edit;
    private readonly Action changed;
    private readonly GameData data;
    private readonly List<Bitmap> portraits = [];
    private readonly string?[] customSlots;
    private readonly string?[] baseSlots = new string?[9];
    private readonly Dictionary<string, string> baseSources = new(StringComparer.OrdinalIgnoreCase);
    private readonly Grid customCards = new() { ColumnDefinitions = new ColumnDefinitions("*,*,*"), ColumnSpacing = StudioTheme.SpaceLg, RowSpacing = StudioTheme.SpaceLg };
    private readonly Grid baseCards = new() { ColumnDefinitions = new ColumnDefinitions("*,*,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto"), ColumnSpacing = StudioTheme.SpaceLg, RowSpacing = StudioTheme.SpaceLg };
    private readonly TextBlock count = new() { Name = "StartingPoolCount", TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold };
    private CheckBox? useBaseGame;
    private Button? bulkImport;
    private NativePlayerPoolEditor? nativePool;
    private bool changedState;
    public bool HasChanges { get => changedState || nativePool?.HasChanges == true; private set => changedState=value; }
    private string DirectoryPath => Path.Combine(campaign(), "player_teams", folder);
    private bool Bench => folder == "draft_pool";
    public StartingPoolEditor(Window owner, string root, Func<string> campaign, string folder, Action<string> edit, Action changed)
    {
        this.owner = owner; this.root = root; this.campaign = campaign; this.folder = folder; this.edit = edit; this.changed = changed;
        data = new GameData(root);
        if (VanillaCopyData.IsPool(DirectoryPath))
        {
            customSlots=[];
            nativePool=new NativePlayerPoolEditor(owner,root,()=>DirectoryPath,edit,changed);
            Content=nativePool;return;
        }
        customSlots = new string?[Bench ? 9 : 3];
        Name = Bench ? "StartingBenchPool" : "StartingSuperstarPool";
        customCards.Name = Bench ? "CustomBenchCards" : "SuperstarCards";
        customCards.RowDefinitions = new RowDefinitions(Bench ? "Auto,Auto,Auto" : "Auto");
        var selected = SelectedPlayerPool.Read(DirectoryPath) ?? (Directory.Exists(DirectoryPath) ? Directory.GetFiles(DirectoryPath, "*.txt") : []);
        var custom = selected.Where(f => File.Exists(f) && (!Bench || new CampaignDocument(File.ReadAllText(f)).Get(BaseMarker).Length == 0)).ToArray();
        for (int i = 0; i < customSlots.Length && i < custom.Length; i++) customSlots[i] = Path.GetFileName(custom[i]);
        RestoreSlots("custom-slots.list", customSlots);
        var panel = new StackPanel { Spacing = StudioTheme.SpaceMd };
        bulkImport = Button("Bulk import players", async () => await ImportLibraryPlayers());
        bulkImport.Name = Bench ? "BulkImportBench" : "BulkImportSuperstars";
        if (Bench)
        {
            panel.Children.Add(new TextBlock { Text = PoolChoiceGuidance.Bench, TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(count);
            var nativeNames = NativeStartingPlayers.Names("draft_pool");
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in BaseFiles())
            {
                var doc = new CampaignDocument(File.ReadAllText(file));
                if (!nativeNames.Contains(doc.Get("Name")) || !seen.Add(doc.Get("Name"))) continue;
                baseSources["base-" + Path.GetFileName(file)] = file;
            }
            int index = 0;
            foreach (string key in baseSources.Keys.Take(9)) baseSlots[index++] = key;
            RestoreSlots("base-slots.list", baseSlots);
            bool nativeMode = File.Exists(Path.Combine(DirectoryPath, "source.mode"))
                ? File.ReadAllText(Path.Combine(DirectoryPath, "source.mode")).Trim() == "base"
                : custom.Length == 0 && (selected.Length > 0 || !File.Exists(Path.Combine(DirectoryPath, SelectedPlayerPool.Manifest)));
            useBaseGame = new CheckBox { Name = "UseBaseGameBench", Content = "Use base-game bench", IsChecked = nativeMode };
            var customView = Frame(new StackPanel { Spacing = StudioTheme.SpaceMd, Children = { bulkImport, customCards } }); var baseView = Frame(baseCards);
            customView.IsVisible = !nativeMode; baseView.IsVisible = nativeMode;
            useBaseGame.IsCheckedChanged += (_, _) => {
                baseView.IsVisible = useBaseGame.IsChecked == true; customView.IsVisible = !baseView.IsVisible;
                HasChanges = true; changed(); UpdateCount();
            };
            panel.Children.Add(useBaseGame);
            panel.Children.Add(new TextBlock { Text = "Uncheck to use your custom bench. Each view keeps its own choices; only the visible one is used.", TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(baseView); panel.Children.Add(customView); RefreshBaseCards();
        }
        else
        {
            panel.Children.Add(new TextBlock { Text = PoolChoiceGuidance.Superstars, TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(count);
            panel.Children.Add(new TextBlock { Text = "Kit colors come from the player team selected for the run; each superstar can choose its own stick and skate colors, and its own equipment styles with Team equipment off.", TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(bulkImport);
            panel.Children.Add(customCards);
            if (custom.Length > 3) panel.Children.Add(new TextBlock { Text = "This older pool has more than three choices. Saving changes uses the three shown here; other player files remain available to import.", TextWrapping = TextWrapping.Wrap });
        }
        Content = panel; RefreshCustomCards();
    }
    private static Border Frame(Control content) => new() { Padding = new Thickness(8), BorderBrush = StudioTheme.Border, BorderThickness = new Thickness(1), Background = StudioTheme.Surface, CornerRadius = new CornerRadius(6), Child = content };
    private static Button Button(string title, Action action)
    {
        var b = new Button { Content = title, Padding = StudioTheme.ButtonPadding, HorizontalAlignment = HorizontalAlignment.Stretch }; b.Click += (_, _) => action(); return b;
    }
    private IEnumerable<string> BaseFiles()
    {
        string? players = StudioArt.BaseRosters("Base Game Players");
        return players != null ? Directory.GetFiles(players, "*.txt") : [];
    }
    private void RestoreSlots(string name, string?[] slots)
    {
        string file = Path.Combine(DirectoryPath, name);
        if (!File.Exists(file)) return;
        var entries = File.ReadAllLines(file); SelectedPlayerPool.Validate(entries.Where(e => e.Length > 0), slots.Length);
        Array.Clear(slots);
        for (int i = 0; i < slots.Length && i < entries.Length; i++)
            if (entries[i].Length > 0 && (File.Exists(Path.Combine(DirectoryPath, entries[i])) || baseSources.ContainsKey(entries[i]))) slots[i] = entries[i];
    }
    private void UpdateCount()
    {
        if (bulkImport != null)
        {
            int empty = customSlots.Count(s => s == null);
            bulkImport.IsEnabled = empty > 0;
            ToolTip.SetTip(bulkImport, empty > 0 ? $"Import up to {empty} players into empty slots." : "All slots are full. Remove a player to make room.");
        }
        bool native = Bench && useBaseGame?.IsChecked == true;
        var slots = native ? baseSlots : customSlots;
        int filled = slots.Count(s => s != null);
        count.Text = $"{filled} / {slots.Length} " + (Bench ? "draft candidate slots filled · " + (native ? "Base game" : "Custom")
            : "superstar slots filled" + (filled == 0 ? " · Superstar screen will be skipped." : filled < 3 ? " · Players repeat to fill the offer." : " · 3 different choices."));
    }
    private async Task ImportLibraryPlayers()
    {
        try
        {
            await ContentImport.PickPlayers(owner, root, customSlots.Count(s => s == null), sources =>
            {
                var imported = PlayerPoolImport.IntoSlots(root, campaign(), folder, customSlots, sources);
                Array.Copy(imported, customSlots, customSlots.Length);
                HasChanges = true; changed(); RefreshCustomCards();
            });
        }
        catch (Exception ex) { count.Text = ex.Message; }
    }
    private void RefreshBaseCards()
    {
        baseCards.Children.Clear();
        var choices = baseSources.Select(p => new NativeChoice(p.Key, new CampaignDocument(File.ReadAllText(p.Value)).Get("Name"))).ToArray();
        for (int i = 0; i < 9; i++)
        {
            int slot = i;
            var picker = new ComboBox { ItemsSource = choices, SelectedItem = choices.FirstOrDefault(c => c.Key == baseSlots[slot]), PlaceholderText = "Choose base-game player", HorizontalAlignment = HorizontalAlignment.Stretch };
            picker.SelectionChanged += (_, _) => {
                string? key = (picker.SelectedItem as NativeChoice)?.Key;
                int other = key == null ? -1 : Array.IndexOf(baseSlots, key);
                if (other >= 0 && other != slot) baseSlots[other] = baseSlots[slot];
                baseSlots[slot] = key; HasChanges = true; changed(); RefreshBaseCards(); UpdateCount();
            };
            var body = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
            if (baseSlots[slot] is string selected && baseSources.TryGetValue(selected, out string? source))
            {
                var player = new CampaignDocument(File.ReadAllText(source));
                body.Children.Add(new TextBlock { Text = player.Get("Name", "Player"), FontSize = 16, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
                var details = PlayerDetails(player); Grid.SetRow(details, 1); body.Children.Add(details);
            }
            Grid.SetRow(picker, 2); body.Children.Add(picker);
            AddCard(baseCards, body, i, "BaseBenchCard", baseSlots[slot] != null);
        }
    }
    private sealed record NativeChoice(string Key, string Name) { public override string ToString() => Name; }
    private Control PlayerDetails(CampaignDocument player)
    {
        var details = new Grid { ColumnDefinitions = new ColumnDefinitions("68,*"), ColumnSpacing = StudioTheme.SpaceMd, Margin = new Thickness(0, 10) };
        Control portrait = new TextBlock { Text = "#" + player.Get("Number", "—"), FontSize = 24, Foreground = StudioTheme.Accent,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        string? preview = data.HeadPreview("skater", player.Get("Face"));
        if (preview != null)
        {
            var bitmap = new Bitmap(preview); portraits.Add(bitmap);
            portrait = new Image { Source = bitmap, Stretch = Stretch.Uniform };
        }
        details.Children.Add(RosterSelectionCard.Portrait(portrait));
        var attributes = new StackPanel { Spacing = StudioTheme.SpaceSm, Children = {
            new TextBlock { Text = "Ability · " + RewardCatalog.DisplayNames(root, "Abilities", player.Get("Ability")), FontSize = 12, TextWrapping = TextWrapping.Wrap },
            new TextBlock { Text = "Talents · " + RewardCatalog.DisplayNames(root, "Talents", player.Get("Talents")), FontSize = 12, TextWrapping = TextWrapping.Wrap } } };
        Grid.SetColumn(attributes, 1); details.Children.Add(attributes);
        return details;
    }
    private static void AddCard(Grid grid, Control body, int slot, string prefix, bool filled)
    {
        bool bench = prefix.Contains("Bench");
        var slotCard = new StackPanel { Spacing = StudioTheme.SpaceSm };
        slotCard.Children.Add(RosterSelectionCard.Badge((bench ? "Bench " : "Superstar ") + (slot + 1)));
        slotCard.Children.Add(RosterSelectionCard.Shell(body, !filled));
        var card = new Border { Name = prefix + (slot + 1), Child = slotCard };
        Grid.SetColumn(card, slot % 3); Grid.SetRow(card, slot / 3); grid.Children.Add(card);
    }
    public void Save()
    {
        if(nativePool!=null){nativePool.Save();return;}
        if (!HasChanges && File.Exists(Path.Combine(DirectoryPath, SelectedPlayerPool.Manifest))) return;
        Directory.CreateDirectory(DirectoryPath);
        if (Bench)
        {
            foreach (string key in baseSlots.Where(s => s != null).Select(s => s!))
            {
                string target = Path.Combine(DirectoryPath, key);
                if (!File.Exists(target)) { var doc = ContentImport.ReadDocument(baseSources[key]); doc.Set(BaseMarker, doc.Get("Name")); doc.Save(target); }
            }
            File.WriteAllLines(Path.Combine(DirectoryPath, "base-slots.list"), baseSlots.Select(s => s ?? ""));
        }
        File.WriteAllLines(Path.Combine(DirectoryPath, "custom-slots.list"), customSlots.Select(s => s ?? ""));
        var active = Bench && useBaseGame?.IsChecked == true ? baseSlots : customSlots;
        SelectedPlayerPool.Save(DirectoryPath, active.Where(s => s != null).Select(s => s!), Bench ? 9 : 3);
        if (Bench) File.WriteAllText(Path.Combine(DirectoryPath, "source.mode"), useBaseGame?.IsChecked == true ? "base" : "custom");
        HasChanges = false;
    }
}
