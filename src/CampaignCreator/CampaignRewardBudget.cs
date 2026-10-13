using CampaignCore;

namespace CampaignCreator;

public sealed record RewardBudget(string Kind, int Selections, int Minimum, int EligibleFamilies, int StartingFamilies, int SelectedEntries, IReadOnlyList<string> Errors)
{
    public string Summary => $"{SelectedEntries} entries · {EligibleFamilies} distinct reward families available · minimum {Minimum}"
        + $"\nLongest route: {Selections} selections + 2 spare choices. {StartingFamilies} families already on starting/recruitable players or teams are not counted.";
}

public static class CampaignRewardBudget
{
    private sealed record RewardInfo(string Kind, string Id, string Family, int Level, string Asset, long Category);
    private static readonly List<RewardInfo> Info = Load();
    private static readonly string[][] NativeStarting = LoadStarting();
    private static string[][] LoadStarting()
    {
        using var stream = typeof(CampaignRewardBudget).Assembly.GetManifestResourceStream("CampaignCreator.TemplateStartingRewards.tsv")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Split('\n').Select(l => l.TrimEnd('\r').Split('\t')).Where(p => p.Length == 3).ToArray();
    }
    private static List<RewardInfo> Load()
    {
        using var stream = typeof(CampaignRewardBudget).Assembly.GetManifestResourceStream("CampaignCreator.RewardFamilies.tsv")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Split('\n').Select(l => l.TrimEnd('\r').Split('\t'))
            .Where(p => p.Length == 6 && !p[0].StartsWith('#'))
            .Select(p => new RewardInfo(p[0], p[1], p[2], int.TryParse(p[3], out int level) ? level : 1, p[4], long.TryParse(p[5], out long category) ? category : 0)).ToList();
    }
    public static bool IsChaosTalent(string token) => Info.Any(r => r.Kind == "Talents" && (r.Category & 16384) != 0
        && (r.Id.Equals(token, StringComparison.OrdinalIgnoreCase) || r.Asset.Equals(token, StringComparison.OrdinalIgnoreCase)));
    public static bool NativeChaosTalent(string token) => Info.Any(r => r.Kind == "Talents" && (r.Category & 16384) != 0 && r.Level == 1
        && (r.Id.Equals(token, StringComparison.OrdinalIgnoreCase) || r.Asset.Equals(token, StringComparison.OrdinalIgnoreCase)));
    public static string Family(string kind, string token) => Info.FirstOrDefault(r => r.Kind == RewardPoolDefinition.CatalogKind(kind) &&
        (r.Id.Equals(token, StringComparison.OrdinalIgnoreCase) || r.Asset.Equals(token, StringComparison.OrdinalIgnoreCase)))?.Family ?? token;

    public static IReadOnlyList<RewardBudget> Calculate(string root, string campaign, IReadOnlyDictionary<string, RewardPoolDefinition>? edited = null,
        IEnumerable<MapLayout>? layouts = null, string? actSequence = null, IReadOnlyDictionary<string, CampaignDocument>? changedFiles = null)
    {
        CampaignDocument ReadData(string path) => changedFiles != null && changedFiles.TryGetValue(Path.GetFullPath(path), out var change) ? change : Read(path);
        var config = ReadData(Path.Combine(campaign, "campaign.txt"));
        var savedPools = ReadData(Path.Combine(campaign, "reward_pools.txt"));
        string teamsDir = Path.Combine(campaign, "player_teams");
        var teams = Directory.Exists(teamsDir) ? Directory.GetDirectories(teamsDir).Select(d => Path.Combine(d, "team.txt")).Where(File.Exists).Select(ReadData).ToList() : [];
        if (teams.Count == 0) teams.Add(new CampaignDocument("Squad Template = " + config.Get("Squad Template", "Basic Squad")));
        var records = Directory.Exists(teamsDir) ? Directory.GetFiles(teamsDir, "*.txt", SearchOption.AllDirectories)
            .Where(p => !p.Split(Path.DirectorySeparatorChar).Any(s => s.StartsWith('.'))).Select(ReadData).ToList() : [];
        var result = new List<RewardBudget>();
        RewardPoolDefinition Pool(string kind) => edited != null && edited.TryGetValue(kind, out var p) ? p
            : RewardCatalog.ReadPool(root, savedPools, kind);
        var relics = Pool("Relics");
        bool doubles = relics.UseBaseGame || relics.Selected.Any(id => Family("Relics", id) == "Rogue.DoubleReward")
            || records.Any(d => Tokens(d.Get("Team Relics", d.Get("Relics"))).Any(id => Family("Relics", id) == "Rogue.DoubleReward"));
        foreach (string kind in RewardPoolDefinition.Kinds)
        {
            string catalogKind = RewardPoolDefinition.CatalogKind(kind);
            var pool = Pool(kind);
            var entries = RewardCatalog.ReadEntries(root, kind);
            var selected = pool.Selected.Where(entries.ContainsKey).Select(k => entries[k]).ToList();
            var families = selected.Select(e => Family(kind, e.Id.Length > 0 ? e.Id : e.Key)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var starting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var doc in records)
                foreach (string token in Tokens(doc.Get(kind == "Abilities" ? "Ability" : kind == "Relics" ? "Team Relics" : catalogKind, kind == "Relics" ? doc.Get("Relics") : "")))
                    starting.Add(Family(kind, RewardCatalog.ResolveKey(root, kind, token)));
            foreach (var team in teams)
            {
                string template = team.Get("Squad Template", config.Get("Squad Template", "Basic Squad"));
                if (kind == "Relics" && !team.Get("Starting Relics Mode").Equals("Custom", StringComparison.OrdinalIgnoreCase))
                    foreach (string token in SquadTemplateCatalog.StartingRelics(template)) starting.Add(Family(kind, token));
                foreach (var item in NativeStarting.Where(p => p[0] == SquadTemplatePolicy.Identity(template) && p[1] == catalogKind))
                    starting.Add(Family(kind, item[2]));
            }
            int blocked = families.Count(starting.Contains);
            families.ExceptWith(starting);
            int selections = 0;
            foreach (var team in teams)
            {
                string template = team.Get("Squad Template", config.Get("Squad Template", "Basic Squad"));
                bool On(string id) => SquadBehaviors.Enabled(template, id, k => team.Get(k));
                bool rewardTalentAvailable = new[] { Pool("Talents"), Pool(RewardPoolDefinition.ChaosTalents) }
                    .Any(p => !p.UseBaseGame && p.Selected.Any(t => Family("Talents", t) == "Rogue.ButtRewardTalent"));
                int forced = records.Count(d => Tokens(d.Get("Talents")).Any(t => Family("Talents", RewardCatalog.ResolveKey(root, "Talents", t)) == "Rogue.ButtRewardTalent"))
                    + NativeStarting.Count(p => p[0] == SquadTemplatePolicy.Identity(template) && p[1] == "Talents" && Family("Talents", p[2]) == "Rogue.ButtRewardTalent");
                if (rewardTalentAvailable) forced = Math.Max(forced, (On("two_lines") ? 10 : 5) + (int.TryParse(team.Get("Bench Size"), out int b) && b >= 0 ? b : 7));
                int Cost(MapLayoutNode n)
                {
                    int count = RewardRouteBudget.NodeSelections(n, kind, doubles);
                    if (n.Type is "Elite" or "Boss" or "Challenge")
                    {
                        int extra = doubles && n.Type != "Challenge" ? 1 : 0;
                        if (On("gm_rewards")) count = kind == "Relics" ? 1 + extra : kind == "Talents" ? extra : 0;
                        if (On("no_relic_rewards")) count = kind == "Talents" ? (n.Type == "Challenge" ? 1 : 2) + extra : kind == "Relics" ? extra : 0;
                        if (kind == "Talents" && count > 0) count += forced;
                    }
                    return count;
                }
                var maps = layouts?.ToList();
                var native = MapCatalog.LoadBaseMaps(Path.Combine(root, "_game_maps.txt"));
                int NativeMaximum(MapLayout layout) => native.Where(v => SquadTemplatePolicy.Identity(v.BaseSquad) == SquadTemplatePolicy.Identity(layout.BaseSquad) && v.BaseMap == layout.BaseMap)
                    .Select(v => RewardRouteBudget.Maximum(v, Cost)).DefaultIfEmpty(RewardRouteBudget.Maximum(layout, Cost)).Max();
                int route;
                if (maps != null) route = maps.Sum(m => RewardRouteBudget.Maximum(m, Cost));
                else
                {
                    var loaded = OpponentAssignments.LoadLayouts(root, campaign, template, actSequence ?? config.Get("Act Sequence", "1,2,3"));
                    route = loaded.Sum(m => File.Exists(Path.Combine(campaign, "map_layouts", $"map-{m.Map}.txt")) ? RewardRouteBudget.Maximum(m.Layout, Cost)
                        : NativeMaximum(m.Layout));
                }
                selections = Math.Max(selections, route);
            }
            int minimum = RewardRouteBudget.Minimum(selections);
            var errors = new List<string>();
            if (!pool.UseBaseGame)
            {
                if (pool.Selected.Any(id => !entries.ContainsKey(id))) errors.Add(kind + ": remove or replace unavailable selected rewards.");
                if (families.Count < minimum) errors.Add($"{kind}: need {minimum} distinct reward families beyond starting rewards for this route; {families.Count} selected. Add {minimum - families.Count} or use Base game pool.");
                bool statBonus = relics.Selected.Any(id => Family("Relics", id) == "Rogue.StatRewardRelic")
                    || records.Any(d => Tokens(d.Get("Team Relics", d.Get("Relics"))).Any(id => Family("Relics", RewardCatalog.ResolveKey(root, "Relics", id)) == "Rogue.StatRewardRelic"));
                if (kind == "Talents" && statBonus && !selected.Any(e => Info.Any(i => i.Kind == kind && i.Id == e.Id && (i.Category & 65536) != 0)))
                    errors.Add("Talents: Stat Reward Relic needs at least one selected stat talent for its additional option.");
                if ((kind == "Relics" || kind == "Talents") && teams.Any(t => SquadBehaviors.Enabled(t.Get("Squad Template", config.Get("Squad Template", "Basic Squad")), "stat_rewards", k => t.Get(k))))
                {
                    long statFlag = kind == "Relics" ? 512 : 65536;
                    int statFamilies = selected.Where(e => Info.Any(i => i.Kind == kind && i.Id == e.Id && (i.Category & statFlag) != 0))
                        .Select(e => Family(kind, e.Id)).Where(f => !starting.Contains(f)).Distinct().Count();
                    if (statFamilies < minimum) errors.Add($"{kind}: Stats reward rules need {minimum} selected stat-reward families beyond starting rewards for the same whole-route reserve; {statFamilies} selected.");
                }
            }
            result.Add(new(kind, selections, minimum, families.Count, blocked, selected.Count, errors));
        }
        return result;
    }
    private static CampaignDocument Read(string file) => new(File.Exists(file) ? File.ReadAllText(file) : "");
    private static IEnumerable<string> Tokens(string value) => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
