using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using GameArt;
using ModCompatibility;

namespace CampaignCreator;

public sealed partial class MainWindow
{
    private readonly CancellationTokenSource updateLifetime = new();
    private readonly HashSet<long> offeredUpdates = [];
    private Task<bool>? releaseCheck;
    private bool updateInstallerStarted;
    private string releaseStatus = "Studio checks GitHub for Studio and mod updates at startup and before launching the game.";
    private TextBlock? releaseHomeStatus;

    private void SetReleaseStatus(string message)
    {
        releaseStatus = message;
        if (releaseHomeStatus != null) releaseHomeStatus.Text = message;
    }

    private async Task StartStudio()
    {
        Closed += (_, _) => updateLifetime.Cancel();
        if (!await CheckForStudioUpdates()) return;
        if (BuildGameArtOnStart) await EnsureGameArt();
        if (!IsVisible || updateInstallerStarted) return;
        StartCompatibilityUpdates();
        await PromptForDefaultLook();
    }

    // False means Studio is closing to run Setup; a failed network check never closes the editor.
    private async Task<bool> CheckForStudioUpdates(bool manual = false)
    {
        if (releaseCheck != null) return await releaseCheck;
        releaseCheck = CheckRelease(manual);
        try { return await releaseCheck; }
        finally { releaseCheck = null; }
    }

    private async Task<bool> CheckRelease(bool manual)
    {
        if (updateInstallerStarted || updateLifetime.IsCancellationRequested) return false;
        SetReleaseStatus("Checking GitHub for Studio and mod updates…");
        try
        {
            var installed = StudioUpdates.ReadBuild(AppContext.BaseDirectory);
            var update = await StudioUpdates.FindLatestAsync(updateLifetime.Token);
            if (!IsVisible) return false;
            if (!StudioUpdates.IsNewer(installed, update))
            {
                SetReleaseStatus(installed.BuildId == update.Manifest.BuildId
                    ? installed.Label + " · Studio and mod release is up to date."
                    : installed.Label + " · This build is newer than the published GitHub package.");
                if (manual) await UpdateMessage("Check for updates", releaseStatus);
                return true;
            }
            SetReleaseStatus("Update available: " + update.Manifest.Label + ". Use Check for updates to install it.");
            if (!manual && !offeredUpdates.Add(update.Manifest.BuildId)) return true;
            offeredUpdates.Add(update.Manifest.BuildId);
            if (!await AskToInstallUpdate(installed, update)) return true;
            return !await InstallStudioUpdate(update);
        }
        catch (OperationCanceledException) when (updateLifetime.IsCancellationRequested) { return false; }
        catch (Exception ex)
        {
            SetReleaseStatus("Could not check for updates. " + (ex is OperationCanceledException ? "GitHub did not respond in time." : ex.Message)
                + " You can keep editing and retry with Check for updates.");
            if (manual && IsVisible) await UpdateMessage("Update check unavailable", releaseStatus);
            return true;
        }
    }

    private Task<bool> AskToInstallUpdate(StudioBuild installed, StudioUpdate update)
    {
        var install = new Button { Name = "StudioUpdateInstall", Content = "Download and install", IsDefault = true };
        var later = new Button { Name = "StudioUpdateLater", Content = "Later", IsCancel = true };
        var page = new Button { Name = "StudioUpdateReleasePage", Content = "View release" };
        StudioTheme.Primary(install);
        var dialog = UpdateDialog("Studio update available", "A Studio and mod update is ready",
            "Installed: " + installed.Label + "\nAvailable: " + update.Manifest.Label + " (build "
                + DateTimeOffset.FromUnixTimeMilliseconds(update.Manifest.BuildId).ToLocalTime().ToString("g") + ")\n\n"
                + "Download and run the Windows installer to update Studio and the bundled mod together. Save your edits and close Tape to Tape first. "
                + "Studio will close when Setup starts and reopen after installation. Your saved campaigns, players and custom heads are kept.",
            page, later, install);
        install.Click += (_, _) => dialog.Close(true);
        later.Click += (_, _) => dialog.Close(false);
        page.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(update.ReleaseUrl) { UseShellExecute = true }); }
            catch (Exception ex) { SetReleaseStatus("Could not open the release page: " + ex.Message); }
        };
        return dialog.ShowDialog<bool>(this);
    }

    private bool HasUpdateBlockingEdits() => dirty || campaignWorkspace?.HasUnsavedChanges == true
        || OwnedWindows.Any(w => w.IsVisible);

    private async Task<bool> InstallStudioUpdate(StudioUpdate update)
    {
        if (!OperatingSystem.IsWindows())
        {
            await UpdateMessage("Windows installer", "Automatic installation is currently available for Windows only.");
            return false;
        }
        string? game = ArtCache.FindGame(Path.Combine(root, "game-folder.txt"));
        if (game == null)
        {
            game = await AskForGame();
            if (game == null) return false;
            ArtCache.Remember(game);
        }
        if (HasUpdateBlockingEdits())
        {
            await UpdateMessage("Save before updating", "Save your edits and close other Studio dialogs, then choose Check for updates again.");
            return false;
        }
        if (CompatibilityUpdater.IsGameRunning(game))
        {
            await UpdateMessage("Close Tape to Tape", "Close the game, then choose Check for updates again to install the update.");
            return false;
        }

        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(updateLifetime.Token);
        var progress = new ProgressBar { Minimum = 0, Maximum = 100, Height = 8 };
        var line = new TextBlock { Text = "Downloading the verified GitHub installer…", TextWrapping = TextWrapping.Wrap };
        var stop = new Button { Content = "Cancel", IsCancel = true };
        var downloading = UpdateDialog("Downloading update", "Updating Studio and the mod", "The installer will open after the download is verified.", stop);
        var content = (StackPanel)downloading.Content!;
        content.Children.Insert(2, progress); content.Children.Insert(3, line);
        stop.Click += (_, _) => downloading.Close();
        downloading.Closing += (_, _) => cancel.Cancel();
        _ = downloading.ShowDialog(this);
        string installer;
        try
        {
            installer = await StudioUpdates.DownloadAsync(update, new Progress<double>(value =>
            {
                progress.Value = value; line.Text = "Downloading… " + value.ToString("0") + "%";
            }), cancel.Token);
            cancel.Token.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException)
        {
            SetReleaseStatus("Update download cancelled or timed out. Nothing was installed.");
            return false;
        }
        catch (Exception ex)
        {
            SetReleaseStatus("Update download failed: " + ex.Message + " Nothing was installed.");
            return false;
        }
        finally { downloading.Close(); }
        // The user may have started the game or another editor while the download was in progress.
        if (!IsVisible || HasUpdateBlockingEdits()) return false;
        if (CompatibilityUpdater.IsGameRunning(game))
        {
            await UpdateMessage("Close Tape to Tape", "The game was started during the download. Close it and choose Check for updates again.");
            return false;
        }
        try
        {
            var start = new ProcessStartInfo(installer) { UseShellExecute = true };
            start.ArgumentList.Add("/DIR=" + Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory));
            start.ArgumentList.Add("/GAMEDIR=" + game);
            start.ArgumentList.Add("/NORESTART");
            start.ArgumentList.Add("/STUDIOUPDATE=1");
            using var setup = Process.Start(start) ?? throw new IOException("Windows could not start Setup.");
            updateInstallerStarted = true;
            compatibilityTimer?.Stop();
            Close();
            return true;
        }
        catch (Exception ex)
        {
            SetReleaseStatus("Setup did not start: " + ex.Message + " Studio is still open.");
            await UpdateMessage("Update not installed", releaseStatus);
            return false;
        }
    }

    private async Task LaunchGameWithUpdateCheck()
    {
        if (!await CheckForStudioUpdates(manual: false) || readingGameArt) return;
        await EnsureModCompatibility(force: true);
        if (updateInstallerStarted || !IsVisible) return;
        if (compatibilityState is not ("Current" or "Updated"))
        {
            await UpdateMessage("Game compatibility needs attention", compatibilityStatus + "\n\nUse Check for updates to look for a repaired release.");
            return;
        }
        string? game = ArtCache.FindGame(Path.Combine(root, "game-folder.txt"));
        if (game == null) { await UpdateMessage("Game folder unavailable", "Use Read game artwork to choose your Tape to Tape installation first."); return; }
        if (CompatibilityUpdater.IsGameRunning(game)) { await UpdateMessage("Tape to Tape is running", "Close the game before launching it with the selected campaign."); return; }
        try { Process.Start(new ProcessStartInfo("steam://rungameid/1566200") { UseShellExecute = true }); }
        catch (Exception ex) { await UpdateMessage("Could not launch Tape to Tape", ex.Message); }
    }

    private Task UpdateMessage(string title, string message)
    {
        var close = new Button { Content = "OK", IsDefault = true, IsCancel = true };
        var dialog = UpdateDialog(title, title, message, close);
        close.Click += (_, _) => dialog.Close();
        return dialog.ShowDialog(this);
    }

    private static Window UpdateDialog(string title, string heading, string message, params Button[] buttons)
    {
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = StudioTheme.SpaceSm };
        foreach (var button in buttons) actions.Children.Add(button);
        return new Window
        {
            Title = title, Width = 590, SizeToContent = SizeToContent.Height, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = StudioTheme.Surface,
            Content = new StackPanel { Margin = new Thickness(24), Spacing = StudioTheme.SpaceMd, Children = {
                new TextBlock { Text = heading, FontSize = 23, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }, actions
            } }
        };
    }
}
