using CampaignCore;

namespace CampaignCreator;

internal static class CustomRewardCatalog
{
    internal static string? CampaignFor(string? file) {
        if (string.IsNullOrWhiteSpace(file)) return null;
        for (var directory = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(file))!); directory != null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "campaign.txt"))) return directory.FullName;
        return null;
    }
    internal static Dictionary<string, CustomRewardDefinition> Items(string root, string campaign) {
        var items = CustomRewardDefinition.LoadFolder(Path.Combine(root, "library", "rewards")).ToDictionary(d => d.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var d in CustomRewardDefinition.LoadFolder(Path.Combine(campaign, CustomRewardDefinition.Folder))) items[d.Id] = d;
        return items;
    }
    internal static void BundleLibrary(string root, string campaign) {
        string folder = Path.Combine(campaign, CustomRewardDefinition.Folder);
        foreach (var d in PortableItems(root, campaign).Values) if (!File.Exists(Path.Combine(folder, d.Id + CustomRewardDefinition.Extension))) d.Save(folder);
    }
    internal static Dictionary<string, CustomRewardDefinition> PortableItems(string root, string campaign) {
        var items = CustomRewardDefinition.LoadFolder(Path.Combine(root, "library", "rewards")).ToDictionary(d => d.Id, StringComparer.OrdinalIgnoreCase);
        var embedded = new Dictionary<string, CustomRewardDefinition>(StringComparer.OrdinalIgnoreCase);
        var authored = CustomRewardDefinition.LoadFolder(Path.Combine(campaign, CustomRewardDefinition.Folder)).ToDictionary(d => d.Id, StringComparer.OrdinalIgnoreCase);
        // Imported exported players/teams carry independent rule snapshots.
        // Materialize those for the campaign runtime and its shared ZIP.
        foreach (string file in Directory.EnumerateFiles(campaign, "*.txt", SearchOption.AllDirectories)
            .Where(f => !Path.GetRelativePath(campaign, f).Split(Path.DirectorySeparatorChar).SkipLast(1)
                .Any(p => p.StartsWith('_') || p.StartsWith('.') || p is "custom_reward_progress" or "map_backups")))
            foreach (var d in CustomRewardExport.Read(new CampaignDocument(File.ReadAllText(file))))
                if (!authored.ContainsKey(d.Id)) CustomRewardExport.AddSnapshot(embedded, d);
        foreach (var d in embedded.Values) items[d.Id] = d;
        foreach (var d in authored.Values) items[d.Id] = d;
        return items;
    }
    internal static void ValidateCampaignLoadouts(string root, string campaign, IReadOnlyDictionary<string, CustomRewardDefinition> items) {
        if (items.Count == 0) return;
        var game = new GameData(root);
        game.CustomRewards.Clear();
        foreach (var item in items.Values) {
            game.CustomRewards[item.Id] = item;
            var aliases = item.IsRelic ? game.Relics : game.Talents;
            aliases[item.Id] = item.Id; aliases.TryAdd(item.Name, item.Id);
        }
        foreach (string file in Directory.EnumerateFiles(campaign, "*.txt", SearchOption.AllDirectories)
            .Where(f => !Path.GetRelativePath(campaign, f).Split(Path.DirectorySeparatorChar).SkipLast(1)
                .Any(p => p.StartsWith('_') || p.StartsWith('.') || p is "custom_reward_progress" or "map_backups"))) {
            var document = new CampaignDocument(File.ReadAllText(file));
            bool? goalie = document.Get("Name").Length == 0 || Path.GetFileName(file).Equals("team.txt", StringComparison.OrdinalIgnoreCase) ? null
                : Path.GetFileName(file).Contains("goalie", StringComparison.OrdinalIgnoreCase) || document.Get("Position").Equals("Goalie", StringComparison.OrdinalIgnoreCase)
                    || document.Fields.ContainsKey("Catching") || document.Fields.ContainsKey("Goalie Skill");
            try { game.ValidateCustomLoadout(document, goalie); }
            catch (ArgumentException ex) { throw new ArgumentException(Path.GetRelativePath(campaign, file) + ": " + ex.Message, ex); }
        }
    }
    internal static void PreserveLoadout(GameData game, CampaignDocument document, string file, bool? goalie = null) {
        game.ValidateCustomLoadout(document, goalie);
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string key in new[] { "Talents", "Goalie Talents", "Team Talents", "Relics", "Team Relics" })
            foreach (string value in document.Get(key).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
                string id = value;
                var aliases = key.Contains("Relics") ? game.Relics : game.Talents;
                if (aliases.TryGetValue(value, out var resolved)) id = resolved;
                if (game.CustomRewards.ContainsKey(id)) ids.Add(id);
            }
        var referenced = ids.Select(id => game.CustomRewards[id]).ToArray();
        CustomRewardExport.Embed(document, referenced);
        string? campaign = CampaignFor(file);
        if (campaign != null) {
            string folder = Path.Combine(campaign, CustomRewardDefinition.Folder);
            foreach (var d in referenced) if (!File.Exists(Path.Combine(folder, d.Id + CustomRewardDefinition.Extension))) d.Save(folder);
        }
    }
    internal static Dictionary<string, RewardCatalog.Entry> Entries(string root, string campaign, string kind)
    {
        var entries = RewardCatalog.ReadEntries(root, kind);
        string catalog = RewardPoolDefinition.CatalogKind(kind);
        foreach (var d in Items(root, campaign).Values.Where(d => d.CatalogKind == catalog))
            entries[d.Id] = new RewardCatalog.Entry(d.Id, d.Name, d.FullDescription, 1, d.Id, d.Kind);
        return entries;
    }
    internal static bool Belongs(string kind, string id, IReadOnlyDictionary<string, CustomRewardDefinition> custom) =>
        custom.TryGetValue(id, out var d) ? CustomRewardPoolSelection.ForNative(kind) == d.Kind : RewardCatalog.BelongsToPool(kind, id);
}
