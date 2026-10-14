using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CampaignCore;

namespace CampaignCreator;

public sealed partial class CampaignWorkspace
{
    private readonly Button deleteCampaign = new() { Name = "DeleteCampaign", Content = "Delete campaign", Foreground = Brush.Parse("#FF9292"), Padding = StudioTheme.ButtonPadding };
    private bool deletingCampaign;

    private async Task DeleteCampaign()
    {
        if (isNew || deletingCampaign) return;
        deletingCampaign = true;
        try
        {
            string target = ValidateDeleteTarget();
            string name = Path.GetFileName(target);
            var cancel = new Button { Name = "CancelDeleteCampaign", Content = "Cancel", IsDefault = true, IsCancel = true };
            var confirm = new Button { Name = "ConfirmDeleteCampaign", Content = "Delete campaign", Foreground = Brush.Parse("#FF9292") };
            var dialog = new Window {
                Name = "DeleteCampaignDialog", Title = "Delete campaign", Width = 540, SizeToContent = SizeToContent.Height,
                CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = StudioTheme.Surface,
                Content = new StackPanel { Margin = new Thickness(24), Spacing = StudioTheme.SpaceMd, Children = {
                    new TextBlock { Text = "Delete \u201c" + name + "\u201d?", FontSize = 23, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap },
                    new TextBlock { Text = "Permanently delete this campaign from this computer, including its maps, opponents and campaign players? This cannot be undone. Shared library content and custom head projects are kept."
                        + (HasUnsavedChanges ? " Unsaved campaign edits will also be discarded." : ""), TextWrapping = TextWrapping.Wrap },
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = StudioTheme.SpaceSm, Children = { cancel, confirm } }
                } }
            };
            cancel.Click += (_, _) => dialog.Close(false);
            confirm.Click += (_, _) => dialog.Close(true);
            if (!await dialog.ShowDialog<bool>(owner)) return;
            // Recheck after confirmation; only a direct, unlinked saved campaign may be removed.
            target = ValidateDeleteTarget();
            var games = Process.GetProcessesByName("Tape to Tape");
            try { if (games.Length > 0) throw new InvalidOperationException("Close Tape to Tape before deleting a campaign, then try again."); }
            finally { foreach (var game in games) game.Dispose(); }
            ValidateDeleteTree(target);
            string activeFile = Path.Combine(Path.GetFullPath(root), "active.txt");
            if (File.Exists(activeFile))
            {
                RejectLink(activeFile);
                string? line = File.ReadLines(activeFile).Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0 && !l.StartsWith('#'));
                string active = line == null ? "" : line[(line.IndexOf('=') + 1)..].Trim();
                if (active.Equals(name, StringComparison.OrdinalIgnoreCase))
                    new CampaignDocument("# Campaign to play; default uses the base game.\nActive Campaign = default\n").Save(activeFile);
            }
            Directory.Delete(target, recursive: true);
            back();
        }
        catch (Exception ex) { message.Text = "Could not delete campaign: " + ex.Message; }
        finally { deletingCampaign = false; }
    }

    private string ValidateDeleteTarget()
    {
        string target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        string campaigns = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(root, "campaigns")));
        if (!string.Equals(Path.GetDirectoryName(target), campaigns, StringComparison.OrdinalIgnoreCase)
            || !File.Exists(Path.Combine(target, "campaign.txt")))
            throw new InvalidOperationException("Only a saved campaign in this Studio's campaigns folder can be deleted.");
        for (var folder = new DirectoryInfo(target); folder != null; folder = folder.Parent) RejectLink(folder.FullName);
        return target;
    }

    private static void RejectLink(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Linked files or folders cannot be deleted from Studio.");
    }

    private static void ValidateDeleteTree(string directory)
    {
        foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
        {
            RejectLink(entry);
            if (Directory.Exists(entry)) ValidateDeleteTree(entry);
        }
    }
}
