using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CampaignCore;

namespace CampaignCreator;

public sealed partial class MainWindow
{
    private bool teamExportPending;
    private sealed record TeamExportChoice(ExportTranslation.ExistingTeam? Target);

    private async Task<TeamExportChoice?> ChoosePlayNowExport(string teamName)
    {
        var targets = ExportTranslation.ExistingTeams(Path.Combine(GameSavePaths.Root, "TeamDataModels"),
            Path.Combine(UserContentPaths.Root, "play_now_overrides"), root);
        var dialog = new Window { Title = "Export team to Play Now", Width = 760, Height = 410,
            MinWidth = 560, MinHeight = 370, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var picker = new ComboBox { Name = "PlayNowExportTarget", ItemsSource = targets, SelectedIndex = -1,
            PlaceholderText = "Choose an exported team to update", HorizontalAlignment = HorizontalAlignment.Stretch,
            IsEnabled = targets.Count > 0 };
        var update = new Button { Name = "PlayNowUpdateTeam", Content = "Update selected team", IsEnabled = false };
        var create = new Button { Name = "PlayNowExportAsNew", Content = "Export as new" };
        var cancel = new Button { Name = "PlayNowExportCancel", Content = "Cancel" };
        bool submitted = false;
        void Complete(TeamExportChoice? choice)
        {
            if (submitted) return;
            submitted = true;
            picker.IsEnabled = update.IsEnabled = create.IsEnabled = cancel.IsEnabled = false;
            dialog.Close(choice);
        }
        picker.SelectionChanged += (_, _) => update.IsEnabled = !submitted && picker.SelectedItem is ExportTranslation.ExistingTeam;
        update.Click += (_, _) => { if (picker.SelectedItem is ExportTranslation.ExistingTeam target) Complete(new(target)); };
        create.Click += (_, _) => Complete(new(null));
        cancel.Click += (_, _) => Complete(null);
        var body = new StackPanel { Spacing = 16, Children = {
            new TextBlock { Text = "Export " + teamName, FontSize = 24, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
            new TextBlock { Text = "Create another Play Now team, or replace an exported team's data and roster. Your Studio team stays unchanged.", TextWrapping = TextWrapping.Wrap },
            new TextBlock { Text = "Update existing team", FontWeight = FontWeight.SemiBold },
            picker,
            new TextBlock { Text = targets.Count == 0 ? "No Studio-exported teams are available to update." : "Choose the exact export using its name, date and ID. Shared players in other teams are preserved.", TextWrapping = TextWrapping.Wrap, Foreground = StudioTheme.Muted },
            new TextBlock { Text = "Close the game before exporting, then restart it to load the changes.", TextWrapping = TextWrapping.Wrap }
        } };
        var layout = new Grid { Margin = new Thickness(24), RowDefinitions = new RowDefinitions("*,Auto") };
        layout.Children.Add(new ScrollViewer { Content = body,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        foreach (var action in new[] { cancel, create, update }) action.Margin = new Thickness(5, 4, 0, 0);
        var actions = new WrapPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0), Children = { cancel, create, update } };
        Grid.SetRow(actions, 1);
        layout.Children.Add(actions);
        dialog.Content = layout;
        return await dialog.ShowDialog<TeamExportChoice?>(this);
    }
}
