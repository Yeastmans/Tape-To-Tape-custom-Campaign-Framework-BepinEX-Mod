using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace CampaignCreator;

public sealed partial class MainWindow
{
    private void ShowExportReport(ExportReport report)
    {
        if (report.Skipped.Count == 0) return;
        status.Text += $" Skipped {report.Skipped.Count} unavailable selections.";
        var dialog = new Window { Title = "Export complete", Width = 640, Height = 420,
            WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new DockPanel { Margin = new Thickness(20), LastChildFill = true };
        var heading = new TextBlock {
            Text = "Export succeeded. These unavailable selections were left out of the exported copy. Your saved players and team are unchanged.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) };
        DockPanel.SetDock(heading, Dock.Top); panel.Children.Add(heading);
        var close = new Button { Content = "Done", HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0) };
        close.Click += (_, _) => dialog.Close();
        DockPanel.SetDock(close, Dock.Bottom); panel.Children.Add(close);
        panel.Children.Add(new ScrollViewer { Content = new SelectableTextBlock {
            Text = string.Join("\n", report.Skipped), TextWrapping = TextWrapping.Wrap } });
        dialog.Content = panel;
        _ = dialog.ShowDialog(this);
    }
}
