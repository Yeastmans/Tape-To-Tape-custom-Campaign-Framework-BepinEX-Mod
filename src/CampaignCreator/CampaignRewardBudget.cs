using CampaignCore;

namespace CampaignCreator;

public sealed record RewardBudget(string Kind, int Selections, int Minimum, int EligibleFamilies, int StartingFamilies, int SelectedEntries, IReadOnlyList<string> Errors)
{
    public string Summary => $"{SelectedEntries} selected entries · {EligibleFamilies} different {CampaignRewardBudget.RewardName(Kind)} (upgrade levels count together)."
        + (Errors.Count > 0 ? "\n" + string.Join("\n", Errors) : "");
}

public static class CampaignRewardBudget
{
    public static int MinimumChoices(string kind) => kind switch { "Abilities" => 1, RewardPoolDefinition.ChaosTalents => 2, _ => 3 };
    public static string RewardName(string kind) => kind switch { "Relics" => "relics", RewardPoolDefinition.ChaosRelics => "Chaos relics", "Abilities" => "abilities", "Goalie Talents" => "goalie talents", RewardPoolDefinition.ChaosTalents => "Chaos talents", _ => "talents" };
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
    public static bool IsChaosRelic(string token) => Info.Any(r => r.Kind == "Relics" && (r.Category & 128) != 0
        && (r.Id.Equals(token, StringComparison.OrdinalIgnoreCase) || r.Asset.Equals(token, StringComparison.OrdinalIgnoreCase)));
    public static bool NativeChaosTalent(string token) => Info.Any(r => r.Kind == "Talents" && (r.Category & 16384) != 0 && r.Level == 1
        && (r.Id.Equals(token, StringComparison.OrdinalIgnoreCase) || r.Asset.Equals(token, StringComparison.OrdinalIgnoreCase)));
    public static string Family(string kind, string token) => Info.FirstOrDefault(r => r.Kind == RewardPoolDefinition.CatalogKind(kind) &&
        (r.Id.Equals(token, StringComparison.OrdinalIgnoreCase) || r.Asset.Equals(token, StringComparison.OrdinalIgnoreCase)))?.Family ?? token;

    public static IReadOnlyList<RewardBudget> Calculate(string root, string campaign, IReadOnlyDictionary<string, RewardPoolDefinition>? edited = null,
        IEnumerable<MapLayout>? layouts = null, string? actSequence = null, IReadOnlyDictionary<string, CampaignDocument>? changedFiles = null,
        string? mapSquadDirectory = null, IReadOnlyList<int>? originalStages = null, IReadOnlyList<bool>? nativeInheritedMaps = null,
        string? validationSquadDirectory = null)
    {
        CampaignDocument ReadData(string path) => changedFiles != null && changedFiles.TryGetValue(Path.GetFullPath(path), out var change) ? change : Read(path);
        var config = ReadData(Path.Combine(campaign, "campaign.txt"));
        var savedPools = ReadData(Path.Combine(campaign, "reward_pools.txt"));
        string teamSetting = config.Get("Use Player Teams", config.Get("Custom Player Teams", "yes"));
        bool useTeams = teamSetting.Equals("yes", StringComparison.OrdinalIgnoreCase) || teamSetting.Equals("true", StringComparison.OrdinalIgnoreCase);
        // Campaign edits validate every selectable squad. A single-team edit
        // validates that team so unrelated broken maps cannot prevent repair.
        // Disabled squads remain on disk for portability, not active validation.
        var teamDirectories = (useTeams ? SquadMapOverrides.Squads(campaign) : Array.Empty<string>())
            .Where(d => validationSquadDirectory == null || Path.GetFullPath(d).Equals(Path.GetFullPath(validationSquadDirectory), StringComparison.OrdinalIgnoreCase)).ToArray();
        var teamPaths = teamDirectories.ToDictionary(d => ReadData(Path.Combine(d, "team.txt")), d => d);
        var teams = teamPaths.Keys.ToList();
        if (teams.Count == 0) teams.Add(new CampaignDocument("Squad Template = " + config.Get("Squad Template", "Basic Squad")));
        var records = teamDirectories.SelectMany(d => Directory.GetFiles(d, "*.txt", SearchOption.AllDirectories))
            .Where(p => !p.Split(Path.DirectorySeparatorChar).Any(s => s.StartsWith('.'))).Select(ReadData).ToList();
        var result = new List<RewardBudget>();
        var customRewards = CustomRewardCatalog.Items(root, campaign);
        // These snapshots live only for this validation. Reuse them across
        // recipients and reward kinds, then reread on the next edit/save.
        var pools = new Dictionary<string, RewardPoolDefinition>();
        var catalogs = new Dictionary<string, Dictionary<string, RewardCatalog.Entry>>();
        string Resolve(string kind, string token)
        {
            string catalogKind = RewardPoolDefinition.CatalogKind(kind);
            if (!catalogs.TryGetValue(catalogKind, out var catalog))
                catalogs[catalogKind] = catalog = RewardCatalog.ReadEntries(root, catalogKind);
            return RewardCatalog.ResolveKey(catalog, token);
        }
        var maps = layouts?.ToList();
        List<MapLayout>? native = null;
        var teamLayouts = teams.ToDictionary(t => t, t => SquadMapCatalog.Resolve(root, campaign, t, teamPaths.GetValueOrDefault(t),
            maps, mapSquadDirectory, originalStages, nativeInheritedMaps, actSequence ?? config.Get("Act Sequence", CampaignActSequence.Default), config.Get("Squad Template", "Basic Squad")));
        var mapErrors = new List<string>();
        var sources = new Lazy<List<MapLayout>>(() => {
            var current = MapCatalog.LoadSourceMaps(Path.Combine(root, "_game_maps.txt"));
            return current.Count > 0 ? current : MapCatalog.LoadBaseMaps(Path.Combine(root, "_game_maps.txt"));
        });
        var opponentKeys = CampaignNavigation.Load(campaign).Where(n => n.Context.StartsWith("Opponent ") && n.FilePath != null)
            .Select(n => Path.GetFileName(Path.GetDirectoryName(n.FilePath))!).ToArray();
        foreach (var pair in teamLayouts)
            foreach (var resolved in pair.Value.Where(m => m.SquadOverride))
            {
                var errors = resolved.Layout.Validate(opponentKeys).Concat(MapPlacementBounds.Validate(resolved.Layout))
                    .Concat(MapCatalog.ValidateSource(resolved.Layout, sources.Value))
                    .Concat(CustomCoachCatalog.Errors(root, campaign, resolved.Layout))
                    .Concat(GoalieFeatureOptions.ValidateNodes(GoalieFeatureOptions.Recruitment(config), new[] { resolved.Layout }, resolved.Stage));
                mapErrors.AddRange(errors.Select(e => pair.Key.Get("Team Name", "Squad") + $", map {resolved.Stage}: " + e));
            }
        RewardPoolDefinition Pool(string kind) {
            if (pools.TryGetValue(kind, out var cached)) return cached;
            var native = edited != null && edited.TryGetValue(kind, out var p) ? p : RewardCatalog.ReadPool(root, savedPools, kind);
            string customKind = CustomRewardPoolSelection.ForNative(kind);
            if (customKind.Length == 0) return pools[kind] = native;
            var custom = edited != null && edited.TryGetValue(customKind, out var c) ? c : CustomRewardPoolSelection.Read(savedPools, customKind, customRewards.Values);
            var defaults = RewardCatalog.ReadPool(root, new CampaignDocument(""), kind).Selected;
            return pools[kind] = CustomRewardPoolSelection.Effective(kind, native, custom, defaults, customRewards.Keys);
        }
        var relics = Pool("Relics");
        bool doubles = relics.UseBaseGame || relics.Selected.Any(id => Family("Relics", id) == "Rogue.DoubleReward")
            || records.Any(d => Tokens(d.Get("Team Relics", d.Get("Relics"))).Any(id => Family("Relics", id) == "Rogue.DoubleReward"));
        foreach (string kind in RewardPoolDefinition.Kinds)
        {
            string catalogKind = RewardPoolDefinition.CatalogKind(kind);
            var pool = Pool(kind);
            var entries = CustomRewardCatalog.Entries(root, campaign, kind);
            var selected = pool.Selected.Where(entries.ContainsKey).Select(k => entries[k]).ToList();
            var families = selected.Select(e => Family(kind, e.Id.Length > 0 ? e.Id : e.Key)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var starting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var doc in records)
                foreach (string token in Tokens(doc.Get(kind == "Abilities" ? "Ability" : kind == "Relics" ? "Team Relics" : catalogKind, kind == "Relics" ? doc.Get("Relics") : "")))
                    starting.Add(Family(kind, Resolve(kind, token)));
            foreach (var team in teams)
            {
                string template = team.Get("Squad Template", config.Get("Squad Template", "Basic Squad"));
                if (kind == "Relics" && !team.Get("Starting Relics Mode").Equals("Custom", StringComparison.OrdinalIgnoreCase))
                    foreach (string token in SquadTemplateCatalog.StartingRelics(template)) starting.Add(Family(kind, token));
                foreach (var item in NativeStarting.Where(p => p[0] == SquadTemplatePolicy.Identity(template) && p[1] == catalogKind))
                    starting.Add(Family(kind, item[2]));
            }
            int blocked = families.Count(starting.Contains);
            // Ownership is checked for the actual recipient at runtime. Rewards on
            // another starter, optional recruit or alternative squad do not remove
            // that type from the campaign's pool for every player.
            int selections = 0;
            foreach (var team in teams)
            {
                string template = team.Get("Squad Template", config.Get("Squad Template", "Basic Squad"));
                bool On(string id) => SquadBehaviors.Enabled(template, id, k => team.Get(k));
                bool rewardTalentAvailable = new[] { Pool("Talents"), Pool(RewardPoolDefinition.ChaosTalents) }
                    .Any(p => !p.UseBaseGame && p.Selected.Any(t => Family("Talents", t) == "Rogue.ButtRewardTalent"));
                int forced = records.Count(d => Tokens(d.Get("Talents")).Any(t => Family("Talents", Resolve("Talents", t)) == "Rogue.ButtRewardTalent"))
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
                int NativeMaximum(MapLayout layout) => (native ??= MapCatalog.LoadBaseMaps(Path.Combine(root, "_game_maps.txt")))
                    .Where(v => SquadTemplatePolicy.Identity(v.BaseSquad) == SquadTemplatePolicy.Identity(layout.BaseSquad) && v.BaseMap == layout.BaseMap)
                    .Select(v => RewardRouteBudget.Maximum(v, Cost)).DefaultIfEmpty(RewardRouteBudget.Maximum(layout, Cost)).Max();
                int route = teamLayouts[team].Sum(m => m.Native ? NativeMaximum(m.Layout) : RewardRouteBudget.Maximum(m.Layout, Cost));
                selections = Math.Max(selections, route);
            }
            // A pool is reused by successive offers. Route length and bonus reward
            // selections cannot require a new unique reward for the entire run.
            int minimum = MinimumChoices(kind);
            var errors = new List<string>();
            if (result.Count == 0) errors.AddRange(mapErrors.Distinct());
            if (!pool.UseBaseGame)
            {
                if (pool.Selected.Any(id => !entries.ContainsKey(id))) errors.Add(kind + ": remove or replace unavailable selected rewards.");
                if (families.Count < minimum) errors.Add($"Select at least {minimum} different {RewardName(kind)} for a normal offer, or use Base game pool.");
                bool statBonus = relics.Selected.Any(id => Family("Relics", id) == "Rogue.StatRewardRelic")
                    || records.Any(d => Tokens(d.Get("Team Relics", d.Get("Relics"))).Any(id => Family("Relics", Resolve("Relics", id)) == "Rogue.StatRewardRelic"));
                if (kind == "Talents" && statBonus && !selected.Any(e => customRewards.ContainsKey(e.Id) || Info.Any(i => i.Kind == kind && i.Id == e.Id && (i.Category & 65536) != 0)))
                    errors.Add("Talents: Stat Reward Relic needs at least one selected stat talent for its additional option.");
                if ((kind == "Relics" || kind == "Talents") && teams.Any(t => SquadBehaviors.Enabled(t.Get("Squad Template", config.Get("Squad Template", "Basic Squad")), "stat_rewards", k => t.Get(k))))
                {
                    long statFlag = kind == "Relics" ? 512 : 65536;
                    int statFamilies = selected.Where(e => customRewards.ContainsKey(e.Id) || Info.Any(i => i.Kind == kind && i.Id == e.Id && (i.Category & statFlag) != 0))
                        .Select(e => Family(kind, e.Id)).Distinct().Count();
                    if (statFamilies < minimum) errors.Add($"{kind}: Stats reward rules need at least {minimum} different selected stat rewards for a normal offer; {statFamilies} selected.");
                }
            }
            result.Add(new(kind, selections, minimum, families.Count, blocked, selected.Count, errors));
        }
        return result;
    }
    private static CampaignDocument Read(string file) => new(File.Exists(file) ? File.ReadAllText(file) : "");
    private static IEnumerable<string> Tokens(string value) => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
