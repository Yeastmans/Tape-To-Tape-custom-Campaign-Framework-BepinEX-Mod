using CampaignCore;

namespace CampaignCreator;

internal static class CustomCoachCatalog
{
    internal static string Library(string root) => Path.Combine(root, "library", "nodes");
    internal static IReadOnlyList<CustomCoachDefinition> Load(string root, out string errors)
    {
        var items = new List<CustomCoachDefinition>(); var failures = new List<string>();
        string directory = Library(root);
        if (Directory.Exists(directory)) foreach (string file in Directory.GetFiles(directory, "*" + CustomCoachDefinition.Extension))
        {
            try { items.Add(CustomCoachDefinition.Parse(File.ReadAllText(file))); }
            catch (Exception ex) { failures.Add(Path.GetFileName(file) + ": " + ex.Message); }
        }
        errors = string.Join("\n", failures);
        return items.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }
    internal static Dictionary<string, RewardCatalog.Entry> Entries(string root, string campaign, CustomCoachDefinition? snapshot = null)
    {
        var entries = RewardCatalog.ReadEntries(root, "Relics");
        var custom = CustomRewardCatalog.Items(root, campaign);
        if (snapshot != null) foreach (var reward in snapshot.Rewards) custom[reward.Id] = reward;
        foreach (var item in custom.Values)
        {
            entries.Remove(item.Id);
            if (item.IsRelic && !item.NeedsRedesign)
                entries[item.Id] = new RewardCatalog.Entry(item.Id, item.Name, item.FullDescription, 1, item.Id, item.Kind);
        }
        return entries;
    }
    internal static void Validate(string root, string campaign, CustomCoachDefinition coach)
    {
        CustomCoachPool.ValidateAvailable(coach, Entries(root, campaign, coach).Keys);
        // Validate each owner before campaign precedence can hide a library
        // collision (including a matching companion carried by an imported ZIP).
        foreach (string folder in new[] { Path.Combine(root, "library", "rewards"), Path.Combine(campaign, CustomRewardDefinition.Folder) })
            CustomCoachPool.AddDependencies(CustomRewardDefinition.LoadFolder(folder).ToDictionary(d => d.Id, StringComparer.OrdinalIgnoreCase), coach);
        if (coach.ImagePng.Length > 0)
        {
            using var stream = new MemoryStream(Convert.FromBase64String(coach.ImagePng));
            using var image = new Avalonia.Media.Imaging.Bitmap(stream);
        }
    }
    internal static Dictionary<string, CustomRewardDefinition> Dependencies(string root, string campaign, IEnumerable<MapLayout> layouts)
    {
        var existing = CustomRewardCatalog.Items(root, campaign);
        var result = new Dictionary<string, CustomRewardDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var coach in layouts.SelectMany(m => m.Nodes).Where(n => n.Type == "Coach" && n.CustomCoach != null).Select(n => n.CustomCoach))
        {
            Validate(root, campaign, coach);
            CustomCoachPool.AddDependencies(existing, coach);
            CustomCoachPool.AddDependencies(result, coach);
        }
        return result;
    }
    internal static IEnumerable<string> Errors(string root, string campaign, MapLayout layout)
    {
        try { Dependencies(root, campaign, new[] { layout }); return Array.Empty<string>(); }
        catch (Exception ex) { return new[] { ex.Message }; }
    }
    internal static void SaveDependencies(string root, string campaign, IEnumerable<MapLayout> layouts)
    {
        var dependencies = Dependencies(root, campaign, layouts);
        string folder = Path.Combine(campaign, CustomRewardDefinition.Folder);
        foreach (var item in dependencies.Values)
            if (!File.Exists(Path.Combine(folder, item.Id + CustomRewardDefinition.Extension))) item.Save(folder);
    }
    internal static IEnumerable<CustomCoachDefinition> Saved(string campaign)
    {
        string folder = Path.Combine(campaign, "map_layouts");
        var files = (Directory.Exists(folder) ? Directory.GetFiles(folder, "map-*.txt") : Array.Empty<string>())
            .Concat(SquadMapOverrides.Squads(campaign).SelectMany(SquadMapOverrides.Files));
        foreach (string file in files)
            foreach (var node in MapLayout.Parse(File.ReadAllText(file)).Nodes)
                if (node.Type == "Coach" && node.CustomCoach != null) yield return node.CustomCoach;
    }
}
