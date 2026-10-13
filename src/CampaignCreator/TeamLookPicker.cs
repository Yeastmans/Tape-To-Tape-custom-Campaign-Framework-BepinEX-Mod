using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace CampaignCreator;

internal static class TeamLookPicker
{
    internal static async Task<string?> Pick(Window owner, string root)
    {
        var dialog = new Window { Name = "FillTeamLookDialog", Title = "Fill team look", Width = 720, Height = 610,
            MinWidth = 660, MinHeight = 580, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        string[] sources = [ContentImport.BaseGame, ContentImport.InGame, ContentImport.CampaignCustom];
        var tabs = new TabControl { Name = "FillLookSourceTabs",
            ItemsSource = sources.Select(s => new TabItem { Header = s }).ToArray(), SelectedIndex = 0 };
        var hint = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var search = new TextBox { Watermark = "Search teams" };
        var list = new ListBox { Name = "FillLookTeams", Height = 240 };
        var message = new TextBlock { Foreground = Brush.Parse("#FFD18C"), TextWrapping = TextWrapping.Wrap };
        var fill = new Button { Name = "FillLookSelected", Content = "Fill look from selected", IsEnabled = false };
        StudioTheme.Primary(fill);
        var cancel = new Button { Content = "Cancel" };
        void Refresh()
        {
            string selected = (tabs.SelectedItem as TabItem)?.Header?.ToString() ?? ContentImport.BaseGame;
            hint.Text = selected switch {
                ContentImport.BaseGame => "Teams shipped with the game and included with Studio.",
                ContentImport.InGame => "Teams created in the game's editor or exported to the game.",
                _ => "Teams saved in your campaigns and Studio exports." };
            var entries = ContentImport.Entries(root, "team", selected)
                .Where(e => e.Name.Contains(search.Text ?? "", StringComparison.OrdinalIgnoreCase)).ToArray();
            list.ItemsSource = entries;
            list.SelectedItem = null;
            fill.IsEnabled = false;
            message.Text = entries.Length > 0 ? "Choose a team to copy its look."
                : search.Text?.Length > 0 ? "No matching teams."
                : selected == ContentImport.InGame ? "No custom teams in the game yet. Make one in the game's editor or export a team from Studio."
                : "No teams in this tab yet.";
        }
        tabs.SelectionChanged += (_, _) => Refresh();
        search.TextChanged += (_, _) => Refresh();
        list.SelectionChanged += (_, _) => fill.IsEnabled = list.SelectedItem is ContentImport.Entry;
        fill.Click += (_, _) => { if (list.SelectedItem is ContentImport.Entry entry) dialog.Close(entry.File); };
        cancel.Click += (_, _) => dialog.Close(null);
        dialog.Content = new StackPanel { Margin = new Thickness(24), Spacing = StudioTheme.SpaceMd, Children = {
            new TextBlock { Text = "Fill team look", FontSize = 24 },
            new TextBlock { Text = "Copies home and away equipment and colors only. Team name, logo, roster, squad setup and other settings stay as they are.",
                TextWrapping = TextWrapping.Wrap },
            tabs, hint, search, list, message,
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = StudioTheme.SpaceMd,
                Children = { fill, cancel } }
        } };
        Refresh();
        return await dialog.ShowDialog<string?>(owner);
    }
}
