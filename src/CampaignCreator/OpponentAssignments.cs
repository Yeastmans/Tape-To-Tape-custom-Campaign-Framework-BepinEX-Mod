using CampaignCore;
using System.Text.RegularExpressions;

namespace CampaignCreator;

/// <summary>A node that takes an opponent (Elite/Boss). Identified by position, since template maps get fresh node ids on every load.</summary>
public sealed record MatchNode(int Map, int Act, int Layer, int Native, bool Boss, string Team)
{
    public string Label => $"Map {Map} · Layer {Layer + 1} · Node {Native + 1}" + (Boss ? " (Boss)" : "");
}

public static class OpponentAssignments
{
    public static List<MatchNode> MatchNodes(IEnumerable<(int Map, int Act, MapLayout Layout)> layouts) =>
        layouts.OrderBy(p => p.Map).SelectMany(p => p.Layout.Nodes.Where(n => n.Type is "Elite" or "Boss")
            .Select(n => new MatchNode(p.Map, p.Act, n.Layer, p.Layout.NativeIndex(n), n.Type == "Boss", n.Team))
            .OrderBy(n => n.Layer).ThenBy(n => n.Native)).ToList();
    private static List<MapLayout>? baseMaps;
    /// <summary>The campaign's maps as the map editor would open them, read without creating an editor (which can mark maps dirty).</summary>
    public static List<(int Map, int Act, MapLayout Layout)> LoadLayouts(string contentRoot, string campaign, string template, string actSequence)
    {
        var result = new List<(int, int, MapLayout)>();
        var sequence = actSequence.Split(',').Select(s => int.TryParse(s.Trim(), out int n) ? Math.Clamp(n, 1, 3) : 1).ToArray();
        for (int i = 0; i < sequence.Length; i++)
        {
            string file = Path.Combine(campaign, "map_layouts", $"map-{i + 1}.txt");
            MapLayout layout;
            if (File.Exists(file)) layout = MapLayout.Parse(File.ReadAllText(file));
            else layout = MapCatalog.TemplateMap(baseMaps ??= MapCatalog.LoadBaseMaps(Path.Combine(contentRoot, "_game_maps.txt")), template, sequence[i]);
            MapNodePlacement.ArrangeLayers(layout);
            result.Add((i + 1, sequence[i], layout));
        }
        return result;
    }
    public static Dictionary<string, List<string>> FromLayouts(IEnumerable<(int Map, MapLayout Layout)> layouts)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (number, layout) in layouts.OrderBy(p => p.Map))
            foreach (var node in layout.Nodes.Where(n => n.Team.Length > 0 && n.Type is "Elite" or "Boss").OrderBy(n => n.Layer))
            {
                if (!result.TryGetValue(node.Team, out var nodes)) result[node.Team] = nodes = new();
                nodes.Add($"Map {number} · Layer {node.Layer + 1} · Node {layout.NativeIndex(node) + 1}" + (node.Type == "Boss" ? " (Boss)" : ""));
            }
        return result;
    }
    public static Dictionary<string, List<string>> Load(string campaign)
    {
        var layouts = new List<(int Map, MapLayout Layout)>();
        string folder = Path.Combine(campaign, "map_layouts");
        if (Directory.Exists(folder)) foreach (string file in Directory.GetFiles(folder, "map-*.txt"))
            if (int.TryParse(Path.GetFileNameWithoutExtension(file)[4..], out int number))
                layouts.Add((number, MapLayout.Parse(File.ReadAllText(file))));
        var result = FromLayouts(layouts);
        string legacy = Path.Combine(campaign, "assignments.txt");
        if (File.Exists(legacy)) foreach (string line in File.ReadLines(legacy))
        {
            var m = Regex.Match(line, @"^\s*Map\s+(\d+)\s*/\s*Layer\s+(\d+)\s*/\s*Node\s+(\d+)\s*=\s*(.+?)\s*$", RegexOptions.IgnoreCase);
            if (!m.Success || layouts.Any(l => l.Map == int.Parse(m.Groups[1].Value))) continue;
            string key = m.Groups[4].Value;
            if (!result.TryGetValue(key, out var nodes)) result[key] = nodes = new();
            nodes.Add($"Map {m.Groups[1].Value} · Layer {int.Parse(m.Groups[2].Value) + 1} · Node {int.Parse(m.Groups[3].Value) + 1}");
        }
        return result;
    }
}
