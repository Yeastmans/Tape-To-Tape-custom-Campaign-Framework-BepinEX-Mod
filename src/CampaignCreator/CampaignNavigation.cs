using CampaignCore;

namespace CampaignCreator;

public sealed record CampaignNode(string DisplayName, string Context, string? FilePath, List<CampaignNode> Children)
{
    public bool IsExpanded { get; set; }
    public IEnumerable<CampaignNode> Descendants() => new[] { this }.Concat(Children.SelectMany(c => c.Descendants()));
}

public static class CampaignNavigation
{
    public static List<CampaignNode> Load(string root)
    {
        if (!Directory.Exists(root)) return [];
        string Label(string file, bool team = false)
        {
            var d = new CampaignDocument(File.ReadAllText(file));
            var fallback = Path.GetFileNameWithoutExtension(file).Replace('_', ' ');
            return d.Get(team ? "Team Name" : "Name", d.Get(team ? "Import Team" : "Import Player", fallback));
        }
        CampaignNode Player(string file)
        {
            var d = new CampaignDocument(File.ReadAllText(file));
            var position = d.Get("Position", Path.GetFileNameWithoutExtension(file).Split(" - ")[0]);
            return new(Label(file), position, file, []);
        }
        CampaignNode Team(string dir, int order)
        {
            string file = Path.Combine(dir, "team.txt");
            var players = Path.Combine(dir, "players");
            return new(File.Exists(file) ? Label(file, true) : Path.GetFileName(dir).Replace('_', ' '), order > 0 ? $"Opponent {order}" : "Custom squad", File.Exists(file) ? file : null,
                Directory.Exists(players) ? Directory.GetFiles(players, "*.txt", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase).Select(Player).ToList() : []);
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
        var reserved = new[] { "draft_pool", "free_agents", "superstars" };
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
        return nodes;
    }
    public static List<CampaignNode> Filter(IEnumerable<CampaignNode> nodes, string query) => nodes.Select(n =>
        (n.DisplayName + " " + n.Context).Contains(query, StringComparison.OrdinalIgnoreCase) ? n : n with { Children = Filter(n.Children, query) })
        .Where(n => n.Children.Count > 0 || (n.DisplayName + " " + n.Context).Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
}
