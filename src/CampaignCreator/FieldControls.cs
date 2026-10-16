using System.Globalization;

using Avalonia;

using Avalonia.Controls;

using Avalonia.Controls.Templates;

using Avalonia.Layout;

using Avalonia.Media;

using CampaignCore;



namespace CampaignCreator;



internal static class FieldControls

{
    internal static readonly IBrush EmptySelectionBrush = Brush.Parse("#FF6B6B");

    internal static (Control control, Func<string> read) Create(string key, string value, string role, GameData data, Action changed, Action<string> preview, bool opponentTeam = false, bool fillHeight = false, bool talentsPopout = false)

    {

        bool required = EditorRequirements.Required(false, role).Concat(EditorRequirements.Required(true, role)).Contains(key);
        var hint = new TextBlock { Foreground = required ? EmptySelectionBrush : StudioTheme.Muted, FontSize = 11, TextWrapping = TextWrapping.Wrap };

        Func<string>? reader = null;
        string previous = value;

        // Keep the hint measured even when filled so nearby controls do not move or resize.
        void Refresh() { bool missing = string.IsNullOrWhiteSpace(reader?.Invoke() ?? value); hint.Opacity = missing ? 1 : 0; hint.Text = required ? "Required — nothing selected" : "Nothing selected (optional)"; }

        var field = Build(key, value, role, data, () => {
            Refresh();
            if (reader == null || reader() == previous) return;
            previous = reader(); changed();
        }, preview, opponentTeam, fillHeight, talentsPopout);

        reader = field.read; previous = reader(); Refresh();

        var wrapper = new Grid { Tag = key, RowDefinitions = new RowDefinitions(fillHeight ? "*,Auto" : "Auto,Auto"), RowSpacing = 3 };
        wrapper.Children.Add(field.control); Grid.SetRow(hint, 1); wrapper.Children.Add(hint);
        return (wrapper, field.read);

    }

    private static (Control control, Func<string> read) Build(string key, string value, string role, GameData data, Action changed, Action<string> preview, bool opponentTeam = false, bool fillHeight = false, bool talentsPopout = false)

    {

        // Literal identity/description text is not a random expression. Keep the
        // same text exceptions as import cleanup so opening a saved field does
        // not erase a name such as "Captain Random".
        if (!value.StartsWith("Helmet/CustomMasks/", StringComparison.OrdinalIgnoreCase)
            && UpgradeCleanup.RandomValue(value) && key is not ("Name" or "Team Name" or "Description" or "City" or "Abbreviation" or "Goal Sound" or "Goal Sound Data"
            or "Relics" or "Team Relics" or "Talents" or "Goalie Talents" or "Team Talents")) value = "";
        if (key == "Logo From") return LogoPicker.Create(data.Root, value, changed);
        var kind = FieldSchema.Kind(key, value);

        if (FieldSchema.Stats.Contains(key, StringComparer.OrdinalIgnoreCase))
            return Stat(value, changed);

        if (key == "Size Offset") return SizeOffset(value, changed);

        if (kind == FieldKind.Color)

        {

            string saved = value;

            var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = StudioTheme.SpaceSm };

            var picker = new ColorPicker { IsAlphaEnabled = false, IsAlphaVisible = false, IsColorPreviewVisible = false, IsAccentColorsVisible = false, Color = FieldSchema.TryRgb(value, out var r, out var g, out var b) ? Color.FromRgb(r,g,b) : Colors.White };

            var label = new TextBlock { Text = value.Length == 0 ? "Nothing selected" : value, VerticalAlignment = VerticalAlignment.Center };
            if (value.Length == 0) label.Foreground = EmptySelectionBrush;

            picker.ColorChanged += (_, e) => { saved = FieldSchema.Rgb(e.NewColor.R, e.NewColor.G, e.NewColor.B); label.Text = saved; label.ClearValue(TextBlock.ForegroundProperty); changed(); };

            panel.Children.Add(picker); panel.Children.Add(label);

            return (panel, () => saved);

        }

        if (key.Equals("Left Handed", StringComparison.OrdinalIgnoreCase))
        {
            var toggle = new ToggleSwitch { OnContent = "Left handed", OffContent = "Right handed",
                IsChecked = value.Equals("yes", StringComparison.OrdinalIgnoreCase) || value.Equals("true", StringComparison.OrdinalIgnoreCase) };
            toggle.IsCheckedChanged += (_, _) => changed();
            return (toggle, () => toggle.IsChecked == true ? "yes" : "no");
        }

        if (kind == FieldKind.Boolean && !new[] { "", "yes", "no", "true", "false" }.Contains(value, StringComparer.OrdinalIgnoreCase))

            return Choices(key.Equals("Left Handed", StringComparison.OrdinalIgnoreCase) ? new[] { "yes", "no" } : new[] { "yes", "no" }, key, value, changed, preview);

        if (kind == FieldKind.Boolean) return Choices(new[] { "yes", "no" }, key, value, changed, preview);

        if (kind == FieldKind.Number)

        {

            decimal? initial = decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

            bool fractional = key is "Size Offset" or "Pass Read" or "Stat Scale";

            decimal minimum = key == "Size Offset" ? -100 : 0, maximum = key == "Number" ? 99 : key == "Pass Read" ? 1 : 999;

            // Preserve authored random/range expressions and out-of-range legacy values until explicitly changed.

            if (value.Length > 0 && (initial == null || initial < minimum || initial > maximum)) return Choices(new[] { value }, key, value, changed, preview);

            string saved = value;

            var numeric = new NumericUpDown { Minimum = minimum, Maximum = maximum, Increment = fractional ? .1m : 1m, FormatString = key == "Pass Read" ? "0.#########" : fractional ? "0.##" : "0", Value = initial };
            if (key == "Pass Read") ToolTip.SetTip(numeric, "Native range 0–1. Zero uses normal pass reaction. Any positive value enables quick pass reading only when the current difficulty permits it; higher values do not scale this effect.");

            numeric.ValueChanged += (_, _) => { saved = numeric.Value?.ToString(CultureInfo.InvariantCulture) ?? ""; changed(); };

            return (numeric, () => saved);

        }

        var options = data.Assets.Values(role, key).ToList();

        if (key.Equals("Size", StringComparison.OrdinalIgnoreCase)) options = data.Sizes.ToList();

        if (key is "Squad Head" or "Zamboni Head" or "Bench Head") options = data.Assets.Values("skater", "Face").ToList();

        if ((role == "skater" && key == "Face") || key is "Squad Head" or "Zamboni Head" or "Bench Head")
        {
            options = HeadAppearance.Choices(options).ToList();
            value = HeadAppearance.Base(value);
        }

        if (key.Equals("Skin Color", StringComparison.OrdinalIgnoreCase)) options = ["light", "dark"];

        if (key.Equals("Left Handed", StringComparison.OrdinalIgnoreCase)) options = ["yes", "no"];

        if (key.Equals("Squad Template", StringComparison.OrdinalIgnoreCase) || key.Equals("Maps", StringComparison.OrdinalIgnoreCase)) options = data.Templates.ToList();

        if (key.Equals("Position", StringComparison.OrdinalIgnoreCase)) options = role == "goalie" ? ["Goalie"] : ["Center", "Left Wing", "Right Wing", "Left Defense", "Right Defense", "Line 2 Center", "Line 2 Left Wing", "Line 2 Right Wing", "Line 2 Left Defense", "Line 2 Right Defense"];

        if (key.Equals("Ability", StringComparison.OrdinalIgnoreCase))

        {

            var catalog = RewardCatalog.ReadEntries(data.Root, role == "goalie" ? "Goalie Abilities" : "Abilities")

                .GroupBy(p => p.Key, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Value, StringComparer.OrdinalIgnoreCase);

            value = RewardCatalog.ResolveKey(data.Root, role == "goalie" ? "Goalie Abilities" : "Abilities", value);

            return Choices(catalog.Keys.Append("none"), key, value, changed, preview,

                item => catalog.TryGetValue(item, out var entry) ? entry.Name : Friendly(item),

                item => catalog.TryGetValue(item, out var entry) ? RewardCatalog.Reminder(entry.Description) : "");

        }

        if (kind == FieldKind.Multiple)

        {

            bool relics = key.Contains("Relic", StringComparison.OrdinalIgnoreCase);

            var entries = relics ? RewardCatalog.ReadEntries(data.Root, "Relics").Values.AsEnumerable()

                : RewardCatalog.ReadEntries(data.Root, role == "goalie" ? "Goalie Talents" : "Talents").Values.AsEnumerable();

            var catalog = entries.GroupBy(e => e.Id.Length > 0 ? e.Key : relics ? e.Name : e.Key, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            foreach (var item in data.CustomRewards.Values.Where(d => !d.NeedsRedesign && d.IsRelic == relics && (relics || key == "Team Talents" || d.IsGoalie == (role == "goalie"))))
                catalog[item.Id] = new RewardCatalog.Entry(item.Id, item.Name, item.FullDescription, 1, item.Id, item.Kind);

            var usualKeys = catalog.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (!relics)

                foreach (var entry in RewardCatalog.ReadEntries(data.Root, role == "goalie" ? "Talents" : "Goalie Talents").Values)

                    catalog.TryAdd(entry.Key, entry);

            if (!relics)

                foreach (var name in data.Talents.Keys.Where(name => !data.CustomRewards.ContainsKey(data.Talents[name]) && !catalog.Values.Any(e => e.Asset == name || e.Id == name)))

                    catalog.TryAdd(name, new RewardCatalog.Entry(name, Friendly(name), "Loaded game talent outside the usual position catalog.", 1));

            var all = catalog.Keys.OrderBy(k => catalog[k].Name).ToList();

            bool randomPool = key is "Random Pool" or "Team Random Pool";

            if (randomPool) all.Insert(0,"all");

            var selected = value.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList();

            for (int i = 0; i < selected.Count; i++)

            {

                if (catalog.ContainsKey(selected[i])) continue;

                var matches = catalog.Values.Where(e => e.Id.Equals(selected[i], StringComparison.OrdinalIgnoreCase) || e.Asset.Equals(selected[i], StringComparison.OrdinalIgnoreCase) || e.Name.Equals(selected[i], StringComparison.OrdinalIgnoreCase)).ToArray();

                if (matches.Length == 1) selected[i] = matches[0].Key;

            }

            bool UnsupportedRelic(string item) => opponentTeam && relics &&
                (item.Equals("Abacus", StringComparison.OrdinalIgnoreCase)
                || item.Equals("Stat Reward Relic", StringComparison.OrdinalIgnoreCase)
                || item.Equals("0a29f353-8e00-4863-a1ee-7616457e6048", StringComparison.OrdinalIgnoreCase)
                || catalog.TryGetValue(item, out var relic) && relic.Asset.Equals("Stat Reward Relic", StringComparison.OrdinalIgnoreCase));
            // Keep imported reward titles even when this editor has no catalog entry.
            if (!randomPool)
                foreach (var item in selected.Where(s => s.Length > 0))
                    if (!all.Contains(item, StringComparer.OrdinalIgnoreCase)) all.Add(item);
            selected = selected.Where(s => all.Contains(s, StringComparer.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            string saved = string.Join(", ", selected);

            var panel = new StackPanel { Spacing = 6 };

            var search = new TextBox { Watermark = "Search choices and levels…" };

            var choices = new StackPanel { Spacing = 2 };
            Action updateSummary = () => { };


            void Refresh()

            {

                choices.Children.Clear();

                foreach (var item in all.Where(t => !UnsupportedRelic(t) || selected.Contains(t, StringComparer.OrdinalIgnoreCase)).Where(t => relics ||(randomPool && t == "all") || usualKeys.Contains(t) || selected.Contains(t, StringComparer.OrdinalIgnoreCase))

                    .Where(t => t.Contains(search.Text ?? "", StringComparison.OrdinalIgnoreCase) || (catalog.TryGetValue(t, out var e) && (e.Name + " " + e.Description + " " + e.Asset).Contains(search.Text ?? "", StringComparison.OrdinalIgnoreCase))))

                {

                    catalog.TryGetValue(item, out var entry);

                    var check = new CheckBox {

                        Content = RewardRow.Create(entry?.Name ?? item, UnsupportedRelic(item) ? "Does not affect opponent teams. Uncheck to remove." : entry != null ? RewardCatalog.Reminder(entry.Description) : item == "all" ? "Use every talent in the pool." : "Imported reward; not in the editor catalog.", importedTitle: entry == null && !randomPool),

                        IsChecked = selected.Contains(item, StringComparer.OrdinalIgnoreCase),

                        HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch

                    };

                    check.IsCheckedChanged += (_, _) => {

                        if (check.IsChecked == true) {

                            if(randomPool && item=="all") selected.Clear();

                            else if(randomPool) selected.RemoveAll(s=>s.Equals("all",StringComparison.OrdinalIgnoreCase));

                            if (!selected.Contains(item, StringComparer.OrdinalIgnoreCase)) selected.Add(item);

                        } else selected.RemoveAll(s => s.Equals(item, StringComparison.OrdinalIgnoreCase));

                        saved = string.Join(", ", selected); updateSummary(); changed();

                        if(randomPool || UnsupportedRelic(item)) Refresh();

                    };

                    choices.Children.Add(check);

                }

            }

            search.TextChanged += (_, _) => Refresh(); Refresh();

            if (talentsPopout)
            {
                var summary = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = StudioTheme.Muted, FontSize = 12 };
                void UpdateSummary() => summary.Text = selected.Count == 0 ? "Nothing selected" :
                    string.Join(", ", selected.Select(item => catalog.TryGetValue(item, out var entry) ? entry.Name : item));
                updateSummary = UpdateSummary;
                UpdateSummary();
                // A dialog window, like Import: a Flyout caps its width and clipped the descriptions.
                var open = new Button { Name = "PlayerTalentsSelector", Content = "Choose talents…",
                    HorizontalAlignment = HorizontalAlignment.Stretch, Padding = StudioTheme.ButtonPadding };
                open.Click += async (_, _) => {
                    if (TopLevel.GetTopLevel(open) is not Window owner) return;
                    var dialog = new Window { Title = key, Width = 720, Height = 610, MinWidth = 560, MinHeight = 420,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner };
                    var done = new Button { Content = "Done", HorizontalAlignment = HorizontalAlignment.Right }; StudioTheme.Primary(done);
                    done.Click += (_, _) => dialog.Close();
                    dialog.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Escape) { dialog.Close(); e.Handled = true; } };
                    var list = RewardRow.ListBox(choices);
                    var body = new Grid { Margin = new Thickness(24), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto"), RowSpacing = StudioTheme.SpaceMd };
                    Control[] rows = [
                        new TextBlock { Text = key, FontSize = 24 },
                        new TextBlock { Text = "Select any number of talents. Changes are kept when you close this window.", TextWrapping = TextWrapping.Wrap },
                        search, list, done ];
                    for (int i = 0; i < rows.Length; i++) { Grid.SetRow(rows[i], i); body.Children.Add(rows[i]); }
                    search.Text = ""; Refresh();
                    dialog.Content = body;
                    dialog.Opened += (_, _) => search.Focus();
                    // search and choices are reused by the next dialog, so release them from this one.
                    try { await dialog.ShowDialog(owner); }
                    finally { dialog.Content = null; body.Children.Clear(); ((ScrollViewer)list.Child!).Content = null; }
                };
                var compact = new StackPanel { Spacing = StudioTheme.SpaceXs, Children = { open, summary } };
                return (compact, () => saved);
            }


            panel.Children.Add(search); panel.Children.Add(RewardRow.ListBox(choices, 440));

            panel.Children.Add(new TextBlock { Text = all.Count == 0 ? "No game catalog found. Nothing selected." : "Game catalog entries; mode support must be checked in game.", FontSize = 11, TextWrapping = TextWrapping.Wrap });

            if (fillHeight)
            {
                var fitted = new Grid { RowDefinitions = new RowDefinitions(string.Join(",", panel.Children.Select(c => c is Border ? "*" : "Auto"))), RowSpacing = 6 };
                var children = panel.Children.ToArray(); panel.Children.Clear();
                for (int i = 0; i < children.Length; i++)
                {
                    if (children[i] is Border { Child: ScrollViewer scroll }) scroll.Height = double.NaN;
                    Grid.SetRow(children[i], i); fitted.Children.Add(children[i]);
                }
                return (fitted, () => saved);
            }
            return (panel, () => saved);

        }

        if (key is "Face" or "Squad Head" or "Zamboni Head" or "Bench Head")
            return PreviewGridPicker.Create("Choose head", new[] { new PreviewGridPicker.Choice("", "Nothing selected") }
                .Concat(options.Distinct(StringComparer.OrdinalIgnoreCase).Select(id => new PreviewGridPicker.Choice(id, Friendly(id)))),
                value, id => PreviewGridPicker.FileImage(data.HeadPreview("skater", id)), changed, preview);

        if (options.Count > 0 || kind == FieldKind.Choice) return Choices(options, key, value, changed, preview);

        var text = new TextBox { Text = value, Watermark = "Nothing selected" }; text.TextChanged += (_, _) => changed();

        return (text, () => text.Text ?? "");

    }

    private static (Control control, Func<string> read) Stat(string value, Action changed)
    {
        // Editor range includes boosted and custom stats, as requested.
        const int maximum = 999;
        string saved = value;
        bool updating = false;
        var input = new TextBox { Text = value, Watermark = "—", Width = 58, VerticalContentAlignment = VerticalAlignment.Center };
        var slider = new Slider { Minimum = 0, Maximum = maximum, TickFrequency = 1, IsSnapToTickEnabled = true,
            SmallChange = 1, LargeChange = 10, VerticalAlignment = VerticalAlignment.Center,
            Value = 0 };
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int initial) && initial >= 0)
        {
            slider.Value = Math.Min(maximum, initial);
        }
        slider.PropertyChanged += (_, e) => {
            if (updating || e.Property != Slider.ValueProperty) return;
            updating = true;
            saved = Math.Round(slider.Value).ToString(CultureInfo.InvariantCulture); input.Text = saved;
            updating = false; changed();
        };
        input.TextChanged += (_, _) => {
            if (updating) return;
            string text = input.Text ?? "";
            updating = true;
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
            {
                int clamped = Math.Clamp(number, 0, maximum);
                saved = clamped.ToString(CultureInfo.InvariantCulture); slider.Value = clamped;
                if (number != clamped) input.Text = saved;
            }
            else if (text.Length == 0) saved = "";
            updating = false; changed();
        };
        input.LostFocus += (_, _) => input.Text = saved;
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 6 };
        row.Children.Add(slider); Grid.SetColumn(input, 1); row.Children.Add(input);
        return (row, () => saved);
    }

    // The game multiplies the Size class's body scale by this (unclamped), so the
    // range runs well past the base game's own 0.9-1.1. Blank keeps the game's 1.0.
    private static (Control control, Func<string> read) SizeOffset(string value, Action changed)
    {
        const double minimum = 0.1, maximum = 5;
        string saved = value;
        bool updating = false;
        static string Format(double number) => Math.Round(number, 2).ToString("0.##", CultureInfo.InvariantCulture);
        var input = new TextBox { Text = value, Watermark = "1", Width = 58, VerticalContentAlignment = VerticalAlignment.Center };
        var slider = new Slider { Minimum = minimum, Maximum = maximum, TickFrequency = .05, IsSnapToTickEnabled = true,
            SmallChange = .05, LargeChange = .25, VerticalAlignment = VerticalAlignment.Center, Value = 1 };
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double initial) && initial > 0)
            slider.Value = Math.Clamp(initial, minimum, maximum);
        slider.PropertyChanged += (_, e) => {
            if (updating || e.Property != Slider.ValueProperty) return;
            updating = true;
            saved = Format(slider.Value); input.Text = saved;
            updating = false; changed();
        };
        // Clamp without rewriting the box mid-typing ("0." on the way to "0.5"); LostFocus shows the stored value.
        input.TextChanged += (_, _) => {
            if (updating) return;
            string text = input.Text ?? "";
            updating = true;
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) && number > 0)
            {
                double clamped = Math.Clamp(number, minimum, maximum);
                saved = Format(clamped); slider.Value = clamped;
            }
            else if (text.Length == 0) { saved = ""; slider.Value = 1; }
            updating = false; changed();
        };
        input.LostFocus += (_, _) => input.Text = saved;
        var reset = new Button { Content = "Normal", Padding = new Thickness(8, 4), VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(reset, "Back to the game's normal size (1). Base-game players sit between 0.9 and 1.1.");
        reset.Click += (_, _) => input.Text = "";
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 6 };
        row.Children.Add(slider); Grid.SetColumn(input, 1); row.Children.Add(input); Grid.SetColumn(reset, 2); row.Children.Add(reset);
        ToolTip.SetTip(slider, "Multiplies the player's size in game: 1 is normal, 2 is twice as big. Stacks with Size.");
        return (row, () => saved);
    }

    internal static string Friendly(string value)

    {

        if (value.Length == 0) return "None";

        string leaf = value[(value.LastIndexOf('/') + 1)..];

        if (leaf == "Customization_colors") return "Team colours";

        return System.Text.RegularExpressions.Regex.Replace(leaf.Replace('_', ' '), "(?<=[a-z])([A-Z])", " $1");

    }

    private static (Control, Func<string>) Choices(IEnumerable<string> available, string key, string value, Action changed, Action<string> preview, Func<string, string>? display = null, Func<string, string>? reminder = null)

    {

        var known = available.Where(item => !string.IsNullOrWhiteSpace(item)).ToList();

        var options = new[] { "" }.Concat(known.Where(item => !string.IsNullOrWhiteSpace(item))).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        string Label(string item)

        {

            if (item.Length == 0) return "Nothing selected";

            string name = display?.Invoke(item) ?? Friendly(item);

            if (item.Length > 0 && !known.Contains(item, StringComparer.OrdinalIgnoreCase)) return name + " (saved value; unavailable)";

            if (item.Contains('/') && options.Count(x => Friendly(x) == name) > 1) name += " · " + string.Join(" / ", item.Split('/').SkipLast(1).Select(Friendly));

            return name;

        }

        var combo = new StableComboBox { ItemsSource = options, SelectedItem = options.FirstOrDefault(x => x.Equals(value, StringComparison.OrdinalIgnoreCase)) ?? "", PlaceholderText = "Choose " + Friendly(key).ToLowerInvariant(), HorizontalAlignment = HorizontalAlignment.Stretch,

            ItemTemplate = new FuncDataTemplate<string>((item, _) => {
                if (reminder != null) return RewardRow.Create(Label(item ?? ""), reminder(item ?? ""));
                var text = new TextBlock { Text = Label(item ?? ""), TextTrimming = TextTrimming.CharacterEllipsis };
                if (string.IsNullOrEmpty(item)) text.Foreground = EmptySelectionBrush;
                return text;
            }),

            HorizontalContentAlignment = HorizontalAlignment.Stretch };

        string saved = combo.SelectedItem as string ?? "";

        combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is not string selected) return; saved = selected; changed(); preview(saved); };

        combo.GotFocus += (_, _) => preview(saved);

        return (combo, () => saved);

    }

}


