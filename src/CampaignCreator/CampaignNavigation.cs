using CampaignCore;

namespace CampaignCreator;

public sealed record CampaignNode(string DisplayName, string Context, string? FilePath, List<CampaignNode> Children)
{
    public bool IsExpanded { get; set; }
    public bool IsNativeOpponent { get; init; }
    public IEnumerable<CampaignNode> Descendants() => new[] { this }.Concat(Children.SelectMany(c => c.Descendants()));
}

public static class CampaignNavigation
{
    private static readonly HashSet<string> SummaryFields = new(StringComparer.OrdinalIgnoreCase)
        { "Name", "Team Name", "Import Player", "Import Team", "Position", VanillaCopyData.OpponentId };

    private static CampaignDocument ReadSummary(string file)
    {
        // Embedded portraits/reward snapshots can dwarf the roster metadata.
        // List navigation needs only these fields, not a parsed copy of every
        // embedded value for every refresh. Keep duplicate-field semantics.
        var lines = new List<string>();
        foreach (string line in File.ReadLines(file))
        {
            int equals = line.IndexOf('=');
            if (equals > 0 && SummaryFields.Contains(line[..equals].Replace('\t', ' ').Trim())) lines.Add(line);
        }
        return new CampaignDocument(string.Join('\n', lines));
    }

    public static List<CampaignNode> Load(string root)
    {
        if (!Directory.Exists(root)) return [];
        string Label(string file, CampaignDocument d, bool team = false)
        {
            var fallback = Path.GetFileNameWithoutExtension(file).Replace('_', ' ');
            return d.Get(team ? "Team Name" : "Name", d.Get(team ? "Import Team" : "Import Player", fallback));
        }
        CampaignNode Player(string file)
        {
            var d = ReadSummary(file);
            var position = d.Get("Position", Path.GetFileNameWithoutExtension(file).Split(" - ")[0]);
            return new(Label(file, d), position, file, []);
        }
        CampaignNode Team(string dir, int order)
        {
            string file = Path.Combine(dir, "team.txt");
            var summary = File.Exists(file) ? ReadSummary(file) : null;
            var players = Path.Combine(dir, "players");
            var children = Directory.Exists(players) ? Directory.GetFiles(players, "*.txt", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase).Select(Player).ToList() : [];
            foreach (var (pool, title) in new[] { ("draft_pool", "Bench"), ("superstars", "Superstars"), ("free_agents", "Free agents") })
            {
                string path = Path.Combine(dir, "player_teams", pool);
                if (Directory.Exists(path)) children.Add(new("Squad " + title, "Player pool", null,
                    Directory.GetFiles(path, "*.txt").Order(StringComparer.OrdinalIgnoreCase).Select(Player).ToList()));
            }
            return new(summary != null ? Label(file, summary, true) : Path.GetFileName(dir).Replace('_', ' '), order > 0 ? $"Opponent {order}" : "Custom squad", summary != null ? file : null,
                children) { IsNativeOpponent = summary?.Get(VanillaCopyData.OpponentId).Length > 0 };
        }
        var nodes = new List<CampaignNode>();
        foreach (var file in Directory.GetFiles(root, "*.txt").Order(StringComparer.OrdinalIgnoreCase))
        {
            string stem = Path.GetFileNameWithoutExtension(file);
            if (stem.Equals("save", StringComparison.OrdinalIgnoreCase)) continue;
            nodes.Add(new(stem.Equals("campaign", StringComparison.OrdinalIgnoreCase) ? "Campaign Settings" : System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(stem.Replace('_', ' ')), "", file, []));
        }
        string teams = Path.Combine(root, "teams");
        if (Directory.Exists(teams)) nodes.AddRange(Directory.GetDirectories(teams).Order(StringComparer.OrdinalIgnoreCase).Select((dir, i) => Team(dir, i + 1)));
        string squads = Path.Combine(root, "player_teams");
        var reserved = new[] { "draft_pool", "free_agents", "superstars", GoalieFreeAgents.Folder };
        if (Directory.Exists(squads))
        {
            var children = Directory.GetDirectories(squads).Order(StringComparer.OrdinalIgnoreCase)
                .Where(d => !reserved.Contains(Path.GetFileName(d), StringComparer.OrdinalIgnoreCase)).Select(d => Team(d, 0)).ToList();
            if (children.Count > 0) nodes.Add(new("Custom Squads", "", null, children));
        }
        foreach (var (folder, label) in new[] { ("draft_pool", "Starting Bench"), ("free_agents", "Free Agents"), ("superstars", "Superstars") })
        {
            var children = new List<CampaignNode>();
            foreach (var parent in new[] { root, squads })
            {
                string dir = Path.Combine(parent, folder);
                if (Directory.Exists(dir)) children.AddRange(Directory.GetFiles(dir, "*.txt", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase).Select(Player));
            }
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
