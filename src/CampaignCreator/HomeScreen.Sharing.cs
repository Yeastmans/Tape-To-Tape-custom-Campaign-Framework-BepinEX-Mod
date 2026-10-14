using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace CampaignCreator;

/// <summary>Home-screen campaign import (file or community folder) and community upload.</summary>
public sealed partial class MainWindow
{
    private static readonly IBrush SharingWarn = Brush.Parse("#FFD18C");

    private static string FormatSize(long bytes) => bytes >= 1024 * 1024 ? $"{bytes / (1024d * 1024):0.0} MB" : bytes >= 1024 ? $"{bytes / 1024} KB" : $"{bytes} B";

    private async Task ShowCampaignImport()
    {
        var dialog = new Window { Title = "Import campaign", Width = 640, Height = 600, MinWidth = 520, MinHeight = 460, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Background };
        var feedback = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = StudioTheme.Muted };
        var list = new ListBox { Name = "CommunityCampaigns", Background = StudioTheme.Input, MinHeight = 200,
            ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<CampaignSharing.CommunityItem>((item, _) => item == null ? new TextBlock() : new Grid {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = StudioTheme.SpaceMd, Children = {
                    new TextBlock { Text = item.Name, TextTrimming = TextTrimming.CharacterEllipsis },
                    WithColumn(new TextBlock { Text = FormatSize(item.Size) + (item.Modified.Length >= 10 ? " · " + item.Modified[..10] : ""), Foreground = StudioTheme.Muted, FontSize = 12, VerticalAlignment = VerticalAlignment.Center }, 1)
                } }) };
        bool busy = false;
        async Task Install(string zip, string? cleanup = null)
        {
            try
            {
                string folder = CampaignSharing.TopFolder(zip);
                if (Directory.Exists(Path.Combine(root, "campaigns", folder)))
                {
                    string? choice = await AskExisting(dialog, folder);
                    if (choice == null) { feedback.Text = "Import cancelled."; return; }
                    if (choice == "replace") CampaignSharing.SetAside(root, folder);
                    else folder = CampaignSharing.FreeName(root, folder);
                }
                var result = CampaignSharing.Install(zip, root, folder);
                string logos = result.LogosCopied + result.LogosKept == 0 ? "" : $" Logos: {result.LogosCopied} added" + (result.LogosKept > 0 ? $", {result.LogosKept} already in your CustomLogos folder (kept yours)." : ".");
                ShowHome(); // refreshes Select campaign behind the dialog
                string heads = result.HeadsCopied + result.HeadsKept == 0 ? "" : $" Custom heads: {result.HeadsCopied} added, {result.HeadsKept} already installed. Restart the game to load new heads.";
                feedback.Text = $"Imported {result.Folder}. Choose it under Select campaign to play it.{logos}{heads}";
                feedback.Foreground = Brush.Parse("#70DEA2");
            }
            finally { if (cleanup != null && File.Exists(cleanup)) File.Delete(cleanup); }
        }
        var fromFile = Button("Import from file…", async () =>
        {
            if (busy) return;
            var picked = await dialog.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions {
                Title = "Choose a campaign zip", AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("Campaign zip") { Patterns = ["*.zip", "*.t2tcampaign"] }]
            });
            string? path = picked.FirstOrDefault()?.TryGetLocalPath();
            if (path == null) return;
            try { await Install(path); } catch (Exception ex) { feedback.Text = ex.Message; feedback.Foreground = SharingWarn; }
        });
        fromFile.Name = "ImportCampaignFile"; StudioTheme.Primary(fromFile);
        async Task Refresh()
        {
            feedback.Text = "Loading community campaigns…"; feedback.Foreground = StudioTheme.Muted;
            try
            {
                var items = await CampaignSharing.ListAsync();
                list.ItemsSource = items.OrderByDescending(i => i.Modified).ToList();
                feedback.Text = items.Count == 0 ? "No community campaigns yet. Upload yours to be the first." : $"{items.Count} community {(items.Count == 1 ? "campaign" : "campaigns")}.";
            }
            catch (Exception ex) { feedback.Text = "Couldn't load community campaigns: " + ex.Message; feedback.Foreground = SharingWarn; }
        }
        var download = Button("Download and install", async () =>
        {
            if (busy) return;
            if (list.SelectedItem is not CampaignSharing.CommunityItem item) { feedback.Text = "Choose a campaign from the list first."; feedback.Foreground = SharingWarn; return; }
            busy = true; feedback.Text = "Downloading " + item.Name + "…"; feedback.Foreground = StudioTheme.Muted;
            string zip = Path.Combine(Path.GetTempPath(), "t2t_" + Guid.NewGuid().ToString("N") + ".zip");
            try { await CampaignSharing.DownloadAsync(item.Path, zip); await Install(zip, zip); }
            catch (Exception ex) { feedback.Text = "Download failed: " + ex.Message; feedback.Foreground = SharingWarn; }
            finally { busy = false; if (File.Exists(zip)) File.Delete(zip); }
        });
        download.Name = "DownloadCommunityCampaign";
        var refresh = Button("Refresh", async () => await Refresh());
        refresh.HorizontalAlignment = HorizontalAlignment.Left; download.HorizontalAlignment = HorizontalAlignment.Right;
        var communityActions = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Children = { refresh, WithColumn(download, 1) } };
        var layout = new Grid { Margin = new Thickness(24), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,*,Auto,Auto,Auto"), RowSpacing = StudioTheme.SpaceMd };
        layout.Children.Add(new TextBlock { Text = "Import campaign", FontSize = 26, FontWeight = FontWeight.Bold });
        layout.Children.Add(WithRow(new TextBlock { Text = "Install a campaign zip from your computer, or download one shared by the community.", Foreground = StudioTheme.Muted, TextWrapping = TextWrapping.Wrap }, 1));
        layout.Children.Add(WithRow(fromFile, 2));
        layout.Children.Add(WithRow(new TextBlock { Text = "Community campaigns", FontSize = 20, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, StudioTheme.SpaceSm, 0, 0) }, 3));
        layout.Children.Add(WithRow(list, 4));
        layout.Children.Add(WithRow(communityActions, 5));
        layout.Children.Add(WithRow(feedback, 6));
        var close = Button("Close", () => dialog.Close()); close.HorizontalAlignment = HorizontalAlignment.Right;
        layout.Children.Add(WithRow(close, 7));
        dialog.Content = layout;
        dialog.Opened += async (_, _) => await Refresh();
        await dialog.ShowDialog(this);
    }

    private async Task<string?> AskExisting(Window owner, string folder)
    {
        var dialog = new Window { Title = "Campaign already exists", Width = 480, Height = 230, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Background, CanResize = false };
        string? answer = null;
        var keep = Button("Keep both", () => { answer = "rename"; dialog.Close(); }); StudioTheme.Primary(keep);
        var replace = Button("Replace mine", () => { answer = "replace"; dialog.Close(); });
        var cancel = Button("Cancel", () => dialog.Close());
        dialog.Content = new StackPanel { Margin = new Thickness(24), Spacing = StudioTheme.SpaceMd, Children = {
            new TextBlock { Text = $"You already have a campaign named {folder}.", FontSize = 18, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
            new TextBlock { Text = "Keep both installs this one with a new name. Replace mine moves your copy to campaign_backups first.", Foreground = StudioTheme.Muted, TextWrapping = TextWrapping.Wrap },
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = StudioTheme.SpaceMd, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, replace, keep } }
        } };
        await dialog.ShowDialog(owner);
        return answer;
    }

    private async Task ShowCampaignUpload()
    {
        var campaignsList = CampaignChoices();
        var dialog = new Window { Title = "Upload campaign", Width = 560, Height = 500, MinWidth = 480, MinHeight = 440, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Background };
        var picker = new ComboBox { Name = "UploadCampaignPicker", ItemsSource = campaignsList, HorizontalAlignment = HorizontalAlignment.Stretch, PlaceholderText = "Choose a campaign" };
        if (campaigns.SelectedItem is string current) picker.SelectedItem = campaignsList.FirstOrDefault(c => c.Label == current);
        picker.SelectedItem ??= campaignsList.FirstOrDefault();
        var author = new TextBox { Name = "UploadAuthor", Watermark = "Your name or handle", MaxLength = 60 };
        var description = new TextBox { Name = "UploadDescription", Watermark = "One-line description (optional)", MaxLength = 200 };
        var feedback = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = StudioTheme.Muted,
            Text = "Uploads go to the public community folder. Anyone using the mod can download it, and it can't be removed from Studio." };
        bool busy = false;
        var cancel = Button("Cancel", () => dialog.Close());
        Button upload = null!;
        upload = Button("Upload", async () =>
        {
            if (busy) return;
            if (picker.SelectedItem is not ContentChoice campaign) { feedback.Text = "Choose a campaign to upload."; feedback.Foreground = SharingWarn; return; }
            string name = string.IsNullOrWhiteSpace(author.Text) ? "Anonymous" : author.Text.Trim();
            busy = true; feedback.Foreground = StudioTheme.Muted; feedback.Text = "Zipping " + campaign.Label + "…";
            string zip = Path.Combine(Path.GetTempPath(), "t2t_" + campaign.Label.Replace(' ', '_') + ".zip");
            try
            {
                var assets = await Task.Run(() => CampaignSharing.Zip(campaign.Path, zip, root));
                feedback.Text = $"Uploading {FormatSize(new FileInfo(zip).Length)} with {assets.Logos} logos and {assets.Heads} custom heads…";
                var progress = new Progress<double>(percent => feedback.Text = $"Uploading {campaign.Label}: {percent:0}%…");
                string path = await CampaignSharing.UploadAsync(zip, campaign.Label + " - " + name, name, description.Text?.Trim() ?? "", campaign.Label, progress);
                feedback.Text = $"Uploaded {campaign.Label}. It's now in the community list" + (path.Length > 0 ? " as " + path + "." : ".");
                feedback.Foreground = Brush.Parse("#70DEA2"); upload.IsVisible = false; cancel.Content = "Close";
            }
            catch (Exception ex) { feedback.Text = "Upload failed: " + ex.Message; feedback.Foreground = SharingWarn; }
            finally { busy = false; if (File.Exists(zip)) File.Delete(zip); }
        });
        upload.Name = "UploadCampaignConfirm"; StudioTheme.Primary(upload);
        dialog.Content = new ScrollViewer { Content = new StackPanel { Margin = new Thickness(24), Spacing = StudioTheme.SpaceMd, Children = {
            new TextBlock { Text = "Upload campaign", FontSize = 26, FontWeight = FontWeight.Bold },
            new TextBlock { Text = "Share a campaign with the community. Its custom logos and referenced custom head projects are included.", Foreground = StudioTheme.Muted, TextWrapping = TextWrapping.Wrap },
            new TextBlock { Text = "Campaign" }, picker,
            new TextBlock { Text = "Your name" }, author,
            new TextBlock { Text = "Description" }, description,
            feedback,
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = StudioTheme.SpaceMd, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, upload } }
        } } };
        await dialog.ShowDialog(this);
    }

    private static T WithRow<T>(T control, int row) where T : Control { Grid.SetRow(control, row); return control; }
    private static T WithColumn<T>(T control, int column) where T : Control { Grid.SetColumn(control, column); return control; }
}
