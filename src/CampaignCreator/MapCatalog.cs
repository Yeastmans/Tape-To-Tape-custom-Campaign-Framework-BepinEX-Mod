using System.Globalization;
using System.Text.RegularExpressions;
using CampaignCore;

namespace CampaignCreator;

public static class MapCatalog
{
    public static MapLayout TemplateMap(IEnumerable<MapLayout> catalog, string template, int act)
    {
        string id = SquadTemplatePolicy.RuntimeIdentity(template);
        if (id.Length == 0) id = "vanilla";
        var source = catalog.Where(m => SquadTemplatePolicy.Identity(m.BaseSquad) == id && m.BaseMap == act - 1)
            .OrderBy(m => m.Template).FirstOrDefault()
            ?? catalog.Where(m => SquadTemplatePolicy.Identity(m.BaseSquad) == "vanilla" && m.BaseMap == act - 1)
                .OrderBy(m => m.Template).First();
        var copy = MapLayout.Parse(source.Serialize());
        copy.LookAct = act;
        return copy;
    }
    public static List<MapLayout> LoadBaseMaps(string file)
    {
        using var stream = typeof(MapCatalog).Assembly.GetManifestResourceStream("CampaignCreator.BaseGameMaps.txt")!;
        using var reader = new StreamReader(stream);
        var bundled = Parse(reader.ReadToEnd().Split('\n'));
        var native = bundled.Select(m => SquadTemplatePolicy.RuntimeIdentity(m.BaseSquad)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var current = LoadSourceMaps(file).Where(m => native.Contains(SquadTemplatePolicy.RuntimeIdentity(m.BaseSquad)) && m.Nodes.Count > 0).ToList();
        var available = current.Select(m => (SquadTemplatePolicy.RuntimeIdentity(m.BaseSquad), m.BaseMap)).ToHashSet();
        current.AddRange(bundled.Where(m => !available.Contains((SquadTemplatePolicy.RuntimeIdentity(m.BaseSquad), m.BaseMap))));
        return current;
    }
    // Read game artwork refreshes this snapshot from the installed assets. An
    // older runtime dump must not replace its graphs or their native pools.
    // Keep the dump as a compatibility fallback when no asset snapshot exists.
    public static List<MapLayout> LoadSourceMaps(string file)
    {
        var assets = StudioArt.Path("base-maps", "_game_maps.txt") is { } nativeFile ? Load(nativeFile) : new List<MapLayout>();
        return assets.Count > 0 ? assets : Load(file);
    }
    public static void HydrateNativePools(MapLayout layout, IEnumerable<MapLayout> sources)
    {
        var source = FindSource(layout, sources);
        if (source == null) return;
        foreach (var node in layout.Nodes.Where(n => n.Type == "NULL" && n.NativeRandomWeights == null))
        {
            var native = source.Nodes.FirstOrDefault(n => n.Type == "NULL" && n.SourceLayer == node.SourceLayer && n.SourceNode == node.SourceNode);
            if (native?.NativeRandomWeights != null) node.NativeRandomWeights = new(native.NativeRandomWeights);
        }
    }
    public static MapLayout? FindSource(MapLayout layout, IEnumerable<MapLayout> catalog)
    {
        var choices = catalog.ToArray();
        string? name = MapLayout.ResolveBaseSquad(layout.BaseSquad, choices.Select(m => m.BaseSquad));
        return name == null ? null : choices.FirstOrDefault(m => m.BaseSquad.Equals(name, StringComparison.OrdinalIgnoreCase)
            && m.BaseMap == layout.BaseMap && m.Template == layout.Template);
    }
    public static List<string> ValidateSource(MapLayout layout, IEnumerable<MapLayout> catalog)
    {
        var choices = catalog.ToArray();
        string? name = MapLayout.ResolveBaseSquad(layout.BaseSquad, choices.Select(m => m.BaseSquad));
        if (name == null) return [$"Base squad map is unavailable or ambiguous: {layout.BaseSquad}. Import a current base-game map."];
        if (!choices.Any(m => m.BaseSquad.Equals(name, StringComparison.OrdinalIgnoreCase) && m.BaseMap == layout.BaseMap))
            return [$"Base map {layout.BaseMap + 1} is unavailable for {name}. Import a current base-game map."];
        if (FindSource(layout, choices) == null)
            return [$"Base map variant {layout.Template + 1} is unavailable for {name}. Import a current base-game map."];
        return [];
    }
    public static List<MapLayout> Load(string file)
        => File.Exists(file) ? Parse(File.ReadLines(file)) : new();
    private static List<MapLayout> Parse(IEnumerable<string> lines)
    {
        var maps = new List<MapLayout>();
        string squad = ""; int mapIndex = -1, layer = -1;
        MapLayout? map = null;
        var links = new Dictionary<string, int[]>();
        foreach (var raw in lines)
        {
            string line = raw.Trim(); Match m;
            if ((m = Regex.Match(line, "^SQUAD '(.+)' maps=")).Success) { squad = m.Groups[1].Value; map = null; }
            else if ((m = Regex.Match(line, @"^MAP index=(\d+)")).Success) { mapIndex = int.Parse(m.Groups[1].Value); map = null; }
            else if ((m = Regex.Match(line, @"^TEMPLATE (\d+)")).Success)
            { map = new MapLayout { BaseSquad = squad, BaseMap = mapIndex, Template = int.Parse(m.Groups[1].Value) }; maps.Add(map); }
            else if ((m = Regex.Match(line, @"^LAYER (\d+)")).Success) layer = int.Parse(m.Groups[1].Value);
            else if (map != null && (m = Regex.Match(line, @"^NODE n=(\d+) type=(\S+) pos=([-\d.]+),([-\d.]+) out=\[([^\]]*)\] group='([^']*)' team='([^']*)' gm=(\d+) talents=(\d+) relics=(\d+)")).Success)
            {
                var n = new MapLayoutNode { Layer = layer, SourceLayer = layer, SourceNode = int.Parse(m.Groups[1].Value), Type = m.Groups[2].Value,
                    X = double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture), Y = double.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture),
                    EliteGroup = m.Groups[6].Value, EliteTeam = m.Groups[7].Value, GmChoices = Math.Max(1, int.Parse(m.Groups[8].Value)), TalentRewards = int.Parse(m.Groups[9].Value), RelicRewards = int.Parse(m.Groups[10].Value) };
                // Native RewardCounter ignores these serialized fields for
                // Boss/Challenge: their base reward is one talent, no relic.
                // Import the effective base counts so an unchanged copy does
                // not add rewards. Native squad rules and bonuses stay dynamic.
                if (n.Type is "Boss" or "Challenge") { n.TalentRewards = 1; n.RelicRewards = 0; }
                var weights = Regex.Match(line, @"\sweights=\[([^\]]*)\]");
                if (n.Type == "NULL" && weights.Success)
                {
                    n.NativeRandomWeights = new();
                    foreach (string entry in weights.Groups[1].Value.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var parts = entry.Split(':');
                        if (parts.Length != 2 || !decimal.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out decimal weight) || weight < 0)
                            throw new InvalidDataException("Invalid native random pool: " + line);
                        n.NativeRandomWeights.Add(parts[0], weight);
                    }
                }
                links[n.Id] = m.Groups[5].Value.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray(); map.Nodes.Add(n);
            }
        }
        foreach (var layout in maps) foreach (var n in layout.Nodes)
            // Some native variants retain outgoing indices for branches absent from that variant.
            n.Outgoing = links[n.Id].Select(i => layout.Nodes.FirstOrDefault(t => t.Layer == n.Layer + 1 && t.SourceNode == i)?.Id)
                .OfType<string>().Distinct().ToList();
        return maps;
    }
    public static string ResolveSquad(string value)
    {
        return value.Trim().ToLowerInvariant() switch {
            "basic" => "Basic Squad", "defence" => "Defense Squad", "speedy" => "Speed", "trio" => "Trio Meta", "gauntlet" => "Gauntlet Meta",
            "solo" => "Solo Meta", "bum" => "Bum Squad", "slapshot" => "Slapshot Squad", "referee" => "Referee Squad", "speedrun" => "Speedrun Squad", "violence" => "Violence Squad", "oldschool" => "Old School", _ => value };
    }
}
