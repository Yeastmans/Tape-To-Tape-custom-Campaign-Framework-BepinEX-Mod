using CampaignCore;

namespace CampaignCreator;

// Home needs to know whether an editor has anything to open. Build the named
// roster, its portraits and its campaign navigation only after the user opens it.
internal sealed class HomeContent
{
    internal string Root { get; }
    internal string[] CampaignDirectories { get; }
    internal string[] SavedCampaigns { get; }
    internal bool HasSkaters { get; }
    internal bool HasPlayerTeams { get; }
    internal bool HasSavedPools { get; }
    internal string ActiveCampaign { get; } = "default";
    internal string? SelectionError { get; }

    internal HomeContent(string root, CancellationToken token = default)
    {
        Root = root;
        CampaignDirectories = FindCampaignDirectories(root);
        SavedCampaigns = CampaignDirectories.Where(d => File.Exists(Path.Combine(d, "campaign.txt"))).ToArray();
        HasSkaters = HasPlayer("skater", token);
        HasPlayerTeams = SavedCampaigns.Any(c => HasCampaignTeam(c, squads: true, token)) || HasLibraryPlayerTeam(token);
        HasSavedPools = File.Exists(Path.Combine(BaseGamePlayerPools.Folder(root), "settings.txt"));
        token.ThrowIfCancellationRequested();
        try
        {
            string path = Path.Combine(root, "active.txt");
            string? line = File.Exists(path) ? File.ReadLines(path).Select(l => l.Trim())
                .FirstOrDefault(l => l.Length > 0 && !l.StartsWith('#')) : null;
            if (line == null) SaveSelection("default");
            else if (line[(line.IndexOf('=') + 1)..].Trim() is { Length: > 0 } selection) ActiveCampaign = selection;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SelectionError = "Could not read or save the campaign selection: " + ex.Message;
        }
    }

    internal static string[] FindCampaignDirectories(string root)
    {
        string folder = Path.Combine(root, "campaigns");
        return Directory.Exists(folder) ? Directory.GetDirectories(folder).Order().ToArray() : [];
    }

    internal void SaveSelection(string value)
    {
        Directory.CreateDirectory(Root);
        string path = Path.Combine(Root, "active.txt");
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, "# Campaign to play; default uses the base game.\nActive Campaign = " + value + "\n");
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    internal bool HasOpponentTeams(CancellationToken token) =>
        Files(Path.Combine(Root, "library", "teams"), "team.txt", true).Any(f => !ContentRemoval.IsArchivedPath(f))
        || SavedCampaigns.Any(c => HasCampaignTeam(c, squads: false, token));

    private static bool HasCampaignTeam(string campaign, bool squads, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return CampaignNavigation.TeamDirectories(campaign, squads).Any(d =>
            !ContentRemoval.IsArchivedPath(d) && File.Exists(Path.Combine(d, "team.txt")));
    }

    private bool HasLibraryPlayerTeam(CancellationToken token)
    {
        string folder = Path.Combine(Root, "library", "player_teams");
        // Current teams have a team.txt. Older imports can identify themselves
        // with a Team Name field in a differently named file.
        if (Files(folder, "team.txt", true).Any(f => !ContentRemoval.IsArchivedPath(f))) return true;
        return Files(folder, "*.txt", true).Any(f => Matches(f, "team", token));
    }

    internal bool HasPlayer(string kind, CancellationToken token)
    {
        var library = Files(Path.Combine(Root, "library", "players"), "*.txt", true);
        return library.Concat(SavedCampaigns.SelectMany(c => CampaignPlayers(c, token)))
            .Any(f => Matches(f, kind, token));
    }

    private static bool Matches(string file, string kind, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (ContentRemoval.IsArchivedPath(file)) return false;
        if (Path.GetFileName(file).Equals("team.txt", StringComparison.OrdinalIgnoreCase)) return kind == "team";
        try { return ContentImport.Matches(CampaignNavigation.ReadSummary(file, token), file, kind); }
        catch (IOException) { return false; } // A file can be removed or locked while Home is opening.
        catch (UnauthorizedAccessException) { return false; }
    }

    private static IEnumerable<string> CampaignPlayers(string campaign, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        foreach (string folder in Pools)
            foreach (string file in CampaignNavigation.PoolFiles(campaign, folder)) yield return file;
        foreach (string file in Files(GoalieFreeAgents.DirectoryPath(campaign), "*.txt", false)) yield return file;
        foreach (string team in CampaignNavigation.TeamDirectories(campaign, false).Concat(CampaignNavigation.TeamDirectories(campaign, true)))
        {
            token.ThrowIfCancellationRequested();
            foreach (string file in Files(Path.Combine(team, "players"), "*.txt", true)) yield return file;
            foreach (string folder in Pools)
                foreach (string file in Files(Path.Combine(team, "player_teams", folder), "*.txt", false)) yield return file;
        }
    }

    private static readonly string[] Pools = ["draft_pool", "free_agents", "superstars"];
    private static IEnumerable<string> Files(string folder, string pattern, bool recursive) =>
        Directory.Exists(folder) ? Directory.EnumerateFiles(folder, pattern,
            recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly) : [];
}
