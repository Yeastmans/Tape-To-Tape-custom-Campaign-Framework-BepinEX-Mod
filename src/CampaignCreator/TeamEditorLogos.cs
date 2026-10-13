using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using CampaignCore;

namespace CampaignCreator;

internal static class LogoPicker
{
    private static readonly IBrush Muted = StudioTheme.Muted;
    private static Button ActionButton(string text, Action action) { var button = new Button { Content = text }; button.Click += (_,_) => action(); return button; }
    // Source locates the preview image; Value is what the file stores. Base-game
    // logos store the team name so the game resolves its own native artwork.
    private sealed record LogoChoice(string Label, string Source, string? Stored = null)
    {
        public string Value => Stored ?? Source;
        public override string ToString() => Label;
    }

    internal static (Control control, Func<string> read) Create(string root, string value, Action changed,
        string emptyCaption = "No logo selected", string title = "Choose team logo", bool inline = false)
    {
        string saved = value;

        var caption = new TextBlock { Text = saved.Length == 0 ? emptyCaption : Path.GetFileNameWithoutExtension(saved), TextTrimming = TextTrimming.CharacterEllipsis };
        Button? choose = null;
        choose = ActionButton("Choose logo", async () =>
        {
            if (TopLevel.GetTopLevel(choose) is not Window owner) return;
            var dialog = new Window { Title = title, Width = 940, Height = 650, MinWidth = 540, MinHeight = 400,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = StudioTheme.Surface };
            var preview = new Image { Width = 140, Height = 140, Stretch = Stretch.Uniform };
            var feedback = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Muted };
            Bitmap? bitmap = null;
            LogoChoice? pending = saved.Length == 0 ? null : new(caption.Text ?? saved, saved);
            void Preview(LogoChoice choice)
            {
                pending = choice; preview.Source = null; bitmap?.Dispose(); bitmap = null;
                try { preview.Source = bitmap = LogoExport.Preview(root, choice.Source); feedback.Text = bitmap == null ? "Preview unavailable for " + choice.Label : choice.Label; }
                catch (Exception ex) { feedback.Text = ex.Message; }
            }
            var thumbnails = new List<Bitmap>();
            var thumbnailTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<LogoChoice>((choice, _) => {
                Bitmap? thumb = null;
                try { if (choice != null) thumb = LogoExport.Preview(root, choice.Source); } catch (Exception) { }
                if (thumb != null) thumbnails.Add(thumb);
                return new StackPanel { Width = 132, Height = 148, Spacing = 6, Children = {
                    thumb == null ? (Control)new TextBlock { Text = "Preview unavailable", Height = 100, TextWrapping = TextWrapping.Wrap }
                        : new Image { Source = thumb, Height = 100, Stretch = Stretch.Uniform },
                    new TextBlock { Text = choice?.Label, TextWrapping = TextWrapping.Wrap, MaxLines = 2 }
                } };
            });
            var gridTemplate = new Avalonia.Controls.Templates.FuncTemplate<Panel>(() => new WrapPanel());
            var native = new ListBox { Name = "BaseGameLogos", ItemTemplate = thumbnailTemplate, ItemsPanel = gridTemplate };
            var custom = new ListBox { Name = "CustomFolderLogos", ItemTemplate = thumbnailTemplate, ItemsPanel = gridTemplate };
            var catalog = Path.Combine(root, "_game_team_logos.txt");
            var names = File.Exists(catalog) ? File.ReadLines(catalog).Where(n => !string.IsNullOrWhiteSpace(n) && !n.StartsWith('#')).Select(n => n.Trim()).ToList() : new List<string>();
            if (names.Count == 0) names.AddRange(StudioArt.LogoTeams());
            native.ItemsSource = names.Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Order().Select(n => new LogoChoice(n, LogoExport.FindFile(root, n) ?? n, n)).ToArray();
            void RefreshCustom()
            {
                custom.ItemsSource = new[] { LogoExport.CustomFolder, Path.Combine(root, "logos") }
                    .Where(Directory.Exists).SelectMany(f => Directory.EnumerateFiles(f, "*.png"))
                    .Where(f => !Path.GetDirectoryName(f)!.Equals(LogoExport.CustomFolder, StringComparison.OrdinalIgnoreCase) || !names.Contains(Path.GetFileNameWithoutExtension(f), StringComparer.OrdinalIgnoreCase))
                    .OrderBy(Path.GetFileName).Select(f => new LogoChoice(Path.GetFileNameWithoutExtension(f), f)).ToArray();
            }
            ScrollViewer.SetHorizontalScrollBarVisibility(native, Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);
            ScrollViewer.SetHorizontalScrollBarVisibility(custom, Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);
            RefreshCustom();
            native.SelectionChanged += (_, _) => { if (native.SelectedItem is LogoChoice c) Preview(c); };
            custom.SelectionChanged += (_, _) => { if (custom.SelectedItem is LogoChoice c) Preview(c); };
            var customPage = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = StudioTheme.SpaceSm };
            customPage.Children.Add(new TextBlock { Text = "PNG images from your CustomLogos folder and imported logos.", TextWrapping = TextWrapping.Wrap });
            Grid.SetRow(custom, 1); customPage.Children.Add(custom);
            var nativePage = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = StudioTheme.SpaceSm };
            nativePage.Children.Add(new TextBlock { Text = names.Count == 0 ? "No base-game logo catalog found. Launch the game with the mod to refresh it." : "Choose a base-game team logo.", TextWrapping = TextWrapping.Wrap });
            Grid.SetRow(native, 1); nativePage.Children.Add(native);
            var sources = new TabControl { ItemsSource = new[] {
                new TabItem { Header = "Base game", Content = nativePage },
                new TabItem { Header = "Custom logos folder", Content = customPage }
            }, SelectedIndex = native.Items.Cast<LogoChoice>().Any(c => c.Source == saved || c.Label == saved) ? 0 : 1 };
            sources.SelectionChanged += (_, e) => {
                if (e.Source != sources) return;
                var selected = (sources.SelectedIndex == 0 ? native : custom).SelectedItem as LogoChoice;
                if (selected != null) Preview(selected);
                else { pending = null; preview.Source = null; bitmap?.Dispose(); bitmap = null; feedback.Text = "Choose a logo from this list."; }
            };
            var body = new Grid { ColumnDefinitions = new ColumnDefinitions("*,170"), ColumnSpacing = StudioTheme.SpaceLg };
            body.Children.Add(sources);
            var details = new StackPanel { Spacing = StudioTheme.SpaceMd, Children = { preview, feedback } };
            Grid.SetColumn(details, 1); body.Children.Add(details);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = StudioTheme.SpaceMd };
            buttons.Children.Add(ActionButton("Import PNG", async () =>
            {
                try
                {
                    var picked = await dialog.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Import team logo", AllowMultiple = false,
                        FileTypeFilter = new[] { new FilePickerFileType("PNG logo") { Patterns = new[] { "*.png" } } } });
                    if (picked.Count == 0) return;
                    string path = picked[0].Path.LocalPath;
                    var source = new CampaignDocument(""); source.Set("Logo From", path);
                    var snapshot = LogoExport.Snapshot(source, root);
                    string folder = Path.Combine(root, "logos"); Directory.CreateDirectory(folder);
                    string target = Path.Combine(folder, Path.GetFileNameWithoutExtension(path) + "-" + snapshot.Get("Export Logo SHA256")[..12] + ".png");
                    File.WriteAllBytes(target, Convert.FromBase64String(snapshot.Get("Export Logo PNG")));
                    RefreshCustom(); sources.SelectedIndex = 1;
                    custom.SelectedItem = custom.Items.Cast<LogoChoice>().First(c => c.Source == target);
                }
                catch (Exception ex) { feedback.Text = ex.Message; }
            }));
            buttons.Children.Add(ActionButton("Cancel", () => dialog.Close()));
            buttons.Children.Add(ActionButton("Use logo", () =>
            {
                if (pending == null) { feedback.Text = "Choose a logo first."; return; }
                if (bitmap == null) { feedback.Text = "This logo has no usable image. Import its PNG or refresh the game catalog."; return; }
                saved = pending.Value; caption.Text = pending.Label;
                changed(); dialog.Close();
            }));
            var shell = new Grid { Margin = new Thickness(20), RowDefinitions = new RowDefinitions("*,Auto"), RowSpacing = StudioTheme.SpaceLg };
            shell.Children.Add(body); Grid.SetRow(buttons, 1); shell.Children.Add(buttons); dialog.Content = shell;
            if (pending != null) Preview(pending);
            try { await dialog.ShowDialog(owner); }
            finally { preview.Source = null; bitmap?.Dispose(); dialog.Content = null; foreach (var thumbnail in thumbnails) thumbnail.Dispose(); }
        });
        // Inline: button and caption on one row, for a toolbar.
        if (inline) { caption.VerticalAlignment = VerticalAlignment.Center; caption.MaxWidth = 260; }
        return (new StackPanel { Orientation = inline ? Orientation.Horizontal : Orientation.Vertical,
            Spacing = inline ? StudioTheme.SpaceMd : StudioTheme.SpaceXs, Children = { choose, caption } }, () => saved);
    }
}
