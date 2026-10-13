using CampaignCore;

namespace CampaignCreator;

public static class CreatorContent
{
    public static string ValidateName(string name)
    {
        name = name.Trim();
        var reserved = new[] { "CON", "PRN", "AUX", "NUL", "draft_pool", "free_agents", "superstars" }
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
        // The same native player can appear in several copied rosters/pools.
        // Keep that original name editable; a rename still uses uniqueness rules.
        if (exceptFile != null && File.Exists(exceptFile) && File.Exists(exceptFile + ".native"))
        {
            var saved = new CampaignDocument(File.ReadAllText(exceptFile));
            var native = new CampaignDocument(File.ReadAllText(exceptFile + ".native"));
            string id = native.Get(VanillaCopyData.PlayerId);
            if (id.Length > 0 && saved.Get(VanillaCopyData.PlayerId) == id && native.Get("Name").Trim() == name) return;
        }
        string? campaign = PlayerCampaign(location);
        if (campaign == null) return;
        foreach (var node in CampaignNavigation.Load(campaign).SelectMany(n => n.Descendants()))
        {
            if (node.FilePath is not string file || string.Equals(Path.GetFullPath(file), exceptFile == null ? null : Path.GetFullPath(exceptFile), StringComparison.OrdinalIgnoreCase)) continue;
            if (!ContentImport.Matches(file, "player")) continue;
            var doc = new CampaignDocument(File.ReadAllText(file));
            if (doc.Get(FreeAgentContent.Marker).Length > 0 || doc.Get(StartingPoolEditor.BaseMarker).Length > 0) continue;
            if (doc.Get("Name").Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("A custom player named '" + name + "' already exists in this campaign. Choose a different name.");
        }
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
        if (kind == "player") ValidatePlayerName(campaign ?? team ?? root, name);
        string target = kind switch {
            "campaign" => Path.Combine(root, "campaigns", name),
            "team" => campaign != null ? Path.Combine(campaign, "teams", name) : Path.Combine(root, "library", "teams", name),
            "player-team" => Path.Combine(root, "library", "player_teams", name),
            "player" => Path.Combine(team != null ? Path.Combine(team, "players") : campaign != null ? Path.Combine(campaign, "free_agents") : Path.Combine(root, "library", "players"), name + ".txt"),
            _ => throw new ArgumentException("Unknown content type.")
        };
        if (kind == "player" && File.Exists(target))
        {
            var existing = new CampaignDocument(File.ReadAllText(target));
            if (existing.Get(FreeAgentContent.Marker).Length > 0 || existing.Get(StartingPoolEditor.BaseMarker).Length > 0)
                target = Path.Combine(Path.GetDirectoryName(target)!, "custom-" + Guid.NewGuid().ToString("N") + ".txt");
        }
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
                foreach (var dir in Directory.GetDirectories(from).Where(d => !Path.GetFileName(d).StartsWith('.') && !Path.GetFileName(d).StartsWith('_')))
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
