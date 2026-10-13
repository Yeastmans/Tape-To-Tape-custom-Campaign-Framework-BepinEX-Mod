using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using GameArt;

namespace CampaignCreator;

public sealed partial class MainWindow
{
    /// <summary>Set by App for the real application; windows built directly (tests) never touch the game files.</summary>
    internal static bool BuildGameArtOnStart;
    private bool readingGameArt;

    /// <summary>
    /// Makes sure the game artwork has been built from the player's install for the current game build,
    /// showing progress while it is built (first launch, and again after a game update).
    /// </summary>
    private async Task EnsureGameArt(bool force = false)
    {
        if (readingGameArt) return;
        readingGameArt = true;
        try { await EnsureModCompatibility(force); if(updateInstallerStarted || !IsVisible) return; await ReadGameArt(force); if(compatibilityStatus.Length>0) status.Text=compatibilityStatus; }
        finally { readingGameArt = false; }
    }

    private async Task ReadGameArt(bool force)
    {
        string? game = ArtCache.FindGame(Path.Combine(root, "game-folder.txt"));
        if (!force && game != null && ArtCache.Ready(game) is { } ready)
        {
            if (ready != StudioArt.Folder) { StudioArt.Use(ready); RefreshGameArt(); }
            return;
        }
        // A previous cache stays usable if the game has been moved or uninstalled.
        if (!force && game == null && StudioArt.Folder != null) return;
        if (!force && !await AskToReadGameArt()) return;
        if (game == null)
        {
            game = await AskForGame();
            if (game == null) return;
            ArtCache.Remember(game);
        }
        var line = new TextBlock { Text = "Opening the game files…", Foreground = StudioTheme.Muted, TextWrapping = TextWrapping.Wrap };
        var close = new Button { Content = "Close", IsVisible = false, HorizontalAlignment = HorizontalAlignment.Right, Padding = StudioTheme.ButtonPadding };
        var dialog = new Window
        {
            Title = "Reading your game files", Width = 540, SizeToContent = SizeToContent.Height, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = StudioTheme.Surface,
            Content = new StackPanel { Margin = new Thickness(24), Spacing = StudioTheme.SpaceMd, Children = {
                new TextBlock { Text = "Reading your game files", FontSize = 22, FontWeight = FontWeight.Bold },
                new TextBlock { Text = "Campaign Studio builds its heads, native head templates, logos, equipment, stadium and map pictures from your own copy of Tape to Tape. This happens on the first launch and again after a game update, and can take a few minutes.", TextWrapping = TextWrapping.Wrap },
                new ProgressBar { IsIndeterminate = true, Height = 6 },
                line, close
            } }
        };
        bool finished = false;
        dialog.Closing += (_, e) => { if (!finished) e.Cancel = true; };
        close.Click += (_, _) => dialog.Close();
        _ = dialog.ShowDialog(this);
        try
        {
            var progress = new Progress<string>(message => line.Text = message);
            string cache = await Task.Run(() => ArtCache.Build(game, progress, force: force));
            StudioArt.Use(cache);
            RefreshGameArt();
            var failures = ArtCache.Failures(cache);
            finished = true;
            if (failures.Count == 0) { status.Text = "Game artwork ready: " + cache; dialog.Close(); return; }
            line.Text = "Some pictures couldn't be read and will show as unavailable: " + string.Join("; ", failures.Select(f => f.Folder + " (" + f.Error + ")")) + " Use Read game artwork on Home to try again.";
        }
        catch (Exception ex)
        {
            finished = true;
            line.Text = "Couldn't read the game files: " + ex.Message + " Use Read game artwork on Home to try again.";
        }
        close.IsVisible = true;
    }

    private Task<bool> AskToReadGameArt()
    {
        var read = new Button { Name = "GameArtReadNow", Content = "Read game artwork", Padding = StudioTheme.ButtonPadding };
        var later = new Button { Name = "GameArtReadLater", Content = "Later", Padding = StudioTheme.ButtonPadding };
        var dialog = new Window
        {
            Name = "GameArtReadPrompt", Title = "Read game artwork", Width = 540, SizeToContent = SizeToContent.Height,
            CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = StudioTheme.Surface,
            Content = new StackPanel { Margin = new Thickness(24), Spacing = StudioTheme.SpaceMd, Children = {
                new TextBlock { Text = "Read artwork from your game", FontSize = 22, FontWeight = FontWeight.Bold },
                new TextBlock { Text = "Campaign Studio needs to read heads, native head templates, equipment, logos, stadiums, maps and base-game players from your installed copy of Tape to Tape. This can take a few minutes. Tape to Tape can stay closed.", TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = "If Studio cannot find the game, choose its installation folder. You can do this later using Read game artwork on Home; previews and base-game imports will be unavailable until then.", TextWrapping = TextWrapping.Wrap, Foreground = StudioTheme.Muted },
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = StudioTheme.SpaceSm, Children = { later, read } }
            } }
        };
        read.Click += (_, _) => dialog.Close(true);
        later.Click += (_, _) => dialog.Close(false);
        return dialog.ShowDialog<bool>(this);
    }

    /// <summary>Reloads everything that caches game art after the art cache changes.</summary>
    private void RefreshGameArt()
    {
        FlatEquipmentArtwork.Reload();
        if (Content is PlayerEditorView or TeamEditorView or CampaignWorkspace) data = new GameData(root);
        else ShowHome();
    }

    private async Task<string?> AskForGame()
    {
        while (true)
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Campaign Studio couldn't find Tape to Tape. Choose the folder that contains Tape to Tape.exe", AllowMultiple = false
            });
            string? folder = folders.FirstOrDefault()?.TryGetLocalPath();
            if (folder == null) return null;
            if (UnityGame.LooksLikeGame(folder)) return folder;
            status.Text = "That folder doesn't contain Tape to Tape. Choose the folder with Tape to Tape.exe.";
        }
    }
}
