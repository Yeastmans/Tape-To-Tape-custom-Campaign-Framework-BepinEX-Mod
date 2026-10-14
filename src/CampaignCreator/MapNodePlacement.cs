using Avalonia;
using CampaignCore;

namespace CampaignCreator;

/// <summary>Placement uses the same unscaled footprint as the canvas tiles.</summary>
public static class MapNodePlacement
{
    public const double Width = 228, Height = 142, Gap = 6, Edge = 35;
    public const double LayerPitch = 522, LaneWidth = 440, MinY = 50;
    // Canvas spacing maps to the native vertical envelope. Paired nodes retain
    // the native minimum separation so large stadium artwork can remain legible.
    // Keep the original canvas/native scale so opening an existing two-row map
    // does not move its tiles. Beta 1.0 authoring uses the native two-row envelope.
    public const double Units = 450 / (MapPlacementBounds.NativeMaxY - MapPlacementBounds.MinY);
    public const double MaxY = MinY + (MapPlacementBounds.MaxY - MapPlacementBounds.MinY) * Units;
    public const double VerticalSpacing = MapPlacementBounds.MinSeparation * Units;
    // Horizontal nudge inside a layer, in native grid units. Layers keep the game's fixed pitch.
    public const double MaxXOffset = 5;
    public static double MaxXOffsetPixels => MaxXOffset * HorizontalPixelsPerGridUnit;
    // The game templates start near X=3.1 and advance about 6.4 grid units per layer.
    // Canvas pixels must not be written back as native grid distance.
    public const double NativeStartX = MapLayout.NativeStartX, NativeLayerPitch = MapLayout.NativeLayerPitch;
    public const double HorizontalPixelsPerGridUnit = 13;
    public static double LayerLeft(int layer) => 54 + layer * LayerPitch;
    public static double LayerCenterLeft(int layer) => LayerLeft(layer) + (LaneWidth - Width - 32) / 2;
    // The entry layer starts the map and the boss ends it (the game pins both), so
    // their nodes keep their horizontal position; they can still be added and moved vertically.
    public static bool HorizontallyFixed(MapLayoutNode node) => node.Layer == 0 || node.Type == "Boss";
    public static Point ConstrainToLayer(MapLayoutNode node, Point wanted) => new(
        Math.Clamp(HorizontallyFixed(node) ? Position(node).X : wanted.X, LayerCenterLeft(node.Layer) - MaxXOffsetPixels, LayerCenterLeft(node.Layer) + MaxXOffsetPixels),
        Math.Clamp(wanted.Y, MinY, MaxY));
    public static bool Overlaps(Point p, MapLayoutNode other, double verticalSpacing = VerticalSpacing)
    {
        var q = Position(other);
        return Math.Abs(p.X - q.X) < Width + Gap - .001 && Math.Abs(p.Y - q.Y) < verticalSpacing - .000001;
    }
    public static Point DragPosition(MapLayoutNode node, Point wanted, IEnumerable<MapLayoutNode> others)
    {
        var start = Position(node); var end = ConstrainToLayer(node, wanted);
        var blockers = others.Where(n => n != node).ToArray();
        double spacing = MapPlacementBounds.Separation(blockers.Count(n => n.Layer == node.Layer) + 1) * Units;
        int steps = Math.Max(1, (int)Math.Ceiling(((Vector)(end - start)).Length / 3));
        var result = start;
        for (int i = 1; i <= steps; i++)
        {
            var candidate = start + (end - start) * ((double)i / steps);
            if (blockers.Any(n => Overlaps(candidate, n, spacing))) break;
            result = candidate;
        }
        return result;
    }
    public static bool ArrangeLayers(MapLayout map)
    {
        // Legacy route choices cannot be safely collapsed or serialized into
        // extra encounters automatically. The editor asks for an explicit edit.
        if (MapPlacementBounds.CapacityErrors(map).Count > 0) return false;
        bool changed = false;
        // Repair crowded/imported layers from top to bottom without reordering
        // the authored node list (connections and IDs keep their identities).
        foreach (var group in map.Nodes.GroupBy(n => n.Layer))
        {
            var rows = group.OrderBy(n => n.Y).ToArray();
            bool repairRows = rows.Any(n => !MapPlacementBounds.Contains(n.Y)) || MapPlacementBounds.HasCrowdedRows(rows);
            for (int i = 0; i < rows.Length; i++)
            {
                var node = rows[i];
                var before = Position(node); var after = ConstrainToLayer(node, before);
                // Keep every already-valid layer unchanged. Repair only a crowded
                // or out-of-range layer, preserving its existing top-to-bottom order.
                if (repairRows) after = new Point(after.X, MinY + (MapPlacementBounds.RowY(rows.Length, i) - MapPlacementBounds.MinY) * Units);
                if (Math.Abs(before.X - LayerCenterLeft(node.Layer)) > MaxXOffsetPixels + .001)
                    after = new Point(LayerCenterLeft(node.Layer), after.Y);
                if (before != after) { Set(node, after); changed = true; }
            }
        }
        return changed;
    }
    public static Point Position(MapLayoutNode n) => new(
        LayerCenterLeft(n.Layer) + (n.X - (NativeStartX + n.Layer * NativeLayerPitch)) * HorizontalPixelsPerGridUnit,
        MinY + (n.Y - MapPlacementBounds.MinY) * Units);
    public static Point Nearest(Point wanted, IEnumerable<MapLayoutNode> others, Size? bounds = null)
    {
        const double min = Edge;
        double maxX = bounds is { } size ? size.Width - Width - Edge : 25035;
        double maxY = Math.Min(MaxY, bounds is { } area ? area.Height - Height - Edge : MaxY);
        if (maxX < min || maxY < MinY) throw new ArgumentException("No free space for this node.");
        wanted = new Point(Math.Clamp(wanted.X, min, maxX), Math.Clamp(wanted.Y, MinY, maxY));
        var boxes = others.Select(Position).ToArray();
        double w = Width + Gap, h = VerticalSpacing;
        var xs = boxes.SelectMany(p => new[] { p.X - w, p.X + w }).Append(wanted.X).Append(min).Append(maxX)
            .Where(x => x >= min && x <= maxX).Distinct();
        Point best = wanted; double distance = double.PositiveInfinity;
        foreach (double x in xs)
        {
            var intervals = boxes.Where(p => Math.Abs(p.X - x) < w - .000001)
                .Select(p => (Low: p.Y - h, High: p.Y + h)).OrderBy(p => p.Low).ToArray();
            var merged = new List<(double Low, double High)>();
            foreach (var interval in intervals)
            {
                if (merged.Count > 0 && interval.Low < merged[^1].High)
                    merged[^1] = (merged[^1].Low, Math.Max(merged[^1].High, interval.High));
                else merged.Add(interval);
            }
            double y = wanted.Y;
            foreach (var interval in merged)
            {
                if (y <= interval.Low || y >= interval.High) continue;
                y = interval.Low >= MinY && (interval.High > maxY || y - interval.Low <= interval.High - y)
                    ? interval.Low : interval.High;
                break;
            }
            if (y < MinY || y > maxY) continue;
            double d = (x - wanted.X) * (x - wanted.X) + (y - wanted.Y) * (y - wanted.Y);
            if (d < distance) { best = new Point(x, y); distance = d; }
        }
        if (double.IsPositiveInfinity(distance)) throw new ArgumentException("No free space for this node.");
        return best;
    }
    public static void Set(MapLayoutNode node, Point position)
    {
        node.X = NativeStartX + node.Layer * NativeLayerPitch
            + (position.X - LayerCenterLeft(node.Layer)) / HorizontalPixelsPerGridUnit;
        node.Y = MapPlacementBounds.Clamp(MapPlacementBounds.MinY + (position.Y - MinY) / Units);
    }
    public static bool Arrange(MapLayout map)
    {
        bool changed = false;
        var placed = new List<MapLayoutNode>();
        foreach (var node in map.Nodes)
        {
            var before = Position(node); var after = Nearest(before, placed);
            if (Math.Abs(before.X - after.X) + Math.Abs(before.Y - after.Y) > .000001) { Set(node, after); changed = true; }
            placed.Add(node);
        }
        return changed;
    }
}
