using CampaignCore;

namespace CampaignCreator;

public static class CreatorContent
{
    public static string ValidateName(string name)
    {
        name = name.Trim();
        var reserved = new[] { "CON", "PRN", "AUX", "NUL", "draft_pool", "free_agents", "superstars", GoalieFreeAgents.Folder }
            .Concat(Enumerable.Range(1, 9).SelectMany(i => new[] { "COM" + i, "LPT" + i }));
        if (name.Length == 0 || name.Length > 80 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.EndsWith('.') || reserved.Contains(name.Split('.')[0], StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Enter a name of 1–80 characters without filename punctuation or a reserved Windows name.");
        return name;
    }

    public static string? PlayerCampaign(string location)
    {
        var directory = new DirectoryInfo(Directory.Exists(location) ? location : Path.GetDirectoryName(location)!);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "campaign.txt"))) return directory.FullName;
            directory = directory.Parent;
        }
        return null;
    }

    public static void ValidatePlayerName(string location, string name, string? exceptFile = null)
    {
        name = ValidateName(name);
        // Roster and pool imports create independent copies. Editing one must
        // remain possible even when another copy already has the same name.
        // New players and actual renames still use the uniqueness rules below.
        if (exceptFile != null && File.Exists(exceptFile))
        {
            var saved = new CampaignDocument(File.ReadAllText(exceptFile));
            if (saved.Get("Name").Trim().Equals(name, StringComparison.OrdinalIgnoreCase)) return;
        }
        string? campaign = PlayerCampaign(location);
        if (campaign == null) return;
        string directory = Directory.Exists(location) ? location : Path.GetDirectoryName(location)!;
        var files = IsFreeAgentDirectory(directory)
            ? Directory.EnumerateFiles(directory, "*.txt", SearchOption.AllDirectories)
            : CampaignNavigation.Load(campaign).SelectMany(n => n.Descendants()).Select(n => n.FilePath).OfType<string>();
        foreach (string file in files)
        {
            if (string.Equals(Path.GetFullPath(file), exceptFile == null ? null : Path.GetFullPath(exceptFile), StringComparison.OrdinalIgnoreCase)) continue;
            if (!ContentImport.Matches(file, "player")) continue;
            var doc = new CampaignDocument(File.ReadAllText(file));
            if (doc.Get(FreeAgentContent.Marker).Length > 0 || doc.Get(StartingPoolEditor.BaseMarker).Length > 0) continue;
            if (doc.Get("Name").Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("A custom player named '" + name + "' already exists in this "
                    + (IsFreeAgentDirectory(directory) ? "free-agent pool" : "campaign") + ". Choose a different name.");
        }
    }

    private static bool IsFreeAgentDirectory(string directory) =>
        Path.GetFileName(Path.TrimEndingDirectorySeparator(directory)).Equals("free_agents", StringComparison.OrdinalIgnoreCase);

    // Free agents are independent recruits: names elsewhere in the campaign
    // must not rename them. Only another copy in this pool needs a suffix.
    // Other player collections retain the existing campaign-wide naming rules.
    public static string AvailablePlayerName(string directory, string requested, IEnumerable<string>? reserved = null)
    {
        requested = ValidateName(requested);
        string scope = IsFreeAgentDirectory(directory) ? directory : PlayerCampaign(directory) ?? directory;
        var names = new HashSet<string>(reserved ?? [], StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(scope))
            foreach (string file in Directory.EnumerateFiles(scope, "*.txt", SearchOption.AllDirectories))
            {
                if (Path.GetRelativePath(scope, file).Split(Path.DirectorySeparatorChar).SkipLast(1)
                    .Any(part => part.StartsWith('.') || part.StartsWith('_')) || !ContentImport.Matches(file, "player")) continue;
                var player = new CampaignDocument(File.ReadAllText(file));
                names.Add(player.Get("Name", player.Get("Import Player")).Trim());
            }
        bool Taken(string name) => names.Contains(name) || File.Exists(Path.Combine(directory, name + ".txt"))
            || Directory.Exists(Path.Combine(directory, name + ".txt"));
        if (!Taken(requested)) return requested;
        string basis = requested;
        int space = requested.LastIndexOf(' ');
        if (space > 0 && int.TryParse(requested[(space + 1)..], out int suffix) && suffix >= 2 && names.Contains(requested[..space]))
            basis = requested[..space];
        for (int number = 2; number < int.MaxValue; number++)
        {
            string ending = " " + number;
            string candidate = basis[..Math.Min(basis.Length, 80 - ending.Length)].TrimEnd() + ending;
            if (!Taken(candidate)) return candidate;
        }
        throw new ArgumentException("No numbered copy name is available.");
    }

    public static CampaignDocument CustomPlayerCopy(string source, string name)
    {
        var doc = source.Length == 0 ? new CampaignDocument("Position = Center\n") : ContentImport.ReadDocument(source);
        doc.Set("Name", ValidateName(name));
        doc.Set(FreeAgentContent.Marker, ""); doc.Set(StartingPoolEditor.BaseMarker, "");
        doc.Set("Studio Player Source", "custom");
        return doc;
    }

    // Stage authoring content outside campaigns so partially copied items never appear in the game.
    public static string Create(string root, string kind, string name, string source, string? campaign, string? team, string? initialCampaignText = null, string initialPlayerPosition = "Center", string? defaultLookRoot = null)
    {
        name = ValidateName(name);
        string playerDirectory = team != null ? Path.Combine(team, "players") : campaign != null ? Path.Combine(campaign, "free_agents") : Path.Combine(root, "library", "players");
        if (kind == "player") name = AvailablePlayerName(playerDirectory, name);
        string target = kind switch {
            "campaign" => Path.Combine(root, "campaigns", name),
            "team" => campaign != null ? Path.Combine(campaign, "teams", name) : Path.Combine(root, "library", "teams", name),
            "player-team" => Path.Combine(root, "library", "player_teams", name),
            "player" => Path.Combine(playerDirectory, name + ".txt"),
            _ => throw new ArgumentException("Unknown content type.")
        };
        if (File.Exists(target) || Directory.Exists(target)) throw new ArgumentException("That name already exists. Choose a different name.");
        string stage = Path.Combine(root, ".studio-staging", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        try
        {
            void Copy(string from, string to)
            {
                if ((File.GetAttributes(from) & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("Linked content cannot be used as a template.");
                Directory.CreateDirectory(to);
                foreach (var file in Directory.GetFiles(from))
                {
                    if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("Linked content cannot be used as a template.");
                    if (!Path.GetFileName(file).Equals("save.txt", StringComparison.OrdinalIgnoreCase))
                    {
                        string copied = Path.Combine(to, Path.GetFileName(file));
                        File.Copy(file, copied); ContentImport.ValidateCopiedFile(copied);
                    }
                }
                foreach (var dir in Directory.GetDirectories(from).Where(d => !Path.GetFileName(d).StartsWith('.') && !Path.GetFileName(d).StartsWith('_')
                    && (kind == "campaign" || !Path.GetFileName(d).Equals(SquadMapOverrides.Folder, StringComparison.OrdinalIgnoreCase))))
                    Copy(dir, Path.Combine(to, Path.GetFileName(dir)));
            }
            string stagedFile;
            if (kind == "player")
            {
                stagedFile = Path.Combine(stage, "player.txt");
                var doc = CustomPlayerCopy(source, name);
                if (source.Length == 0)
                {
                    doc.Set("Position", initialPlayerPosition);
                    DefaultPlayerLook.Apply(root, doc);
                    doc.Set("Override Team Settings", "no");
                }
                // Imports retain the source's equipment mode. Resetting it here
                // hides valid authored premade kits behind the destination team.
                doc.Save(stagedFile);
            }
            else
            {
                if (source.Length > 0) Copy(source, stage);
                stagedFile = Path.Combine(stage, kind == "campaign" ? "campaign.txt" : "team.txt");
                var doc = new CampaignDocument(File.Exists(stagedFile) ? File.ReadAllText(stagedFile) : kind == "campaign" ? initialCampaignText ?? "# Campaign Settings\nAct Sequence = 1,2,3\nMaps = Basic\n" : "");
                if (kind is "team" or "player-team")
                {
                    doc.Set("Team Name", name);
                    doc.Remove(SquadMapOverrides.ModeKey);
                    if (source.Length == 0) DefaultPlayerLook.ApplyTeamLook(defaultLookRoot ?? root, doc, stage);
                }
                doc.Save(stagedFile);
                if (kind is "team" or "player-team") Directory.CreateDirectory(Path.Combine(stage, "players"));
                if (kind is "team" or "player-team") RosterLines.Normalize(Path.Combine(stage, "players"));
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (kind == "player") File.Move(stagedFile, target); else Directory.Move(stage, target);
            return kind == "player" ? target : Path.Combine(target, kind == "campaign" ? "campaign.txt" : "team.txt");
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
    }
}
