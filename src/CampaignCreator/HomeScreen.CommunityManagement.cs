using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace CampaignCreator;

public sealed partial class MainWindow
{
    private async Task ShowCommunityManagement()
    {
        var dialog = new Window { Title = "Manage campaign uploads", Width = 760, Height = 570, MinWidth = 620, MinHeight = 480,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Background };
        var list = new ListBox { Name = "OwnedCommunityCampaigns", Background = StudioTheme.Input, Height = 220,
            ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<ManagedCampaign>((item, _) => item == null ? new TextBlock() : new StackPanel { Spacing = 4, Children = {
                new TextBlock { Text = item.Post?.Name ?? item.Ownership.Name, TextTrimming = TextTrimming.CharacterEllipsis },
                new TextBlock { Text = item.AccessError.Length > 0 ? item.AccessError : item.Post == null ? "Not found in Dropbox — a reserved or removed upload" : FormatSize(item.Post.Size) + " · " + item.Post.Modified,
                    FontSize = 12, Foreground = StudioTheme.Muted, TextTrimming = TextTrimming.CharacterEllipsis }
            } }) };
        var feedback = new TextBlock { Foreground = StudioTheme.Muted, TextWrapping = TextWrapping.Wrap };
        bool busy = false;
        Button replace = null!, delete = null!, refresh = null!, restore = null!, close = null!;
        void Controls()
        {
            bool selected = list.SelectedItem is ManagedCampaign { Post: not null };
            replace.IsEnabled = !busy && selected; delete.IsEnabled = !busy && selected;
            refresh.IsEnabled = restore.IsEnabled = close.IsEnabled = list.IsEnabled = !busy;
        }
        async Task Refresh(bool showMessage = true)
        {
            string? key = (list.SelectedItem as ManagedCampaign)?.Ownership.Key;
            var owned = await CampaignSharing.Community.OwnedAsync();
            list.ItemsSource = owned;
            list.SelectedItem = owned.FirstOrDefault(x => x.Ownership.Key == key) ?? owned.FirstOrDefault();
            if (showMessage) feedback.Text = owned.Count == 0
                ? "No saved uploads yet. Upload a campaign to save access automatically, or add access for an older post."
                : "Select one of your uploads to replace its ZIP or delete its Dropbox copy.";
        }
        async Task Run(Func<Task> action)
        {
            if (busy) return;
            busy = true; Controls(); feedback.Foreground = StudioTheme.Muted;
            try { await action(); }
            catch (Exception ex) { feedback.Text = ex.Message + " Refresh the list before retrying."; feedback.Foreground = SharingWarn; }
            finally { busy = false; Controls(); }
        }
        refresh = Button("Refresh", async () => await Run(async () => { feedback.Text = "Checking your saved upload codes…"; await Refresh(); }));
        refresh.Name = "RefreshManagedCampaigns";
        replace = Button("Replace ZIP…", async () =>
        {
            if (busy || list.SelectedItem is not ManagedCampaign { Post: not null } selected) return;
            await Run(async () =>
            {
                var files = await dialog.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Choose the replacement campaign ZIP", AllowMultiple = false,
                    FileTypeFilter = [new FilePickerFileType("Campaign ZIP") { Patterns = ["*.zip", "*.t2tcampaign"] }] });
                string? file = files.FirstOrDefault()?.TryGetLocalPath();
                if (file == null) return;
                CampaignSharing.TopFolder(file);
                var progress = new Progress<double>(value => feedback.Text = $"Replacing {selected.Post.Name}: {value:0}%…");
                await CampaignSharing.Community.UploadAsync(file, selected.Ownership.Name, progress, selected);
                await Refresh(false);
                feedback.Text = "Updated the posted campaign. The new ZIP is now available from the community list.";
                feedback.Foreground = Brush.Parse("#70DEA2");
            });
        });
        replace.Name = "ReplaceCommunityCampaign"; StudioTheme.Primary(replace);
        delete = Button("Delete from Dropbox…", async () =>
        {
            if (busy || list.SelectedItem is not ManagedCampaign { Post: not null } selected) return;
            await Run(async () =>
            {
                if (!await ConfirmCommunityDeletion(dialog, selected.Post.Name)) return;
                feedback.Text = "Deleting the posted Dropbox campaign…";
                await CampaignSharing.Community.DeleteAsync(selected);
                await Refresh(false);
                feedback.Text = "Deleted the posted campaign from Dropbox. Your local campaign and ZIP files are kept.";
                feedback.Foreground = Brush.Parse("#70DEA2");
            });
        });
        delete.Name = "DeleteCommunityCampaign";
        restore = Button("Add access for an older post…", async () => await Run(async () =>
        {
            string? code = await AskCommunityCode(dialog);
            if (code == null) return;
            feedback.Text = "Verifying the management code…";
            await CampaignSharing.Community.ImportCodeAsync(code);
            await Refresh(false);
            feedback.Text = "Access verified and saved in AppData. You won't need to enter this code again.";
        }));
        restore.Name = "RestoreCommunityAccess";
        close = Button("Close", () => dialog.Close()); close.HorizontalAlignment = HorizontalAlignment.Right;
        var actions = new WrapPanel { Orientation = Orientation.Horizontal, Children = { refresh, replace, delete } };
        foreach (var button in new[] { refresh, replace, delete }) button.Margin = new Thickness(0, 0, 8, 8);
        var layout = new Grid { Margin = new Thickness(24), RowDefinitions = new("Auto,*,Auto"), RowSpacing = StudioTheme.SpaceMd };
        layout.Children.Add(new TextBlock { Text = "Manage campaign uploads", FontSize = 26, FontWeight = FontWeight.Bold });
        layout.Children.Add(WithRow(new ScrollViewer { Content = new StackPanel { Spacing = StudioTheme.SpaceMd, Children = {
            new TextBlock { Text = "Studio checks each campaign against your saved AppData code. Replacement and deletion affect the posted Dropbox copy; your local campaigns are kept.",
                Foreground = StudioTheme.Muted, TextWrapping = TextWrapping.Wrap }, list, feedback
        } } }, 1));
        layout.Children.Add(WithRow(new StackPanel { Spacing = 8, Children = { actions,
            new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 12, Children = { restore, WithColumn(close, 1) } }
        } }, 2));
        dialog.Content = layout;
        list.SelectionChanged += (_, _) => Controls();
        dialog.Closing += (_, e) => { if (busy) e.Cancel = true; };
        dialog.Opened += async (_, _) => await Run(async () => { feedback.Text = "Checking your saved upload codes…"; await Refresh(); });
        Controls();
        await dialog.ShowDialog(this);
    }

    private async Task<bool> ConfirmCommunityDeletion(Window owner, string name)
    {
        bool confirmed = false;
        var dialog = new Window { Title = "Delete posted campaign", Width = 520, Height = 270, MinHeight = 250,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Background };
        var cancel = Button("Cancel", () => dialog.Close());
        var remove = Button("Delete from Dropbox", () => { confirmed = true; dialog.Close(); });
        dialog.Content = new Grid { Margin = new Thickness(24), RowDefinitions = new("*,Auto"), RowSpacing = StudioTheme.SpaceMd, Children = {
            new ScrollViewer { Content = new StackPanel { Spacing = StudioTheme.SpaceMd, Children = {
                new TextBlock { Text = "Delete " + name + "?", FontSize = 20, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = "This removes its public community download from Dropbox. Your local campaign and ZIP files stay on your computer.", TextWrapping = TextWrapping.Wrap }
            } } },
            WithRow(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, remove } }, 1)
        } };
        await dialog.ShowDialog(owner); return confirmed;
    }

    private async Task<string?> AskCommunityCode(Window owner)
    {
        string? answer = null;
        var dialog = new Window { Title = "Add campaign access", Width = 560, Height = 270,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Background };
        var code = new TextBox { Name = "CommunityManagementCode", Watermark = "Management code", PasswordChar = '●' };
        var cancel = Button("Cancel", () => dialog.Close());
        var save = Button("Verify and save", () => { if (!string.IsNullOrWhiteSpace(code.Text)) { answer = code.Text.Trim(); dialog.Close(); } });
        dialog.Content = new StackPanel { Margin = new Thickness(24), Spacing = StudioTheme.SpaceMd, Children = {
            new TextBlock { Text = "Older posts need a code assigned by the community administrator. Paste it once; Studio verifies it and remembers it in AppData.", TextWrapping = TextWrapping.Wrap },
            code, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, save } }
        } };
        await dialog.ShowDialog(owner); return answer;
    }
}
