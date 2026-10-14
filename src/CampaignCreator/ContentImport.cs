using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using CampaignCore;

namespace CampaignCreator;

public static partial class ContentImport
{
    public static CampaignDocument ReadDocument(string file)
    {
        var doc = new CampaignDocument(File.ReadAllText(file));
        new UpgradeCleanup().CleanImportedValues(doc, IsGoalie(doc, file) ? "goalie" : "skater");
        return doc;
    }

    internal static void ValidateCopiedFile(string file)
    {
        if (!Path.GetExtension(file).Equals(".txt", StringComparison.OrdinalIgnoreCase) && Path.GetFileName(file) != "goalie-equipment.cfg") return;
        var doc = new CampaignDocument(File.ReadAllText(file));
        if (!doc.Fields.ContainsKey("Name") && !doc.Fields.ContainsKey("Team Name") && Path.GetFileName(file) != "goalie-equipment.cfg") return;
        new UpgradeCleanup().CleanImportedValues(doc, IsGoalie(doc, file) || Path.GetFileName(file) == "goalie-equipment.cfg" ? "goalie" : "skater");
        doc.Save(file);
    }

    public const string BaseGame = "Base game", InGame = "In-game custom", CampaignCustom = "Campaign custom";
    public const string PlayerTeamLibrary = "Player team library";
    public const string PlayerLibrary = "Player library";
    public sealed record Entry(string Name, string File, string Source) { public override string ToString() => Name; }
    public static bool IsGoalie(CampaignDocument doc, string file) => doc.Get("Export Role") == "goalie" || doc.Get("Position").Contains("Goalie", StringComparison.OrdinalIgnoreCase)
        || doc.Fields.ContainsKey("Catching") || doc.Fields.ContainsKey("Goalie Skill") || Path.GetFileName(file).Contains("goalie", StringComparison.OrdinalIgnoreCase);
    public static bool Matches(string file, string kind)
    {
        var doc = new CampaignDocument(System.IO.File.ReadAllText(file));
        bool team = Path.GetFileName(file).Equals("team.txt", StringComparison.OrdinalIgnoreCase) || doc.Fields.ContainsKey("Team Name");
        return kind == "team" ? team : !team && (doc.Fields.ContainsKey("Name") || doc.Fields.ContainsKey("Import Player"))
            && (kind == "player" || IsGoalie(doc, file) == (kind == "goalie"));
    }
    public static List<Entry> Entries(string root, string kind, string source)
    {
        // Every copy the game holds is listed, dated, even when names repeat.
        if (source == InGame)
            return GameCustomContent.Load(root).Where(i => Matches(i.File, kind)).Select(i =>
            {
                var doc = new CampaignDocument(System.IO.File.ReadAllText(i.File));
                string name = doc.Get(kind == "team" ? "Team Name" : "Name", Path.GetFileNameWithoutExtension(i.File));
                return new Entry(name + (kind == "team" ? "" : IsGoalie(doc, i.File) ? " · Goalie" : " · Skater") + " · " + i.Detail, i.File, source);
            }).OrderBy(e => e.Name).ToList();
        // Studio exports live in the game, so they are listed only under In-game custom.
        var folders = source == PlayerTeamLibrary ? new[] { "player_teams" }
            : source == PlayerLibrary ? new[] { "players" }
            : source == BaseGame ? new[] { "Base Game Teams", "Base Game Players" }
            : kind == "team" ? new[] { "teams", "players" } : new[] { "teams", "players", "player_teams" };
        // Base game comes only from the snapshot decoded from the player's game files (StudioArt).
        // Runtime dumps in the library held campaign edits and custom players.
        var locations = source == BaseGame
            ? folders.Select(f => StudioArt.BaseRosters(f)).OfType<string>()
            : folders.Select(f => Path.Combine(root, "library", f));
        var files = locations.Where(Directory.Exists)
            .SelectMany(f => Directory.EnumerateFiles(f, "*.txt", SearchOption.AllDirectories));
        bool custom = source is CampaignCustom or "Custom";
        if (custom && Directory.Exists(Path.Combine(root, "campaigns")))
            files = files.Concat(Directory.EnumerateFiles(Path.Combine(root, "campaigns"), "*.txt", SearchOption.AllDirectories)
                .Where(f => !Path.GetRelativePath(Path.Combine(root, "campaigns"), f).Split(Path.DirectorySeparatorChar).Any(p => p.StartsWith('_') || p.StartsWith('.'))));
        var result = new List<Entry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string file in files.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Matches(file, kind)) continue;
            var doc = new CampaignDocument(System.IO.File.ReadAllText(file));
            if (custom && (doc.Get(FreeAgentContent.Marker).Length > 0 || doc.Get(StartingPoolEditor.BaseMarker).Length > 0)) continue;
            string name = doc.Get(kind == "team" ? "Team Name" : "Name", doc.Get("Import Player", Path.GetFileNameWithoutExtension(file)));
            if (!custom && source != PlayerLibrary && !seen.Add(name + "|" + (kind == "team" ? "team" : IsGoalie(doc, file) ? "goalie" : "skater"))) continue;
            string detail = Path.GetFileName(Path.GetDirectoryName(file))!;
            result.Add(new Entry(name + (kind == "team" ? "" : IsGoalie(doc, file) ? " · Goalie" : " · Skater") + (detail == name ? "" : " · " + detail), file, source));
        }
        return result.OrderBy(e => e.Name).ToList();
    }
    public static async Task<string?> Pick(Window owner, string root, string kind, string? initialSource = null)
    {
        var dialog = new Window { Title = "Import " + kind, Width = 720, Height = 650, MinWidth = 660, MinHeight = 620, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var sources = kind == "team" ? new[] { PlayerTeamLibrary, BaseGame, InGame, CampaignCustom } : new[] { BaseGame, InGame, CampaignCustom };
        var source = new TabControl { Name = "ImportSourceTabs", ItemsSource = sources.Select(s => new TabItem { Header = s }).ToArray(), SelectedIndex = Math.Max(0, Array.IndexOf(sources, initialSource ?? BaseGame)) };
        var hint = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var search = new TextBox { Watermark = "Search " + kind + "s" };
        var list = new ListBox { Height = 240 };
        var message = new TextBlock { Text = "Nothing selected — choose an entry to import.", Foreground = Brush.Parse("#FFD18C"), TextWrapping = TextWrapping.Wrap };
        var import = new Button { Content = "Import selected", IsEnabled = false }; StudioTheme.Primary(import);
        var browse = new Button { Content = "Import from file…" };
        void Refresh()
        {
            string selected = (source.SelectedItem as TabItem)?.Header?.ToString() ?? BaseGame;
            hint.Text = selected switch {
                PlayerTeamLibrary => "Saved player teams. Import copies the full team and roster into this campaign; the library original stays separate.",
                BaseGame => "Read from your installed Tape to Tape using Read game artwork on Home. Import copies the editable roster fields. Copy vanilla campaign retains native team and player identity checks; an ordinary import does not grant them.",
                InGame => "Custom teams and players saved in the game, made in its editor or exported from Studio. Read from the game's save folder each time; every copy is listed with its date.",
                _ => "Your campaign content and your Studio library. Exporting to the game keeps your campaign copy." };
            var entries = Entries(root, kind, selected).Where(e => e.Name.Contains(search.Text ?? "", StringComparison.OrdinalIgnoreCase)).ToArray();
            list.ItemsSource = entries; list.SelectedItem = null; import.IsEnabled = false;
            message.Text = entries.Length > 0 ? "Nothing selected — choose an entry to import." : search.Text?.Length > 0 ? "No matching entries." : selected == InGame ? "No custom teams or players in the game yet. Make one in the game's editor or export one from Studio." : "No content in this tab yet.";
        }
        list.SelectionChanged += (_, _) => { import.IsEnabled = list.SelectedItem is Entry; message.Text = import.IsEnabled ? "Missing source values stay unselected. Complete required fields before saving." : "Nothing selected — choose an entry to import."; };
        source.SelectionChanged += (_, _) => Refresh(); search.TextChanged += (_, _) => Refresh();
        import.Click += (_, _) => { if (list.SelectedItem is Entry entry) dialog.Close(entry.File); };
        browse.Click += async (_, _) => {
            try {
                var picked = await dialog.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Import " + kind, AllowMultiple = false, FileTypeFilter = [new FilePickerFileType("Campaign content") { Patterns = kind == "team" ? ["team.txt"] : ["*.txt"] }] });
                string? file = picked.FirstOrDefault()?.TryGetLocalPath(); if (file == null) return;
                if (!Matches(file, kind)) throw new ArgumentException("Choose a " + kind + " file for this destination.");
                dialog.Close(file);
            } catch (Exception ex) { message.Text = ex.Message; }
        };
        dialog.Content = new StackPanel { Margin = new Thickness(24), Spacing = StudioTheme.SpaceMd, Children = { new TextBlock { Text = "Import " + kind, FontSize = 24 }, source, hint, search, list, message, new StackPanel { Orientation = Orientation.Horizontal, Spacing = StudioTheme.SpaceMd, Children = { import, browse } } } };
        Refresh(); return await dialog.ShowDialog<string?>(owner);
    }
}
