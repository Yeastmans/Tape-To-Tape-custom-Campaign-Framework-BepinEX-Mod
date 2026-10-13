using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace CampaignCreator;

internal sealed partial class TeamEditorView
{
    private Control SquadIconPicker()
    {
        const string key = "Squad Head"; // Keep existing campaign files compatible.
        string saved = Value(key);
        string folder = Path.Combine(GameSavePaths.Root, "CustomLogos");
        var image = new Image { Width = 64, Height = 64, Stretch = Stretch.Uniform };
        var caption = new TextBlock { FontSize = 11, Foreground = Muted, TextTrimming = TextTrimming.CharacterEllipsis };
        Bitmap? bitmap = null;
        void Preview()
        {
            image.Source = null; bitmap?.Dispose(); bitmap = null;
            string? path = saved.StartsWith("CustomLogos/", StringComparison.OrdinalIgnoreCase)
                ? Path.Combine(folder, Path.GetFileName(saved)) : data.HeadPreview("skater", saved);
            if (path == null && saved.Length > 0 && Path.GetFileName(saved) == saved)
                path = Path.Combine(folder, saved.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? saved : saved + ".png");
            try { if (path != null && File.Exists(path)) image.Source = bitmap = new Bitmap(path); }
            catch (Exception) { /* Preserve saved values even if their artwork cannot be decoded. */ }
            caption.Text = saved.Length == 0 ? "Choose a squad icon" : FieldControls.Friendly(Path.GetFileNameWithoutExtension(saved));
        }
        Track(key, () => saved);
        var choose = ActionButton("Choose squad icon", async () =>
        {
            var heads = CampaignCore.HeadAppearance.Choices(data.Assets.Values("skater", "Face"))
                .Select(id => new PreviewGridPicker.Choice(id, FieldControls.Friendly(id)));
            var logos = (Directory.Exists(folder) ? Directory.GetFiles(folder, "*.png").OrderBy(Path.GetFileName).ToArray() : [])
                .Select(path => new PreviewGridPicker.Choice("CustomLogos/" + Path.GetFileName(path), Path.GetFileNameWithoutExtension(path)));
            var choices = new[] { new PreviewGridPicker.Choice("", "Nothing selected") }.Concat(heads).Concat(logos);
            string? selected = await PreviewGridPicker.Show(owner, "Choose squad icon", choices, saved,
                id => PreviewGridPicker.FileImage(id.StartsWith("CustomLogos/", StringComparison.OrdinalIgnoreCase)
                    ? Path.Combine(folder, Path.GetFileName(id)) : data.HeadPreview("skater", id)));
            if (selected != null && selected != saved) { saved = selected; Preview(); changed(); }

        });
        // Image on the right keeps the label and button left-aligned with the rows above.
        var panel = new Grid { ColumnDefinitions = new ColumnDefinitions("*,64"), ColumnSpacing = StudioTheme.SpaceMd };
        var content = new StackPanel { Spacing = 3, Children = { Label("SQUAD ICON"), choose, caption } };
        panel.Children.Add(content);
        Grid.SetColumn(image, 1); panel.Children.Add(image);
        panel.DetachedFromVisualTree += (_, _) => { image.Source = null; bitmap?.Dispose(); bitmap = null; };
        panel.AttachedToVisualTree += (_, _) => Preview();
        Preview();
        return panel;
    }

    private Control SquadMascotPicker()
    {
        var choices = new List<MascotOption>();
        using var stream = typeof(App).Assembly.GetManifestResourceStream("CampaignCreator.SquadMascots.tsv")!;
        using var reader = new StreamReader(stream);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.ReadLine() is { } line)
        {
            var cells = line.Split('\t');
            if (cells.Length == 3 && seen.Add(cells[2]))
                choices.Add(new(cells[0], SquadTemplateCatalog.Find(cells[0])?.Name ?? cells[1]));
        }
        string saved = Value("Mascot");
        if (saved.Length > 0 && !choices.Any(c => c.Id.Equals(saved, StringComparison.OrdinalIgnoreCase)))
            choices.Add(new(saved, saved + " (saved value)"));
        var combo = new ComboBox { Tag = "Mascot", ItemsSource = choices,
            SelectedItem = choices.FirstOrDefault(c => c.Id.Equals(saved, StringComparison.OrdinalIgnoreCase)),
            PlaceholderText = "Choose a mascot",
            HorizontalAlignment = HorizontalAlignment.Stretch };
        Track("Mascot", () => saved);
        combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is MascotOption option) { saved = option.Id; changed(); } };
        return new StackPanel { Spacing = 3, Children = { Label("MASCOT"), combo,
            new TextBlock { Text = "Base-game squad mascot (plushie).", FontSize = 11, Foreground = Muted, TextWrapping = TextWrapping.Wrap } } };
    }
    private sealed record MascotOption(string Id, string Name)
    {
        public override string ToString() => Name;
    }

    private Control SquadHeadPicker(string key, string label, string hint)
    {
        var image = new Image { Width = 64, Height = 64, Stretch = Stretch.Uniform };
        Bitmap? bitmap = null;
        void Preview(string value)
        {
            image.Source = null;
            bitmap?.Dispose(); bitmap = null;
            if (data.HeadPreview("skater", value) is { } path)
                image.Source = bitmap = new Bitmap(path);
        }
        var field = FieldControls.Create(key, Value(key), "skater", data, changed, Preview);
        field.control.Tag = key;
        Track(key, field.read);
        Preview(field.read());
        var panel = new Grid { ColumnDefinitions = new ColumnDefinitions("*,64"), ColumnSpacing = StudioTheme.SpaceMd };
        var content = new StackPanel { Spacing = 3, Children = {
            Label(label), field.control,
            new TextBlock { Text = hint, FontSize = 11, Foreground = Muted, TextWrapping = TextWrapping.Wrap }
        } };
        panel.Children.Add(content);
        Grid.SetColumn(image, 1); panel.Children.Add(image);
        panel.DetachedFromVisualTree += (_, _) => { image.Source = null; bitmap?.Dispose(); bitmap = null; };
        panel.AttachedToVisualTree += (_, _) => Preview(field.read());
        return panel;
    }
}
