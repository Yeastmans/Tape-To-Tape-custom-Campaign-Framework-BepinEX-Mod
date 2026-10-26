using System.Diagnostics;
using Avalonia.Controls;

namespace CampaignCreator;

public sealed partial class MainWindow
{
    private async Task UninstallStudio()
    {
        if (HasUpdateBlockingEdits())
        {
            await UpdateMessage("Save before uninstalling", "Save your edits and close other Studio dialogs, then choose Uninstall again.");
            return;
        }
        string uninstaller = Path.Combine(AppContext.BaseDirectory, "unins000.exe");
        if (!OperatingSystem.IsWindows() || !File.Exists(uninstaller))
        {
            await UpdateMessage("Uninstaller unavailable", "This copy has no Windows uninstaller. If Studio is installed elsewhere, open that copy or uninstall it from Windows Settings > Apps > Installed apps.");
            return;
        }
        if (GameRunningForUninstall())
        {
            await UpdateMessage("Close Tape to Tape", "Close the game before uninstalling Studio and the mod.");
            return;
        }
        var proceed = new Button { Name = "StudioUninstallConfirm", Content = "Open uninstaller" };
        var cancel = new Button { Name = "StudioUninstallCancel", Content = "Cancel", IsDefault = true, IsCancel = true };
        var dialog = UpdateDialog("Uninstall Campaign Studio", "Open the Windows uninstaller?",
            "Studio will close and the Windows uninstaller will ask you to confirm removing Campaign Studio, BepInEx and all mods in the game's BepInEx folder. Your saved campaigns, players and custom heads are kept in Local AppData.", cancel, proceed);
        dialog.Name = "StudioUninstallDialog";
        proceed.Click += (_, _) => dialog.Close(true);
        cancel.Click += (_, _) => dialog.Close(false);
        if (!await dialog.ShowDialog<bool>(this)) return;
        if (HasUpdateBlockingEdits() || GameRunningForUninstall())
        {
            await UpdateMessage("Uninstall postponed", "Save any edits and close Tape to Tape and other Studio dialogs, then choose Uninstall again.");
            return;
        }
        try
        {
            var start = new ProcessStartInfo(uninstaller) { UseShellExecute = true };
            start.ArgumentList.Add("/NORESTART");
            using var process = Process.Start(start) ?? throw new IOException("Windows could not open the uninstaller.");
            compatibilityTimer?.Stop();
            Close();
        }
        catch (Exception ex)
        {
            await UpdateMessage("Uninstall did not start", ex.Message + " Studio is still open.");
        }
    }

    private static bool GameRunningForUninstall()
    {
        var processes = Process.GetProcessesByName("Tape to Tape");
        try { return processes.Length > 0; }
        finally { foreach (var process in processes) process.Dispose(); }
    }
}
