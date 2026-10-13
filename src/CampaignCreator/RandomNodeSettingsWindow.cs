using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using CampaignCore;

namespace CampaignCreator;

public sealed class RandomNodeSettingsWindow : Window
{
    public RandomNodeSettingsWindow(MapLayoutNode node)
    {
        Title = "Edit random node"; Width = 540; Height = 650; MinHeight = 450;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = StudioTheme.Background;
        var panel = new StackPanel { Spacing = StudioTheme.SpaceLg };
        panel.Children.Add(new TextBlock { Text = "Random node choices", FontSize = 24, FontWeight = FontWeight.Bold });
        panel.Children.Add(new TextBlock { Text = "Choose the events this node can pick from. Higher weights are more likely; unchecked events are excluded.", TextWrapping = TextWrapping.Wrap });
        bool missingNative = node.RandomWeights == null && node.NativeRandomWeights == null && node.SourceLayer >= 0 && node.SourceNode >= 0;
        IReadOnlyDictionary<string, decimal> initial = node.RandomWeights ?? node.NativeRandomWeights
            ?? (missingNative ? new Dictionary<string, decimal>() : RandomNodePool.Defaults);
        var explanation = new TextBlock { TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(explanation);
        if (missingNative) panel.Children.Add(new TextBlock { Text = "The vanilla pool for this node is unavailable. Read the current game data and import it again to load its vanilla choices, or choose an explicit custom pool below.", TextWrapping = TextWrapping.Wrap });
        var inputs = new Dictionary<string, NumericUpDown>();
        var chances = new Dictionary<string, TextBlock>();
        var warning = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brush.Parse("#FFCB80") };
        var apply = new Button { Content = "Apply", IsDefault = true, Padding = new Thickness(20, 9) }; StudioTheme.Primary(apply);
        void Update()
        {
            decimal total = inputs.Values.Sum(i => i.Value ?? 0);
            bool valid = inputs.Values.All(i => i.Value is >= 0 and <= 1000000) && total > 0;
            apply.IsEnabled = valid; warning.Text = valid ? "" : "Give at least one event a weight greater than 0.";
            foreach (var pair in inputs) chances[pair.Key].Text = total > 0 ? $"{(pair.Value.Value ?? 0) / total:P1}" : "0%";
            bool unchangedNative = node.RandomWeights == null && node.NativeRandomWeights != null
                && inputs.All(p => (p.Value.Value ?? 0) == node.NativeRandomWeights.GetValueOrDefault(p.Key));
            explanation.Text = unchangedNative
                ? "Vanilla choices for this node. Keeping them unchanged preserves native placement rules. Percentages show pool shares; the game can further limit eligible events."
                : "Custom choices use these roll odds. Repeats are allowed, and native placement, Nurse and Solo Blademaster limits are overridden.";
        }
        foreach (var entry in RandomNodePool.Defaults)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,130,65"), ColumnSpacing = StudioTheme.SpaceMd };
            var selected = new CheckBox { Name = "RandomChoice_" + entry.Key, Tag = entry.Key,
                Content = entry.Key switch { "RandomConditioning" => "Random conditioning", "FanNumber1" => "Fan Number 1", _ => entry.Key },
                IsChecked = initial.GetValueOrDefault(entry.Key) > 0, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(selected);
            var input = new NumericUpDown { Tag = entry.Key, Minimum = 0, Maximum = 1000000, Increment = 1,
                Value = initial.GetValueOrDefault(entry.Key), IsEnabled = selected.IsChecked == true, FormatString = "0.##" };
            var chance = new TextBlock { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
            inputs.Add(entry.Key, input); chances.Add(entry.Key, chance);
            Grid.SetColumn(input, 1); Grid.SetColumn(chance, 2); row.Children.Add(input); row.Children.Add(chance); panel.Children.Add(row);
            decimal lastPositive = input.Value > 0 ? input.Value.Value : 1;
            bool syncing = false;
            selected.IsCheckedChanged += (_, _) =>
            {
                if (syncing) return;
                syncing = true;
                input.IsEnabled = selected.IsChecked == true;
                input.Value = selected.IsChecked == true ? lastPositive : 0;
                syncing = false; Update();
            };
            input.ValueChanged += (_, _) =>
            {
                if (syncing) return;
                syncing = true;
                if (input.Value > 0) lastPositive = input.Value.Value;
                selected.IsChecked = input.Value > 0;
                input.IsEnabled = selected.IsChecked == true;
                syncing = false; Update();
            };
        }
        panel.Children.Add(new TextBlock { Text = "Apply keeps these choices in the draft. Save maps to use changes in a new run. Already generated maps keep their events.", TextWrapping = TextWrapping.Wrap });
        var cancel = new Button { Content = "Cancel", Padding = new Thickness(20, 9) };
        cancel.Click += (_, _) => Close();
        apply.Click += (_, _) => { if (apply.IsEnabled) Close(inputs.ToDictionary(p => p.Key, p => p.Value.Value!.Value)); };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
        var footer = new StackPanel { Spacing = StudioTheme.SpaceMd, Margin = new Thickness(0, 12, 0, 0), Children = {
            warning, new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = StudioTheme.SpaceMd, Children = { cancel, apply } } } };
        var layout = new DockPanel { Margin = new Thickness(24) };
        DockPanel.SetDock(footer, Dock.Bottom); layout.Children.Add(footer);
        layout.Children.Add(new ScrollViewer { Content = panel });
        Content = layout; Update();
    }
}
