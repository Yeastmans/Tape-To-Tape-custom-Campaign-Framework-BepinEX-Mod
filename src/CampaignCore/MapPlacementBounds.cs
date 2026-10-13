using System;
using System.Collections.Generic;
using System.Linq;

namespace CampaignCore;

/// <summary>Authored placement inside the native two-row vertical envelope.</summary>
public static class MapPlacementBounds
{
    // Preserve the native catalog's vertical range. Wider row spans can expose
    // scenery outside the native artwork and require a separately verified renderer.
    public const double MinY = 6.5, NativeMaxY = 14.2, MaxY = NativeMaxY;
    // Native catalog: 479 layers have one or two nodes. Its closest paired
    // markers are 5.9 grid units apart. Keep that separation for both rows.
    // This is an authoring policy. Runtime MapLayout still reads legacy graphs.
    public const int MaxNodesPerLayer = 2;
    public const double MinSeparation = 5.9, PairTopY = 7, PairBottomY = 13.5;
    public static bool Contains(double y) => !double.IsNaN(y) && !double.IsInfinity(y)
        && y >= MinY - .000001 && y <= MaxY + .000001;
    public static double Clamp(double y) => double.IsNaN(y) || double.IsInfinity(y)
        ? (MinY + MaxY) / 2 : Math.Clamp(y, MinY, MaxY);
    public static List<string> CapacityErrors(MapLayout map) => map.Nodes.GroupBy(n => n.Layer)
        .Where(g => g.Count() > MaxNodesPerLayer)
        .Select(g => $"Layer {g.Key + 1} has {g.Count()} nodes. Keep at most {MaxNodesPerLayer} per layer: add layers, recreate extra nodes there and reconnect their paths, or remove extras. Fit never drops nodes or splits routes.")
        .ToList();
    public static List<string> Validate(MapLayout map)
    {
        var errors = CapacityErrors(map);
        errors.AddRange(map.Nodes.Where(n => !Contains(n.Y))
            .Select(n => $"Layer {n.Layer + 1}, node {map.NativeIndex(n) + 1}: outside the visible map area. Use Fit node placement before saving."));
        errors.AddRange(map.Nodes.GroupBy(n => n.Layer).Where(g => g.Count() >= 2
            && g.All(n => Contains(n.Y)) && HasCrowdedRows(g))
            .Select(g => $"Layer {g.Key + 1}: nodes are too close for native map artwork. Use Fit node placement to separate them before saving."));
        return errors;
    }

    public static bool HasCrowdedRows(IEnumerable<MapLayoutNode> nodes)
    {
        var heights = nodes.Select(n => n.Y).OrderBy(y => y).ToArray();
        return heights.Zip(heights.Skip(1), (a, b) => b - a).Any(gap => gap < MinSeparation - .000001);
    }

    public static double RowY(int count, int index) => count switch
    {
        1 => (MinY + NativeMaxY) / 2,
        2 => index == 0 ? PairTopY : PairBottomY,
        _ => throw new ArgumentOutOfRangeException(nameof(count))
    };

    // GraphMapAdapter centres the complete graph and inverts native Y. Screen
    // coordinates therefore increase with authored Y, not with world-space Y.
    // .45 world units/grid unit and a 4.5 orthographic half-height yield 25px
    // per grid unit in Studio's 500px native-map preview.
    public const double PreviewPixelsPerGridUnit = 25;
    public static double PreviewY(MapLayout map, MapLayoutNode node, double centre = 260)
    {
        var finite = map.Nodes.Where(n => !double.IsNaN(n.Y) && !double.IsInfinity(n.Y)).ToArray();
        double middle = finite.Length == 0 ? (MinY + MaxY) / 2 : (finite.Min(n => n.Y) + finite.Max(n => n.Y)) / 2;
        return centre + (node.Y - middle) * PreviewPixelsPerGridUnit;
    }
}
