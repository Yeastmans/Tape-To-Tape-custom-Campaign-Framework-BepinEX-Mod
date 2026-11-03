using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CampaignCore;

namespace CampaignCreator;

/// <summary>Remove only an owned local copy; archived entries never enter catalogs or exports.</summary>
public static class ContentRemoval
{
    public const string ArchiveFolder = "_removed_entries";
    public const string LockFile = ".entry-removal.lock";
    private const string ArchiveMarker = ".studio-removal-archive";
    public static bool IsArchivedPath(string path)
    {
        for (var directory = new DirectoryInfo(Path.GetFullPath(path)); directory != null; directory = directory.Parent)
            if (directory.Name.StartsWith(ArchiveFolder, StringComparison.OrdinalIgnoreCase) && File.Exists(Path.Combine(directory.FullName, ArchiveMarker))) return true;
        return false;
    }
    public static Task<bool> Confirm(Window owner, string name, string detail)
    {
        var cancel = new Button { Name = "CancelRemoveEntry", Content = "Cancel", IsDefault = true, IsCancel = true };
        var remove = new Button { Name = "ConfirmRemoveEntry", Content = "Remove", Foreground = Brush.Parse("#FF9292") };
        var dialog = new Window { Name = "RemoveEntryDialog", Title = "Remove entry", Width = 540, MinWidth = 420, Height = 300,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = StudioTheme.Surface };
        cancel.Click += (_, _) => dialog.Close(false); remove.Click += (_, _) => dialog.Close(true);
        dialog.Content = new Grid { Margin = new Thickness(24), RowDefinitions = new("*,Auto"), RowSpacing = StudioTheme.SpaceMd, Children = {
            new ScrollViewer { Content = new StackPanel { Spacing = StudioTheme.SpaceMd, Children = {
                new TextBlock { Text = "Remove “" + name + "”?", FontSize = 23, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = detail + " The original library item and copies elsewhere are kept. A local archive is retained.", TextWrapping = TextWrapping.Wrap }
            } } },
            Row(new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 12, Children = { cancel, remove } })
        } };
        return dialog.ShowDialog<bool>(owner);
    }
    private static Control Row(Control control) { Grid.SetRow(control, 1); return control; }

    public static string RemovePoolFile(string directory, string entry, IEnumerable<string>? selection = null)
    {
        string[] current = selection?.ToArray() ?? (SelectedPlayerPool.Read(directory) ?? Directory.GetFiles(directory, "*.txt")).Select(Path.GetFileName).OfType<string>().ToArray();
        string[] next = current.Where(f => !f.Equals(entry, StringComparison.OrdinalIgnoreCase)).ToArray();
        return ArchiveFile(directory, entry, () => SelectedPlayerPool.Save(directory, next));
    }
    public static string ArchiveFile(string directory, string entry, Action commit)
        => ArchiveFiles(directory, [entry], commit);
    public static string ArchiveFiles(string directory, IEnumerable<string> entries, Action commit)
        => Archive(directory, entries, false, commit);
    public static string ArchiveTeam(string directory, string entry)
        => Archive(directory, [entry], true, () => { });
    public static string RemoveCampaignTeam(string campaign, string entry)
    {
        string root = Path.GetFullPath(campaign), config = Path.Combine(root, "campaign.txt");
        SafeAncestors(config);
        if (!File.Exists(config)) throw new InvalidOperationException("The campaign no longer exists.");
        string lockPath = Path.Combine(root, LockFile); SafeAncestors(lockPath);
        using var held = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        string parent = Path.Combine(root, "player_teams"); SafeAncestors(parent);
        string[] reserved = ["draft_pool", "free_agents", "superstars", GoalieFreeAgents.Folder];
        if (reserved.Contains(entry, StringComparer.OrdinalIgnoreCase)) throw new ArgumentException("Choose a player team, not a shared pool.");
        bool only = new[] { "yes", "true", "1" }.Contains(new CampaignDocument(File.ReadAllText(config)).Get("Custom Squads Only"), StringComparer.OrdinalIgnoreCase);
        int count = Directory.GetDirectories(parent).Count(d => !reserved.Contains(Path.GetFileName(d), StringComparer.OrdinalIgnoreCase)
            && !IsArchivedPath(d) && File.Exists(Path.Combine(d, "team.txt")));
        if (only && count <= 1) throw new InvalidOperationException("Turn off Custom teams only or add another player team before removing the last one.");
        return ArchiveTeam(parent, entry);
    }

    private static string Archive(string directory, IEnumerable<string> entries, bool teams, Action commit)
    {
        string parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        string[] names = entries.ToArray();
        if (names.Length == 0 || names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Length) throw new ArgumentException("Choose an entry to remove.");
        var targets = names.Select(name =>
        {
            if (name is "." or ".." || name != Path.GetFileName(name) || name.IndexOfAny(['/', '\\', ':']) >= 0
                || (!teams && !name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("Only an entry in this list can be removed.");
            string target = Path.GetFullPath(Path.Combine(parent, name));
            if (!string.Equals(Path.GetDirectoryName(target), parent, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Entry is outside this list.");
            if (IsArchivedPath(target)) throw new ArgumentException("This entry is already in the removal archive.");
            if (teams ? !Directory.Exists(target) || !File.Exists(Path.Combine(target, "team.txt")) : !File.Exists(target)) throw new FileNotFoundException("This entry no longer exists. Reopen the list.", target);
            SafeAncestors(target); if (teams) SafeTree(target);
            return target;
        }).ToArray();
        // Starting-slot Save updates these files together. Retain their previous
        // bytes too, so a failed callback cannot leave dangling slot references.
        string[] metadata = ["selected.list", "custom-slots.list", "base-slots.list", "source.mode"];
        var previous = metadata.ToDictionary(n => n, n => { string f = Path.Combine(parent, n); SafeAncestors(f); return File.Exists(f) ? File.ReadAllBytes(f) : null; });
        // Keep archives outside pool/team discovery folders. In particular the
        // native loader interprets every child of campaign/player_teams as a squad.
        string archiveRoot = Path.GetDirectoryName(parent) ?? parent;
        for (var ancestor = new DirectoryInfo(parent); ancestor != null; ancestor = ancestor.Parent)
            if (File.Exists(Path.Combine(ancestor.FullName, "campaign.txt")) || ancestor.Name.Equals("base_game_pools", StringComparison.OrdinalIgnoreCase))
            { archiveRoot = ancestor.FullName; break; }
        string archiveBase = Path.Combine(archiveRoot, ArchiveFolder); SafeAncestors(archiveBase);
        if (Directory.Exists(archiveBase) && !IsArchivedPath(archiveBase)) archiveBase += "-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(archiveBase);
        string marker = Path.Combine(archiveBase, ArchiveMarker); SafeAncestors(marker);
        if (!File.Exists(marker)) File.WriteAllText(marker, "Campaign Studio removed entries v1\n");
        string archive = Path.Combine(archiveBase, Guid.NewGuid().ToString("N")); SafeAncestors(archive);
        Directory.CreateDirectory(archive);
        using (var metadataFile = new FileStream(archive + ".json", FileMode.CreateNew, FileAccess.Write, FileShare.None))
            System.Text.Json.JsonSerializer.Serialize(metadataFile, new { source = Path.GetRelativePath(archiveRoot, parent), metadata = previous });
        var moved = new List<(string Source, string Backup)>();
        bool committing = false;
        try
        {
            foreach (string target in targets)
            {
                SafeAncestors(target); if (teams) SafeTree(target);
                string backup = Path.Combine(archive, Path.GetFileName(target));
                if (teams) Directory.Move(target, backup); else File.Move(target, backup);
                moved.Add((target, backup));
            }
            committing = true; commit();
            return moved[0].Backup;
        }
        catch (Exception error)
        {
            var rollbackErrors = new List<Exception>();
            foreach (var (source, backup) in moved.AsEnumerable().Reverse())
            {
                try
                {
                    SafeAncestors(source); SafeAncestors(backup);
                    if (teams) Directory.Move(backup, source); else File.Move(backup, source);
                }
                catch (Exception rollback) { rollbackErrors.Add(rollback); }
            }
            if (committing) foreach (var (name, bytes) in previous)
            {
                try
                {
                    string target = Path.Combine(parent, name); SafeAncestors(target);
                    if (bytes == null) { if (File.Exists(target)) File.Delete(target); }
                    else if (!File.Exists(target) || !File.ReadAllBytes(target).SequenceEqual(bytes))
                    {
                        string temporary = target + ".restore-" + Guid.NewGuid().ToString("N");
                        try { File.WriteAllBytes(temporary, bytes); File.Move(temporary, target, true); }
                        finally { if (File.Exists(temporary)) File.Delete(temporary); }
                    }
                }
                catch (Exception rollback) { rollbackErrors.Add(rollback); }
            }
            if (rollbackErrors.Count > 0) throw new AggregateException("Removal failed and recovery needs attention. Archived copies and original selection metadata are retained at " + archive, new[] { error }.Concat(rollbackErrors));
            throw;
        }
    }
    private static void SafeAncestors(string path)
    {
        for (string? current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked files and folders cannot be removed from Studio.");
    }
    private static void SafeTree(string directory)
    {
        foreach (string path in Directory.EnumerateFileSystemEntries(directory))
        {
            SafeAncestors(path);
            if (Directory.Exists(path)) SafeTree(path);
        }
    }
}
