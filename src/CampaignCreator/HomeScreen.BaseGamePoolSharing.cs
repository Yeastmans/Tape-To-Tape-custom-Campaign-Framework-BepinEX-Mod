using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using CampaignCore;

namespace CampaignCreator;

public sealed partial class MainWindow
{
    private Control BuildBaseGamePoolCard()
    {
        var edit = Button("Edit pools", async () => await ShowBaseGamePools()); edit.Name = "HomeBaseGamePools"; StudioTheme.Primary(edit);
        var upload = Button("Upload", async () => await ShowBaseGamePoolUpload()); upload.Name = "HomeUploadBaseGamePools";
        upload.IsEnabled = File.Exists(Path.Combine(BaseGamePlayerPools.Folder(root), "settings.txt"));
        ToolTip.SetTip(upload, "Share your saved bench/draft, free-agent and superstar pools with their custom talents, heads and logos. Save your pools first. To update an existing listing, use Manage uploads.");
        var download = Button("Download", async () => await ShowBaseGamePoolDownload()); download.Name = "HomeDownloadBaseGamePools";
        var restore = Button("Restore backup", async () => await new BaseGamePoolRestoreWindow(root, ShowHome).ShowDialog(this)); restore.Name = "HomeRestoreBaseGamePoolsBackup";
        var manage = Button("Manage uploads", async () => await ShowCommunityManagement(pools: true)); manage.Name = "HomeManageBaseGamePoolsUploads";
        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var action in new[] { edit, upload, download, restore, manage }) { action.Margin = new Thickness(0, 0, 10, 8); actions.Children.Add(action); }
        return new Border { Name = "HomeBaseGamePoolsSection", Background = StudioTheme.Surface, BorderBrush = StudioTheme.Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(16), Child = new StackPanel { Spacing = 10, Children = {
            new TextBlock { Text = "Base Game Pools", FontSize = 28, FontWeight = FontWeight.Bold },
            new TextBlock { Text = "Edit and share bench/draft, free-agent and superstar pools for None (base game). Downloads back up your current pools before replacing them.", TextWrapping = TextWrapping.Wrap, Foreground = StudioTheme.Muted }, actions
        } } };
    }

    private async Task ShowBaseGamePoolUpload()
    {
        var dialog = new Window { Title = "Upload Base Game Pools", Name = "UploadBaseGamePoolsWindow", Width = 620, Height = 700, MinWidth = 480, MinHeight = 460, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Background };
        var title = new TextBox { Name = "UploadPoolName", Text = "My Base Game Pools", MaxLength = 160 };
        var author = new TextBox { Name = "UploadPoolAuthor", Watermark = "Your name or handle", MaxLength = 60 };
        var description = new TextBox { Name = "UploadPoolDescription", Watermark = "Describe your pool set (optional)", MaxLength = 2000, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 96 };
        using var icon = new CommunityIconPicker(async () =>
        {
            var picked = await dialog.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Choose a pool set cover", AllowMultiple = false, FileTypeFilter = [new FilePickerFileType("PNG image") { Patterns = ["*.png"] }] });
            return picked.FirstOrDefault()?.TryGetLocalPath();
        });
        var feedback = new TextBlock { Name = "UploadPoolFeedback", Text = "Uploads are public. Only saved, selected players and their referenced assets are shared. Studio saves your private management code automatically.", TextWrapping = TextWrapping.Wrap, Foreground = StudioTheme.Muted };
        bool busy = false;
        var close = Button("Cancel", () => dialog.Close());
        Button upload = null!;
        void Controls()
        {
            upload.IsEnabled = close.IsEnabled = !busy && !icon.IsBusy;
            title.IsEnabled = author.IsEnabled = description.IsEnabled = icon.IsEnabled = !busy && upload.IsVisible;
        }
        upload = Button("Upload pools", async () =>
        {
            if (busy || icon.IsBusy) return;
            string name = title.Text?.Trim() ?? "", by = string.IsNullOrWhiteSpace(author.Text) ? "Anonymous" : author.Text.Trim();
            string zip = Path.Combine(Path.GetTempPath(), "t2t-pools-" + Guid.NewGuid().ToString("N") + ".zip");
            busy = true; Controls(); feedback.Foreground = StudioTheme.Muted;
            try
            {
                var metadata = new CommunityPresentation(name, by, description.Text?.Trim() ?? "", icon.Icon, name + " - " + by).ValidateForUpload(name + " - " + by);
                feedback.Text = "Packaging your saved pools…";
                await Task.Run(() => BaseGamePoolPackages.Export(root, zip, name));
                var progress = new Progress<double>(p => feedback.Text = $"Uploading {name}: {p:0}%…");
                var posted = await CampaignSharing.PoolsCommunity.UploadAsync(zip, name + " - " + by, progress, metadata: metadata);
                feedback.Text = "Uploaded. Your set is available under Base Game Pools → Download." + (posted.MetadataWarning.Length > 0 ? " " + posted.MetadataWarning : "");
                feedback.Foreground = posted.MetadataWarning.Length > 0 ? SharingWarn : Brush.Parse("#70DEA2");
                upload.IsVisible = false; close.Content = "Close";
            }
            catch (Exception ex) { feedback.Text = "Upload could not be confirmed: " + ex.Message + " Check Base Game Pools → Manage uploads before retrying."; feedback.Foreground = SharingWarn; }
            finally { busy = false; Controls(); if (File.Exists(zip)) File.Delete(zip); }
        });
        upload.Name = "UploadBaseGamePoolsConfirm"; StudioTheme.Primary(upload); icon.BusyChanged += Controls;
        var form = new StackPanel { Spacing = StudioTheme.SpaceMd, Children = {
            new TextBlock { Text = "Upload Base Game Pools", FontSize = 26, FontWeight = FontWeight.Bold },
            new TextBlock { Text = "Share all three pools as one set, including referenced custom talents, heads and logos. To update an existing listing, use Manage uploads.", TextWrapping = TextWrapping.Wrap, Foreground = StudioTheme.Muted },
            new TextBlock { Text = "Pool set name" }, title, new TextBlock { Text = "Your name" }, author,
            new TextBlock { Text = "Description" }, description, icon
        } };
        dialog.Content = new Grid { Margin = new Thickness(24), RowDefinitions = new("*,Auto,Auto"), RowSpacing = 12, Children = {
            new ScrollViewer { Content = form }, WithRow(feedback, 1), WithRow(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Right, Children = { close, upload } }, 2)
        } };
        dialog.Closing += (_, e) => { if (busy || icon.IsBusy) e.Cancel = true; };
        await dialog.ShowDialog(this);
    }

    private async Task ShowBaseGamePoolDownload()
    {
        var dialog = new Window { Name = "DownloadBaseGamePoolsWindow", Title = "Download Base Game Pools", Width = 1000, Height = 680, MinWidth = 760, MinHeight = 500, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Background };
        using var details = new CommunityCampaignDetails("pool set");
        var list = new ListBox { Name = "CommunityBaseGamePools", Background = StudioTheme.Input, MinHeight = 180,
            ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<CampaignSharing.CommunityItem>((item, _) => new StackPanel { Spacing = 4, Children = {
                new TextBlock { Text = item?.Name ?? "", TextTrimming = TextTrimming.CharacterEllipsis },
                new TextBlock { Text = item == null ? "" : FormatSize(item.Size) + " · " + item.Modified.Split('T')[0], FontSize = 12, Foreground = StudioTheme.Muted }
            } }) };
        var feedback = new TextBlock { Name = "DownloadPoolFeedback", Foreground = StudioTheme.Muted, TextWrapping = TextWrapping.Wrap };
        bool busy = false;
        Button refresh = null!, fromFile = null!, download = null!, close = null!;
        void Controls() { refresh.IsEnabled = fromFile.IsEnabled = close.IsEnabled = list.IsEnabled = !busy; download.IsEnabled = !busy && list.SelectedItem != null; }
        async Task Run(Func<Task> operation)
        {
            if (busy) return; busy = true; Controls(); feedback.Foreground = StudioTheme.Muted;
            try { await operation(); }
            catch (Exception ex) { feedback.Text = ex.Message; feedback.Foreground = SharingWarn; }
            finally { busy = false; Controls(); }
        }
        async Task Install(string zip)
        {
            feedback.Text = "Checking the pool package…";
            var info = await Task.Run(() => BaseGamePoolPackages.Inspect(zip));
            if (!await BaseGamePoolRestoreWindow.Confirm(dialog, info with { Enabled = true }, restore: false)) { feedback.Text = "Cancelled. Your current pools were kept."; return; }
            feedback.Text = "Backing up and replacing your pools…";
            await Task.Run(() => BaseGamePoolPackages.Install(zip, root));
            ShowHome();
            feedback.Text = "Pools installed. Select None (base game) and restart the game to use them. Your previous setup is available under Restore backup.";
            feedback.Foreground = Brush.Parse("#70DEA2");
        }
        async Task Refresh()
        {
            feedback.Text = "Loading community pool sets…";
            var items = await CampaignSharing.ListAsync("base-game-pools");
            list.ItemsSource = items; details.Show(null, null);
            feedback.Text = items.Count == 0 ? "No shared pool sets yet. Upload yours from the Base Game Pools section." : $"{items.Count} shared pool {(items.Count == 1 ? "set" : "sets")}.";
        }
        refresh = Button("Refresh", async () => await Run(Refresh));
        fromFile = Button("Import pool file…", async () => await Run(async () =>
        {
            var picked = await dialog.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Choose a Base Game Pools ZIP", AllowMultiple = false, FileTypeFilter = [new FilePickerFileType("Base Game Pools package") { Patterns = ["*.zip", "*.t2tpools"] }] });
            string? file = picked.FirstOrDefault()?.TryGetLocalPath(); if (file == null) return;
            string zip = Path.Combine(Path.GetTempPath(), "t2t-pool-import-" + Guid.NewGuid().ToString("N") + ".zip");
            try { await Task.Run(() => File.Copy(file, zip)); await Install(zip); }
            finally { if (File.Exists(zip)) File.Delete(zip); }
        }));
        fromFile.Name = "ImportBaseGamePoolsFile";
        download = Button("Download and install", async () => await Run(async () =>
        {
            if (list.SelectedItem is not CampaignSharing.CommunityItem item) return;
            string zip = Path.Combine(Path.GetTempPath(), "t2t-pool-download-" + Guid.NewGuid().ToString("N") + ".zip");
            try { feedback.Text = "Downloading " + item.Name + "…"; await CampaignSharing.DownloadAsync(item.Path, zip); await Install(zip); }
            finally { if (File.Exists(zip)) File.Delete(zip); }
        }));
        download.Name = "DownloadCommunityBaseGamePools"; StudioTheme.Primary(download);
        close = Button("Close", () => dialog.Close());
        list.SelectionChanged += (_, _) => { var item = list.SelectedItem as CampaignSharing.CommunityItem; details.Show(item?.Name, item?.Presentation); Controls(); };
        dialog.Content = new Grid { Margin = new Thickness(24), RowDefinitions = new("Auto,Auto,*,Auto,Auto,Auto"), RowSpacing = StudioTheme.SpaceMd, Children = {
            new TextBlock { Text = "Download Base Game Pools", FontSize = 26, FontWeight = FontWeight.Bold },
            WithRow(new TextBlock { Text = "Pool sets replace all three pools after confirmation. Your current setup is backed up automatically.", TextWrapping = TextWrapping.Wrap, Foreground = StudioTheme.Muted }, 1),
            WithRow(new Grid { ColumnDefinitions = new("3*,2*"), ColumnSpacing = 16, Children = { list, WithColumn(details, 1) } }, 2),
            WithRow(new WrapPanel { Orientation = Orientation.Horizontal, Children = { refresh, fromFile, download } }, 3),
            WithRow(feedback, 4), WithRow(close, 5)
        } };
        foreach (var button in new[] { refresh, fromFile, download }) button.Margin = new Thickness(0, 0, 10, 6);
        close.HorizontalAlignment = HorizontalAlignment.Right;
        dialog.Closing += (_, e) => { if (busy) e.Cancel = true; };
        dialog.Opened += async (_, _) => await Run(Refresh);
        Controls(); await dialog.ShowDialog(this);
    }
}
