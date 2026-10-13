using System;
using System.Collections.Generic;

namespace CampaignCore;

/// <summary>Fitting full native artwork bounds without changing its scale.</summary>
public static class MapVisualFit
{
    // Native map camera is orthographic, half-height 4.5. The observed bottom
    // HUD begins around 88% of the 1017px frame: reserve 1.2 world units below
    // the artwork, plus .2 above. These are viewport margins, not authored Y.
    public const float CameraHalfHeight = 4.5f;
    public const float BottomMargin = 1.2f, TopMargin = .2f;
    public const float ColumnGap = .25f, RowGap = .15f, BadgePadding = .15f;
    public const float BottomMarginFraction = BottomMargin / (CameraHalfHeight * 2);
    public const float TopMarginFraction = TopMargin / (CameraHalfHeight * 2);
    // Extrema of the current shared native Highlight clip, rounded outward.
    // Reserve its overshoot, not only its final 20.71-unit icon size.
    public const float BadgeNormalSize = 15f, BadgeMaxSize = 22.9f;
    public const float BadgeMinY = -1.13f, BadgeMaxY = 4.03f;
    public const float RewardTextMinY = -17.12f, RewardTextMaxY = -4.22f;

    public readonly struct Envelope
    {
        public readonly float MinX, MaxX, MinY, MaxY;
        public Envelope(float minX, float maxX, float minY, float maxY)
        {
            if (!float.IsFinite(minX) || !float.IsFinite(maxX) || !float.IsFinite(minY) || !float.IsFinite(maxY)
                || maxX < minX || maxY < minY) throw new ArgumentOutOfRangeException(nameof(minX));
            MinX = minX; MaxX = maxX; MinY = minY; MaxY = maxY;
        }
        public Envelope Offset(float x, float y) => new(MinX + x, MaxX + x, MinY + y, MaxY + y);
        public Envelope Union(Envelope other) => new(Math.Min(MinX, other.MinX), Math.Max(MaxX, other.MaxX),
            Math.Min(MinY, other.MinY), Math.Max(MaxY, other.MaxY));
    }

    // The caller supplies columns in logical layer order. A whole column moves
    // together, and subsequent columns retain at least their original spacing.
    // The entrance never moves. The final (boss) column participates before the
    // scenery/intro/scroll extent is calculated.
    public static float[] ColumnOffsets(IReadOnlyList<Envelope> columns, float gap = ColumnGap)
    {
        if (!float.IsFinite(gap) || gap < 0) throw new ArgumentOutOfRangeException(nameof(gap));
        var offsets = new float[columns.Count];
        if (columns.Count == 0) return offsets;
        float right = columns[0].MaxX;
        for (int i = 1; i < columns.Count; i++)
        {
            offsets[i] = Math.Max(offsets[i - 1], right + gap - columns[i].MinX);
            right = Math.Max(right, columns[i].MaxX + offsets[i]);
        }
        return offsets;
    }

    // Rows are supplied bottom to top. Separate only footprints that overlap
    // horizontally, then balance the new space around the original column.
    // Legacy taller maps remain readable; no node is removed.
    public static float[] RowOffsets(IReadOnlyList<Envelope> rows, float gap = RowGap)
    {
        if (!float.IsFinite(gap) || gap < 0) throw new ArgumentOutOfRangeException(nameof(gap));
        var offsets = new float[rows.Count];
        for (int i = 1; i < rows.Count; i++)
        {
            offsets[i] = offsets[i - 1];
            for (int j = 0; j < i; j++)
                if (Math.Min(rows[i].MaxX, rows[j].MaxX) > Math.Max(rows[i].MinX, rows[j].MinX))
                    offsets[i] = Math.Max(offsets[i], rows[j].MaxY + offsets[j] + gap - rows[i].MinY);
        }
        float center = rows.Count == 0 ? 0 : offsets[rows.Count - 1] / 2;
        for (int i = 0; i < offsets.Length; i++) offsets[i] -= center;
        return offsets;
    }

    public readonly struct Frame
    {
        public readonly float HalfHeight, OffsetY;
        internal Frame(float halfHeight, float offsetY) { HalfHeight = halfHeight; OffsetY = offsetY; }
    }

    public readonly struct Placement
    {
        public readonly float X, Y;
        internal Placement(float x, float y) { X = x; Y = y; }
    }

    // Keep a column inside the existing background. If two unusually tall
    // arenas cannot stack in that space, stagger them horizontally instead
    // of zooming beyond the artwork or shrinking the native arena sprites.
    public static Placement[] FitColumn(IReadOnlyList<Envelope> rows, float cameraY, float halfHeight)
    {
        var result = new Placement[rows.Count];
        if (rows.Count == 0) return result;
        var separation = RowOffsets(rows);
        var bounds = rows[0].Offset(0, separation[0]);
        for (int i = 1; i < rows.Count; i++) bounds = bounds.Union(rows[i].Offset(0, separation[i]));
        if (TryVerticalOffset(bounds, cameraY, halfHeight, out float common))
        {
            for (int i = 0; i < rows.Count; i++) result[i] = new Placement(0, separation[i] + common);
            return result;
        }
        for (int i = 0; i < rows.Count; i++)
        {
            if (!TryVerticalOffset(rows[i], cameraY, halfHeight, out float y))
                throw new InvalidOperationException("A single map arena is taller than the native background.");
            float x = 0;
            int moves = 0;
            var placed = rows[i].Offset(x, y);
            for (int j = 0; j < i; j++)
            {
                var previous = rows[j].Offset(result[j].X, result[j].Y);
                if (placed.MinY < previous.MaxY + RowGap && placed.MaxY > previous.MinY - RowGap
                    && placed.MinX < previous.MaxX + ColumnGap && placed.MaxX > previous.MinX - ColumnGap)
                {
                    if (++moves > rows.Count)
                        throw new InvalidOperationException("Map artwork could not be separated at its current coordinates.");
                    // Clearance also guarantees progress after float rounding
                    // when an earlier row is checked again below.
                    x = Math.Max(x, previous.MaxX + ColumnGap - rows[i].MinX) + .001f;
                    placed = rows[i].Offset(x, y);
                    j = -1; // A move can encounter a previously tested staggered row.
                }
            }
            result[i] = new Placement(x, y);
        }
        return result;
    }

    // Keep both sides of the viewport inside the native scenery curtains.
    // A viewport wider than the entire route stays centred, with the game's
    // original edge masks, rather than repeating a border pixel indefinitely.
    public static (float Min, float Max) ScrollRange(float nativeMin, float nativeMax,
        float sceneryMin, float sceneryMax, float halfWidth)
    {
        if (!float.IsFinite(nativeMin) || !float.IsFinite(nativeMax) || nativeMin > nativeMax
            || !float.IsFinite(sceneryMin) || !float.IsFinite(sceneryMax) || sceneryMin >= sceneryMax
            || !float.IsFinite(halfWidth) || halfWidth <= 0) throw new ArgumentOutOfRangeException(nameof(halfWidth));
        float min = sceneryMin + halfWidth + .01f, max = sceneryMax - halfWidth - .01f;
        if (min > max) return ((sceneryMin + sceneryMax) / 2, (sceneryMin + sceneryMax) / 2);
        return (Math.Clamp(nativeMin, min, max), Math.Clamp(nativeMax, min, max));
    }

    // Calculate the desired viewport without changing world-space art size.
    // The caller caps this at the scenery height and uses FitColumn to stagger
    // a column that needs more room. HUD margins keep the same screen fractions.
    public static Frame FitFrame(Envelope bounds, float cameraY)
    {
        if (!float.IsFinite(cameraY)) throw new ArgumentOutOfRangeException(nameof(cameraY));
        float usable = 1 - BottomMarginFraction - TopMarginFraction;
        float halfHeight = Math.Max(CameraHalfHeight, (bounds.MaxY - bounds.MinY) / (2 * usable));
        if (!TryVerticalOffset(bounds, cameraY, halfHeight, out float offset))
            throw new InvalidOperationException("Cannot fit finite map artwork in its calculated viewport.");
        return new Frame(halfHeight, offset);
    }

    // All three current native maps have exactly two tall opaque Square
    // curtains on Fore/order 5. Deliberately reject unrelated black graphics.
    // Returns -1 for the left boundary, +1 for the right, and 0 otherwise.
    public static int NativeEdgeCurtainSide(bool directChild, string sprite, string sortingLayer,
        int sortingOrder, bool simpleDraw, bool opaqueBlack, Envelope curtain, Envelope background)
    {
        if (!directChild || sprite != "Square" || sortingLayer != "Fore" || sortingOrder != 5
            || !simpleDraw || !opaqueBlack || curtain.MinY > background.MinY || curtain.MaxY < background.MaxY
            || curtain.MaxX - curtain.MinX < (background.MaxX - background.MinX) * .4f
            || curtain.MaxY - curtain.MinY < (background.MaxY - background.MinY) * 2) return 0;
        float center = (curtain.MinX + curtain.MaxX) / 2;
        return center < background.MinX ? -1 : center > background.MaxX ? 1 : 0;
    }

    // Find the smallest common translation. A too-tall graph is explicitly
    // rejected; this never silently scales art or compresses the vertical rows.
    public static bool TryVerticalOffset(Envelope bounds, float cameraY, out float offset)
        => TryVerticalOffset(bounds, cameraY, CameraHalfHeight, out offset);

    public static bool TryVerticalOffset(Envelope bounds, float cameraY, float halfHeight, out float offset)
    {
        float min = cameraY - halfHeight + 2 * halfHeight * BottomMarginFraction;
        float max = cameraY + halfHeight - 2 * halfHeight * TopMarginFraction;
        float lower = min - bounds.MinY, upper = max - bounds.MaxY;
        offset = 0;
        if (!float.IsFinite(cameraY) || !float.IsFinite(halfHeight) || halfHeight <= 0 || lower > upper + .0001f) return false;
        offset = Math.Max(lower, Math.Min(0, upper));
        return true;
    }
}
