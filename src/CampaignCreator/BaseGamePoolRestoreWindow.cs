using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace CampaignCreator;

public sealed class BaseGamePoolRestoreWindow : Window
{
    public BaseGamePoolRestoreWindow(string root, Action refreshed)
    {
        Name = "BaseGamePoolBackupWindow"; Title = "Restore Base Game Pools backup"; Width = 800; Height = 560; MinWidth = 640; MinHeight = 440;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = StudioTheme.Background;
        var list = new ListBox { Name = "BaseGamePoolBackupList", Background = StudioTheme.Input,
            ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<BaseGamePoolBackup>((item, _) => new StackPanel { Spacing = 4, Children = {
                new TextBlock { Text = item?.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") + " · " + item?.Reason, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = item == null ? "" : item.Error.Length > 0 ? item.Error : Summary(item.Pools), Foreground = StudioTheme.Muted, TextWrapping = TextWrapping.Wrap }
            } }) };
        var feedback = new TextBlock { Name = "BaseGamePoolBackupFeedback", Foreground = StudioTheme.Muted, TextWrapping = TextWrapping.Wrap };
        bool busy = false;
        Button restore = null!, refresh = null!, close = null!;
        void Controls() { restore.IsEnabled = !busy && list.SelectedItem is BaseGamePoolBackup { Error.Length: 0 }; refresh.IsEnabled = close.IsEnabled = list.IsEnabled = !busy; }
        void Refresh()
        {
            var backups = BaseGamePoolPackages.Backups(root); list.ItemsSource = backups;
            feedback.Text = backups.Count == 0 ? "No backups yet. Downloading a pool set creates one automatically." : "Choose a dated backup. Restoring it first backs up your current pools, so you can undo the restore.";
        }
        restore = MakeButton("Restore selected backup", "RestoreBaseGamePoolBackup", async () =>
        {
            if (busy || list.SelectedItem is not BaseGamePoolBackup selected) return;
            busy = true; Controls();
            try
            {
                if (!await Confirm(this, selected.Pools, restore: true, date: selected.CreatedUtc)) { feedback.Text = "Cancelled. Your current pools were kept."; return; }
                feedback.Text = "Backing up the current setup and restoring your selection…";
                await Task.Run(() => BaseGamePoolPackages.Restore(root, selected.Id));
                refreshed(); Refresh();
                feedback.Text = "Backup restored. Restart the game to use it. The setup you replaced is now another backup."; feedback.Foreground = Brush.Parse("#70DEA2");
            }
            catch (Exception ex) { feedback.Text = "Could not restore: " + ex.Message; feedback.Foreground = Brush.Parse("#FFD18C"); }
            finally { busy = false; Controls(); }
        });
        StudioTheme.Primary(restore);
        refresh = MakeButton("Refresh", "RefreshBaseGamePoolBackups", () => { try { Refresh(); } catch (Exception ex) { feedback.Text = ex.Message; } Controls(); return Task.CompletedTask; });
        close = MakeButton("Close", "CloseBaseGamePoolBackups", () => { Close(); return Task.CompletedTask; });
        list.SelectionChanged += (_, _) => Controls();
        var body = new Grid { Margin = new Thickness(24), RowDefinitions = new("Auto,*,Auto,Auto"), RowSpacing = 16 };
        body.Children.Add(new TextBlock { Text = "Restore Base Game Pools backup", FontSize = 26, FontWeight = FontWeight.Bold });
        Grid.SetRow(list, 1); body.Children.Add(list); Grid.SetRow(feedback, 2); body.Children.Add(feedback);
        var actions = new WrapPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { refresh, close, restore } };
        foreach (var button in new[] { refresh, close, restore }) button.Margin = new Thickness(0, 0, 10, 6);
        Grid.SetRow(actions, 3); body.Children.Add(actions); Content = body;
        Closing += (_, e) => { if (busy) e.Cancel = true; };
        try { Refresh(); } catch (Exception ex) { feedback.Text = ex.Message; } Controls();
    }

    internal static string Summary(BaseGamePoolInfo? info) => info == null ? "No saved pool selections; restores the previous native/default setup."
        : $"Bench / draft: {info.BenchCount} · Free agents: {info.FreeAgentCount} · Superstars: {info.SuperstarCount}" + (info.Enabled ? "" : " · Disabled");

    internal static async Task<bool> Confirm(Window owner, BaseGamePoolInfo? info, bool restore, DateTime? date = null)
    {
        var dialog = new Window { Name = "ConfirmBaseGamePoolReplacement", Title = restore ? "Restore pool backup?" : "Replace Base Game Pools?", Width = 620, Height = 330, MinWidth = 480, MinHeight = 280, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = StudioTheme.Background };
        bool result = false;
        var cancel = MakeButton("Cancel", "CancelBaseGamePoolReplacement", () => { dialog.Close(); return Task.CompletedTask; });
        var apply = MakeButton(restore ? "Restore backup" : "Replace all three pools", "ConfirmBaseGamePoolReplacementButton", () => { result = true; dialog.Close(); return Task.CompletedTask; });
        var text = new StackPanel { Spacing = 14, Children = {
            new TextBlock { Text = restore ? "Restore backup from " + date?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") + "?" : "Install " + info?.Name + "?", FontSize = 22, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap },
            new TextBlock { Text = Summary(info), TextWrapping = TextWrapping.Wrap, Foreground = StudioTheme.Muted },
            new TextBlock { Text = "This replaces your current bench/draft, free-agent and superstar pools. Your current setup will be saved under Restore backup first. Existing runs and campaign selection are kept.", TextWrapping = TextWrapping.Wrap }
        } };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, apply } };
        var layout = new Grid { Margin = new Thickness(24), RowDefinitions = new("*,Auto"), RowSpacing = 16, Children = { new ScrollViewer { Content = text } } };
        Grid.SetRow(buttons, 1); layout.Children.Add(buttons); dialog.Content = layout;
        dialog.Opened += (_, _) => cancel.Focus();
        await dialog.ShowDialog(owner); return result;
    }
    private static Button MakeButton(string text, string name, Func<Task> action)
    {
        var button = new Button { Content = text, Name = name, Padding = new Thickness(12, 8) };
        button.Click += async (_, _) => await action(); return button;
    }
}
