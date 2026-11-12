using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using CampaignCore;

namespace CampaignCreator;

public sealed partial class MainWindow
{
    private Task ShowCommunityManagement(bool pools = false)
        => BuildCommunityManagementWindow(pools ? CampaignSharing.PoolsCommunity : CampaignSharing.Community).ShowDialog(this);

    private Window BuildCommunityManagementWindow(CommunityCampaignClient client)
    {
        bool pools = client.Kind == "base-game-pools";
        string kind = pools ? "pool set" : "campaign", title = pools ? "Manage Base Game Pools uploads" : "Manage campaign uploads";
        var dialog = new Window { Title = title, Width = 760, Height = 570, MinWidth = 620, MinHeight = 480,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Background };
        var list = new ListBox { Name = "OwnedCommunityCampaigns", Background = StudioTheme.Input, Height = 220,
            ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<ManagedCampaign>((item, _) => item == null ? new TextBlock() : new StackPanel { Spacing = 4, Children = {
                new TextBlock { Text = item.Post?.Name ?? item.Ownership.Name, TextTrimming = TextTrimming.CharacterEllipsis },
                new TextBlock { Text = item.AccessError.Length > 0 ? item.AccessError : item.Post == null ? "Not found in Dropbox — a reserved or removed upload" : FormatSize(item.Post.Size) + " · " + item.Post.Modified,
                    FontSize = 12, Foreground = StudioTheme.Muted, TextTrimming = TextTrimming.CharacterEllipsis }
            } }) };
        var feedback = new TextBlock { Foreground = StudioTheme.Muted, TextWrapping = TextWrapping.Wrap };
        bool busy = false;
        Button replace = null!, updateSaved = null!, delete = null!, refresh = null!, restore = null!, close = null!;
        void Controls()
        {
            bool selected = list.SelectedItem is ManagedCampaign { Post: not null };
            replace.IsEnabled = !busy && selected; delete.IsEnabled = !busy && selected;
            updateSaved.IsEnabled = !busy && selected && pools && File.Exists(Path.Combine(BaseGamePlayerPools.Folder(root), "settings.txt"));
            refresh.IsEnabled = restore.IsEnabled = close.IsEnabled = list.IsEnabled = !busy;
        }
        async Task Refresh(bool showMessage = true)
        {
            string? key = (list.SelectedItem as ManagedCampaign)?.Ownership.Key;
            var owned = await client.OwnedAsync();
            list.ItemsSource = owned;
            list.SelectedItem = owned.FirstOrDefault(x => x.Ownership.Key == key) ?? owned.FirstOrDefault();
            if (showMessage) feedback.Text = owned.Count == 0
                ? "No saved uploads yet. Upload a " + kind + " to save access automatically, or add access for an older post."
                : pools ? "Select an upload to update from your saved pools, replace its ZIP or delete its public copy."
                : "Select one of your uploads to update its ZIP or delete its Dropbox copy.";
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
        replace = Button(pools ? "Replace ZIP…" : "Update ZIP…", async () =>
        {
            if (busy || list.SelectedItem is not ManagedCampaign { Post: not null } selected) return;
            await Run(async () =>
            {
                async Task ReplaceZip(string file)
                {
                    if (pools) await Task.Run(() => BaseGamePoolPackages.Inspect(file));
                    else await Task.Run(() => CampaignSharing.TopFolder(file));
                    var progress = new Progress<double>(value => { if (busy) feedback.Text = $"Replacing {selected.Post.Name}: {value:0}%…"; });
                    var posted = await client.UploadAsync(file, selected.Ownership.Name, progress, selected);
                    await Refresh(false);
                    feedback.Text = "Updated the posted " + kind + ". The new ZIP is now available from its community list." +
                        (posted.MetadataWarning.Length > 0 ? " " + posted.MetadataWarning : "");
                    feedback.Foreground = posted.MetadataWarning.Length > 0 ? SharingWarn : Brush.Parse("#70DEA2");
                }
                if (!pools)
                {
                    var source = await ChooseCampaignUpdateSource(dialog, selected.Post.Name);
                    if (source.Saved is { } saved)
                    {
                        string zip = Path.Combine(Path.GetTempPath(), "t2t-campaign-update-" + Guid.NewGuid().ToString("N") + ".zip");
                        try
                        {
                            feedback.Text = "Packaging " + saved.Label + "…";
                            await Task.Run(() => CampaignSharing.Zip(saved.Path, zip, root));
                            await ReplaceZip(zip);
                        }
                        finally { File.Delete(zip); }
                        return;
                    }
                    if (!source.ChooseFile) return;
                }
                var files = await dialog.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Choose the replacement " + kind + " ZIP", AllowMultiple = false,
                    FileTypeFilter = [new FilePickerFileType(pools ? "Base Game Pools ZIP" : "Campaign ZIP") { Patterns = pools ? ["*.zip", "*.t2tpools"] : ["*.zip", "*.t2tcampaign"] }] });
                if (files.FirstOrDefault()?.TryGetLocalPath() is { } file) await ReplaceZip(file);
            });
        });
        replace.Name = "ReplaceCommunityCampaign"; StudioTheme.Primary(replace);
        updateSaved = Button("Update from saved pools…", async () =>
        {
            if (busy || !pools || list.SelectedItem is not ManagedCampaign { Post: not null } selected) return;
            await Run(async () =>
            {
                string zip = Path.Combine(Path.GetTempPath(), "t2t-pool-update-" + Guid.NewGuid().ToString("N") + ".zip");
                try
                {
                    feedback.Text = "Packaging your saved pools and their custom talents, heads and logos…";
                    string name = selected.Post.Presentation?.CampaignName ?? "Base Game Pools";
                    if (name.Length > 160) name = name[..160];
                    var info = await Task.Run(() =>
                    {
                        BaseGamePoolPackages.Export(root, zip, name);
                        return BaseGamePoolPackages.Inspect(zip);
                    });
                    if (!await ConfirmSavedBaseGamePoolUpdate(dialog, selected.Post.Name, info))
                    { feedback.Text = "Cancelled. Your posted pool set was kept."; return; }
                    var progress = new Progress<double>(value => feedback.Text = $"Updating {selected.Post.Name}: {value:0}%…");
                    // No metadata argument: replacement retains the listing's current details and cover.
                    var posted = await client.UploadAsync(zip, selected.Ownership.Name, progress, selected);
                    await Refresh(false);
                    feedback.Text = "Updated the posted pool set from your saved pools. Its title, description and cover were kept." +
                        (posted.MetadataWarning.Length > 0 ? " " + posted.MetadataWarning : "");
                    feedback.Foreground = posted.MetadataWarning.Length > 0 ? SharingWarn : Brush.Parse("#70DEA2");
                }
                finally { if (File.Exists(zip)) File.Delete(zip); }
            });
        });
        updateSaved.Name = "UpdateCommunityPoolsFromSaved"; StudioTheme.Primary(updateSaved);
        ToolTip.SetTip(updateSaved, "Save your pool edits first. Updates the selected listing with all three saved pools and their referenced custom talents, heads and logos.");
        delete = Button("Delete from Dropbox…", async () =>
        {
            if (busy || list.SelectedItem is not ManagedCampaign { Post: not null } selected) return;
            await Run(async () =>
            {
                if (!await ConfirmCommunityDeletion(dialog, selected.Post.Name, pools)) return;
                feedback.Text = "Deleting the posted " + kind + "…";
                await client.DeleteAsync(selected);
                await Refresh(false);
                feedback.Text = "Deleted the posted " + kind + " from Dropbox. Your local files are kept.";
                feedback.Foreground = Brush.Parse("#70DEA2");
            });
        });
        delete.Name = "DeleteCommunityCampaign";
        restore = Button("Add access for an older post…", async () => await Run(async () =>
        {
            string? code = await AskCommunityCode(dialog, pools);
            if (code == null) return;
            feedback.Text = "Verifying the management code…";
            await client.ImportCodeAsync(code);
            await Refresh(false);
            feedback.Text = "Access verified and saved in AppData. You won't need to enter this code again.";
        }));
        restore.Name = "RestoreCommunityAccess";
        close = Button("Close", () => dialog.Close()); close.HorizontalAlignment = HorizontalAlignment.Right;
        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var button in pools ? new[] { refresh, updateSaved, replace, delete } : new[] { refresh, replace, delete })
        { button.Margin = new Thickness(0, 0, 8, 8); actions.Children.Add(button); }
        var layout = new Grid { Margin = new Thickness(24), RowDefinitions = new("Auto,*,Auto"), RowSpacing = StudioTheme.SpaceMd };
        layout.Children.Add(new TextBlock { Text = title, FontSize = 26, FontWeight = FontWeight.Bold });
        layout.Children.Add(WithRow(new ScrollViewer { Content = new StackPanel { Spacing = StudioTheme.SpaceMd, Children = {
            new TextBlock { Text = "Studio checks each " + kind + " against your saved AppData code. Replacement and deletion affect the posted Dropbox copy; your local files are kept.",
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
        return dialog;
    }

    private async Task<(bool ChooseFile, ContentChoice? Saved)> ChooseCampaignUpdateSource(Window owner, string listing)
    {
        var choices = CampaignChoices();
        var dialog = new Window { Title = "Update campaign ZIP", Width = 560, Height = 380, MinWidth = 440, MinHeight = 320,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Background };
        var picker = new ComboBox { Name = "CampaignUpdatePicker", ItemsSource = choices, IsEnabled = choices.Count > 0,
            PlaceholderText = choices.Count == 0 ? "No saved campaigns" : "Choose a campaign", HorizontalAlignment = HorizontalAlignment.Stretch };
        var file = Button("Choose ZIP file…", () => dialog.Close((true, (ContentChoice?)null)));
        file.Name = "UpdateFromCampaignZip"; file.HorizontalAlignment = HorizontalAlignment.Left;
        var saved = Button("Zip and replace", () => { if (picker.SelectedItem is ContentChoice campaign) dialog.Close((false, campaign)); });
        saved.Name = "UpdateFromSavedCampaign"; saved.IsEnabled = false; StudioTheme.Primary(saved);
        picker.SelectionChanged += (_, _) => saved.IsEnabled = picker.SelectedItem is ContentChoice;
        var cancel = Button("Cancel", () => dialog.Close()); cancel.Name = "CancelCampaignUpdate";
        dialog.Content = new Grid { Margin = new Thickness(24), RowDefinitions = new("*,Auto"), RowSpacing = StudioTheme.SpaceMd, Children = {
            new ScrollViewer { Content = new StackPanel { Spacing = StudioTheme.SpaceMd, Children = {
                new TextBlock { Text = "Update ZIP for " + listing, FontSize = 22, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = "Choose a ZIP to replace the public download, or let Studio package a saved campaign with its custom assets.", Foreground = StudioTheme.Muted, TextWrapping = TextWrapping.Wrap },
                file,
                new TextBlock { Text = "Saved campaign" }, picker
            } } },
            WithRow(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, saved } }, 1)
        } };
        return await dialog.ShowDialog<(bool, ContentChoice?)>(owner);
    }

    private async Task<bool> ConfirmSavedBaseGamePoolUpdate(Window owner, string name, BaseGamePoolInfo pools)
    {
        bool confirmed = false;
        var dialog = new Window { Title = "Update posted Base Game Pools", Name = "ConfirmSavedBaseGamePoolUpdateWindow", Width = 560, Height = 350, MinWidth = 440, MinHeight = 300,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Background };
        var cancel = Button("Cancel", () => dialog.Close()); cancel.Name = "CancelSavedBaseGamePoolUpdate";
        var update = Button("Update pool set", () => { confirmed = true; dialog.Close(); }); update.Name = "ConfirmSavedBaseGamePoolUpdate"; StudioTheme.Primary(update);
        dialog.Content = new Grid { Margin = new Thickness(24), RowDefinitions = new("*,Auto"), RowSpacing = StudioTheme.SpaceMd, Children = {
            new ScrollViewer { Content = new StackPanel { Spacing = StudioTheme.SpaceMd, Children = {
                new TextBlock { Text = "Update " + name + "?", FontSize = 22, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = "Replace this listing's public download with your saved pools and their custom talents, heads and logos. Its title, description and cover will stay the same. Your local pools stay unchanged.", TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = $"Bench/draft: {pools.BenchCount}\nFree agents: {pools.FreeAgentCount}\nSuperstars: {pools.SuperstarCount}", TextWrapping = TextWrapping.Wrap }
            } } },
            WithRow(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, update } }, 1)
        } };
        await dialog.ShowDialog(owner); return confirmed;
    }

    private async Task<bool> ConfirmCommunityDeletion(Window owner, string name, bool pools = false)
    {
        bool confirmed = false;
        var dialog = new Window { Title = pools ? "Delete posted pool set" : "Delete posted campaign", Width = 520, Height = 270, MinHeight = 250,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Background };
        var cancel = Button("Cancel", () => dialog.Close());
        var remove = Button("Delete from Dropbox", () => { confirmed = true; dialog.Close(); });
        dialog.Content = new Grid { Margin = new Thickness(24), RowDefinitions = new("*,Auto"), RowSpacing = StudioTheme.SpaceMd, Children = {
            new ScrollViewer { Content = new StackPanel { Spacing = StudioTheme.SpaceMd, Children = {
                new TextBlock { Text = "Delete " + name + "?", FontSize = 20, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = "This removes its public community download from Dropbox. Your local files and backups stay on your computer.", TextWrapping = TextWrapping.Wrap }
            } } },
            WithRow(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, remove } }, 1)
        } };
        await dialog.ShowDialog(owner); return confirmed;
    }

    private async Task<string?> AskCommunityCode(Window owner, bool pools = false)
    {
        string? answer = null;
        var dialog = new Window { Title = pools ? "Add pool set access" : "Add campaign access", Width = 560, Height = 270,
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
