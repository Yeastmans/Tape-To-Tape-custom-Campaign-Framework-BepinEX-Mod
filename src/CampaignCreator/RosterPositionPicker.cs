using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace CampaignCreator;

internal static class RosterPositionPicker
{
    internal sealed record Choice(string Position, string? Occupant, bool Enabled);

    internal static async Task<string?> Show(Window owner, string player, string current, IReadOnlyList<Choice> choices)
    {
        bool secondLine = current.StartsWith("Line 2 ", StringComparison.OrdinalIgnoreCase);
        string Display(string position) => position.Replace("Line 2 ", "", StringComparison.OrdinalIgnoreCase);
        var dialog = new Window { Name = "RosterPositionDialog", Title = "Change skater position", Width = 540, Height = 370,
            MinWidth = 420, MinHeight = 340, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var items = choices.Select(choice => new ComboBoxItem {
            Tag = choice, IsEnabled = choice.Enabled,
            Content = Display(choice.Position) + " · " + (choice.Occupant ?? (choice.Enabled ? "Empty" : "Filled by squad setup"))
        }).ToArray();
        var picker = new ComboBox { Name = "RosterPositionChoice", ItemsSource = items,
            SelectedItem = items.FirstOrDefault(item => ((Choice)item.Tag!).Position.Equals(current, StringComparison.OrdinalIgnoreCase)), HorizontalAlignment = HorizontalAlignment.Stretch };
        var explanation = new TextBlock { Name = "RosterPositionSummary", TextWrapping = TextWrapping.Wrap, Foreground = StudioTheme.Muted };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Padding = StudioTheme.ButtonPadding };
        var apply = new Button { Name = "ApplyRosterPosition", Content = "Move player", Padding = StudioTheme.ButtonPadding };
        StudioTheme.Primary(apply);
        void Refresh()
        {
            var choice = (picker.SelectedItem as ComboBoxItem)?.Tag as Choice;
            bool selected = choice != null && choice.Enabled && !choice.Position.Equals(current, StringComparison.OrdinalIgnoreCase);
            apply.IsEnabled = selected;
            apply.Content = selected && choice!.Occupant != null ? "Swap players" : "Move player";
            explanation.Text = !selected ? "Choose a different position on this line. Goalies keep their own slot."
                : player + " → " + Display(choice!.Position)
                    + (choice.Occupant == null ? "" : "\n" + choice.Occupant + " → " + Display(current))
                    + "\n\nSave the team to keep this change.";
        }
        picker.SelectionChanged += (_, _) => Refresh();
        cancel.Click += (_, _) => dialog.Close();
        apply.Click += (_, _) => {
            if ((picker.SelectedItem as ComboBoxItem)?.Tag is Choice { Enabled: true } choice && !choice.Position.Equals(current, StringComparison.OrdinalIgnoreCase))
                dialog.Close(choice.Position);
        };
        var body = new Grid { Margin = new Thickness(20), RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"), RowSpacing = 16 };
        body.Children.Add(new StackPanel { Spacing = 6, Children = {
            new TextBlock { Text = player, FontSize = 22, TextWrapping = TextWrapping.Wrap },
            new TextBlock { Text = $"Line {(secondLine ? 2 : 1)} · Current position: {Display(current)}", Foreground = StudioTheme.Muted }
        } });
        Grid.SetRow(picker, 1); body.Children.Add(picker);
        Grid.SetRow(explanation, 2); body.Children.Add(explanation);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 12, Children = { cancel, apply } };
        Grid.SetRow(buttons, 3); body.Children.Add(buttons);
        dialog.Content = body; Refresh();
        return await dialog.ShowDialog<string?>(owner);
    }
}
