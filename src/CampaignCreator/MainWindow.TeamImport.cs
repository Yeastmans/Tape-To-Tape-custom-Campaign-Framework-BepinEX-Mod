using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace CampaignCreator;

public sealed partial class MainWindow
{
    private async Task<bool> ConfirmTeamImport()
    {
        var dialog = new Window { Title = "Import another team", Width = 510, Height = 225,
            CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Background };
        var keep = new Button { Content = "Keep editing", IsDefault = true, Padding = new Thickness(14, 9) };
        var import = new Button { Content = "Discard edits and import", Padding = new Thickness(14, 9) };
        keep.Click += (_, _) => dialog.Close(false);
        import.Click += (_, _) => dialog.Close(true);
        dialog.KeyDown += (_, e) => { if (e.Key == Key.Escape) { dialog.Close(false); e.Handled = true; } };
        dialog.Content = new StackPanel { Margin = new Thickness(24), Spacing = StudioTheme.SpaceLg, Children = {
            new TextBlock { Text = "This team has unsaved edits", FontSize = 22, FontWeight = FontWeight.Bold },
            new TextBlock { Text = "Importing opens the selected team and discards your unsaved edits. Your previously saved team stays unchanged.", TextWrapping = TextWrapping.Wrap },
            new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = StudioTheme.SpaceMd, Children = { keep, import } }
        } };
        return await dialog.ShowDialog<bool>(this);
    }
}
