using CampaignCore;

namespace CampaignCreator;

public sealed record CampaignNode(string DisplayName, string Context, string? FilePath, List<CampaignNode> Children)
{
    public bool IsExpanded { get; set; }
    public bool IsNativeOpponent { get; init; }
    internal int RosterCount { get; init; }
    internal string BaseFreeAgent { get; init; } = "";
    public IEnumerable<CampaignNode> Descendants() => new[] { this }.Concat(Children.SelectMany(c => c.Descendants()));
}

public static class CampaignNavigation
{
    private static readonly HashSet<string> SummaryFields = new(StringComparer.OrdinalIgnoreCase)
        { "Name", "Team Name", "Import Player", "Import Team", "Position", "Export Role", "Catching", "Goalie Skill", FreeAgentContent.Marker, VanillaCopyData.OpponentId };

    internal static CampaignDocument ReadSummary(string file, CancellationToken token = default)
    {
        // Embedded portraits/reward snapshots can dwarf the roster metadata.
        // List navigation needs only these fields, not a parsed copy of every
        // embedded value for every refresh. Keep duplicate-field semantics.
        var lines = new List<string>();
        foreach (string line in File.ReadLines(file))
        {
            token.ThrowIfCancellationRequested();
            int equals = line.IndexOf('=');
            if (equals > 0 && SummaryFields.Contains(line[..equals].Replace('\t', ' ').Trim())) lines.Add(line);
        }
        return new CampaignDocument(string.Join('\n', lines));
    }

    internal static string Label(string file, CampaignDocument d, bool team = false)
    {
        var fallback = Path.GetFileNameWithoutExtension(file).Replace('_', ' ');
        return d.Get(team ? "Team Name" : "Name", d.Get(team ? "Import Team" : "Import Player", fallback));
    }
    private static CampaignNode Player(string file)
    {
        var d = ReadSummary(file);
        var position = d.Get("Position", Path.GetFileNameWithoutExtension(file).Split(" - ")[0]);
        return new(Label(file, d), position, file, []) { BaseFreeAgent = d.Get(FreeAgentContent.Marker) };
    }
    internal static IEnumerable<string> TeamDirectories(string campaign, bool squads)
    {
        string parent = Path.Combine(campaign, squads ? "player_teams" : "teams");
        if (!Directory.Exists(parent)) return [];
        string[] reserved = ["draft_pool", "free_agents", "superstars", GoalieFreeAgents.Folder];
        return Directory.EnumerateDirectories(parent).Where(d => !squads ||
            !reserved.Contains(Path.GetFileName(d), StringComparer.OrdinalIgnoreCase) && !ContentRemoval.IsArchivedPath(d));
    }
    internal static List<CampaignNode> Teams(string campaign, bool squads, bool includePlayers = false, CancellationToken token = default)
    {
        return TeamDirectories(campaign, squads).Order(StringComparer.OrdinalIgnoreCase).Select((dir, index) =>
        {
            token.ThrowIfCancellationRequested();
            string file = Path.Combine(dir, "team.txt");
            var summary = File.Exists(file) ? ReadSummary(file, token) : null;
            var players = Path.Combine(dir, "players");
            var members = Directory.Exists(players) ? Directory.EnumerateFiles(players, "*.txt", SearchOption.AllDirectories) : [];
            var children = includePlayers ? members.Order(StringComparer.OrdinalIgnoreCase).Select(Player).ToList() : [];
            int count = includePlayers ? children.Count : squads ? members.Count() : 0;
            foreach (var (pool, title) in new[] { ("draft_pool", "Bench"), ("superstars", "Superstars"), ("free_agents", "Free agents") })
            {
                string path = Path.Combine(dir, "player_teams", pool);
                if (!Directory.Exists(path)) continue;
                count++;
                if (includePlayers) children.Add(new("Squad " + title, "Player pool", null,
                    Directory.GetFiles(path, "*.txt").Order(StringComparer.OrdinalIgnoreCase).Select(Player).ToList()));
            }
            return new CampaignNode(summary != null ? Label(file, summary, true) : Path.GetFileName(dir).Replace('_', ' '),
                squads ? "Custom squad" : $"Opponent {index + 1}", summary != null ? file : null, children)
                { IsNativeOpponent = summary?.Get(VanillaCopyData.OpponentId).Length > 0, RosterCount = count };
        }).ToList();
    }
    internal static IEnumerable<string> PoolFiles(string campaign, string folder)
    {
        foreach (string parent in new[] { campaign, Path.Combine(campaign, "player_teams") })
        {
            string dir = Path.Combine(parent, folder);
            if (Directory.Exists(dir))
                foreach (string file in Directory.EnumerateFiles(dir, "*.txt", SearchOption.AllDirectories).Where(f => !ContentRemoval.IsArchivedPath(f)))
                    yield return file;
        }
    }
    internal static List<CampaignNode> PoolPlayers(string campaign, string folder)
        => PoolFiles(campaign, folder).Order(StringComparer.OrdinalIgnoreCase).Select(Player).ToList();

    public static List<CampaignNode> Load(string root)
    {
        if (!Directory.Exists(root)) return [];
        var nodes = new List<CampaignNode>();
        foreach (var file in Directory.GetFiles(root, "*.txt").Order(StringComparer.OrdinalIgnoreCase))
        {
            string stem = Path.GetFileNameWithoutExtension(file);
            if (stem.Equals("save", StringComparison.OrdinalIgnoreCase)) continue;
            nodes.Add(new(stem.Equals("campaign", StringComparison.OrdinalIgnoreCase) ? "Campaign Settings" : System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(stem.Replace('_', ' ')), "", file, []));
        }
        nodes.AddRange(Teams(root, false, includePlayers: true));
        var squads = Teams(root, true, includePlayers: true);
        if (squads.Count > 0) nodes.Add(new("Custom Squads", "", null, squads));
        foreach (var (folder, label) in new[] { ("draft_pool", "Starting Bench"), ("free_agents", "Free Agents"), ("superstars", "Superstars") })
        {
            var children = PoolPlayers(root, folder);
            if (children.Count > 0) nodes.Add(new(label, "", null, children));
        }
        string goalies = GoalieFreeAgents.DirectoryPath(root);
        if (Directory.Exists(goalies)) nodes.Add(new("Goalie Free Agents", "", null,
            Directory.GetFiles(goalies, "*.txt").Order(StringComparer.OrdinalIgnoreCase).Select(Player).ToList()));
        return nodes;
    }
    public static List<CampaignNode> Filter(IEnumerable<CampaignNode> nodes, string query) => nodes.Select(n =>
        (n.DisplayName + " " + n.Context).Contains(query, StringComparison.OrdinalIgnoreCase) ? n : n with { Children = Filter(n.Children, query) })
        .Where(n => n.Children.Count > 0 || (n.DisplayName + " " + n.Context).Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
}
