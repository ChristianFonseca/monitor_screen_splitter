using VscSplitter.Core.Models;

namespace VscSplitter.Core.Layout;

/// <summary>
/// Pure geometry for grid layouts: zone rectangles, merging/splitting cells and dragging boundaries.
/// All pixel math uses shared integer boundaries so adjacent zones never overlap or leave 1px gaps.
/// </summary>
public static class LayoutEngine
{
    public const int MaxTracks = 12;
    public const double MinTrackFraction = 0.05;

    public readonly record struct Track(int Start, int End)
    {
        public int Length => End - Start;
    }

    public static GridLayout CreateGrid(int rows, int columns)
    {
        rows = Math.Clamp(rows, 1, MaxTracks);
        columns = Math.Clamp(columns, 1, MaxTracks);
        var grid = new GridLayout
        {
            Rows = rows,
            Columns = columns,
            RowWeights = EqualWeights(rows),
            ColumnWeights = EqualWeights(columns),
        };
        for (var r = 0; r < rows; r++)
        {
            for (var c = 0; c < columns; c++)
            {
                grid.Zones.Add(new ZoneDefinition { Row = r, Column = c });
            }
        }

        return grid;
    }

    public static List<double> EqualWeights(int count) => Enumerable.Repeat(1.0 / count, count).ToList();

    public static List<double> NormalizeWeights(IReadOnlyList<double>? weights, int count)
    {
        if (weights is null || weights.Count != count || weights.Any(w => double.IsNaN(w) || double.IsInfinity(w) || w <= 0))
        {
            return EqualWeights(count);
        }

        var sum = weights.Sum();
        return weights.Select(w => w / sum).ToList();
    }

    /// <summary>Repairs a layout loaded from disk: clamps sizes, fixes weights and rebuilds broken zone sets.</summary>
    public static void Normalize(GridLayout grid)
    {
        grid.Rows = Math.Clamp(grid.Rows, 1, MaxTracks);
        grid.Columns = Math.Clamp(grid.Columns, 1, MaxTracks);
        grid.RowWeights = NormalizeWeights(grid.RowWeights, grid.Rows);
        grid.ColumnWeights = NormalizeWeights(grid.ColumnWeights, grid.Columns);
        grid.Zones ??= [];

        if (!IsValidTiling(grid))
        {
            var previous = grid.Zones.OrderBy(z => z.Row).ThenBy(z => z.Column).ToList();
            var fresh = CreateGrid(grid.Rows, grid.Columns).Zones;
            for (var i = 0; i < fresh.Count && i < previous.Count; i++)
            {
                CopyAssignment(previous[i], fresh[i]);
            }

            grid.Zones = fresh;
        }

        SortZones(grid);
    }

    public static bool IsValidTiling(GridLayout grid)
    {
        if (grid.Zones.Count == 0)
        {
            return false;
        }

        var cover = new int[grid.Rows, grid.Columns];
        foreach (var z in grid.Zones)
        {
            if (z.Row < 0 || z.Column < 0 || z.RowSpan < 1 || z.ColumnSpan < 1 ||
                z.Row + z.RowSpan > grid.Rows || z.Column + z.ColumnSpan > grid.Columns)
            {
                return false;
            }

            for (var r = z.Row; r < z.Row + z.RowSpan; r++)
            {
                for (var c = z.Column; c < z.Column + z.ColumnSpan; c++)
                {
                    cover[r, c]++;
                }
            }
        }

        foreach (var count in cover)
        {
            if (count != 1)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Zones are kept in reading order (top-to-bottom, left-to-right); their index is the zone number.</summary>
    public static void SortZones(GridLayout grid) =>
        grid.Zones.Sort((a, b) => a.Row != b.Row ? a.Row.CompareTo(b.Row) : a.Column.CompareTo(b.Column));

    public static int EffectiveGap(int length, int count, int gap)
    {
        if (count <= 1)
        {
            return 0;
        }

        var max = Math.Max(0, (length - count) / (count - 1));
        return Math.Clamp(gap, 0, max);
    }

    /// <summary>Splits <paramref name="length"/> pixels into tracks proportional to the weights, separated by <paramref name="gap"/>.</summary>
    public static Track[] ComputeTracks(int origin, int length, IReadOnlyList<double> weights, int gap)
    {
        var count = weights.Count;
        gap = EffectiveGap(length, count, gap);
        var available = Math.Max(count, length - gap * (count - 1));
        var total = weights.Sum();
        if (total <= 0)
        {
            total = 1;
        }

        var tracks = new Track[count];
        double cumulative = 0;
        for (var i = 0; i < count; i++)
        {
            var start = (int)Math.Round(available * cumulative, MidpointRounding.AwayFromZero);
            cumulative += weights[i] / total;
            var end = i == count - 1 ? available : (int)Math.Round(available * cumulative, MidpointRounding.AwayFromZero);
            tracks[i] = new Track(origin + start + gap * i, origin + end + gap * i);
        }

        return tracks;
    }

    public static PixelRect GetContentArea(PixelRect area, int margin) => margin <= 0 ? area : area.Deflate(margin);

    public static Track[] ComputeColumnTracks(GridLayout grid, PixelRect area, int gap, int margin)
    {
        var inner = GetContentArea(area, margin);
        return ComputeTracks(inner.X, inner.Width, NormalizeWeights(grid.ColumnWeights, grid.Columns), gap);
    }

    public static Track[] ComputeRowTracks(GridLayout grid, PixelRect area, int gap, int margin)
    {
        var inner = GetContentArea(area, margin);
        return ComputeTracks(inner.Y, inner.Height, NormalizeWeights(grid.RowWeights, grid.Rows), gap);
    }

    /// <summary>Pixel rectangle of every zone (same order as <see cref="GridLayout.Zones"/>).</summary>
    public static IReadOnlyList<PixelRect> ComputeZoneRects(GridLayout grid, PixelRect area, int gap, int margin)
    {
        var cols = ComputeColumnTracks(grid, area, gap, margin);
        var rows = ComputeRowTracks(grid, area, gap, margin);
        return grid.Zones.Select(z =>
        {
            var c0 = Math.Clamp(z.Column, 0, cols.Length - 1);
            var c1 = Math.Clamp(z.Column + z.ColumnSpan - 1, c0, cols.Length - 1);
            var r0 = Math.Clamp(z.Row, 0, rows.Length - 1);
            var r1 = Math.Clamp(z.Row + z.RowSpan - 1, r0, rows.Length - 1);
            return PixelRect.FromLtrb(cols[c0].Start, rows[r0].Start, cols[c1].End, rows[r1].End);
        }).ToList();
    }

    /// <summary>
    /// Merges the zones at <paramref name="indices"/> into one zone when together they form an exact rectangle.
    /// The first assigned window (reading order) is kept. Returns the merged zone, or null when not mergeable.
    /// </summary>
    public static ZoneDefinition? TryMerge(GridLayout grid, IEnumerable<int> indices)
    {
        var selected = indices.Distinct()
            .Where(i => i >= 0 && i < grid.Zones.Count)
            .Select(i => grid.Zones[i])
            .ToList();
        if (selected.Count < 2)
        {
            return null;
        }

        var r0 = selected.Min(z => z.Row);
        var c0 = selected.Min(z => z.Column);
        var r1 = selected.Max(z => z.Row + z.RowSpan);
        var c1 = selected.Max(z => z.Column + z.ColumnSpan);

        // Every zone touching the bounding box must be part of the selection, otherwise the union is not a rectangle.
        var selectedSet = new HashSet<ZoneDefinition>(selected, ReferenceEqualityComparer.Instance);
        foreach (var z in grid.Zones)
        {
            var intersects = z.Row < r1 && z.Row + z.RowSpan > r0 && z.Column < c1 && z.Column + z.ColumnSpan > c0;
            if (intersects && !selectedSet.Contains(z))
            {
                return null;
            }
        }

        var merged = new ZoneDefinition { Row = r0, Column = c0, RowSpan = r1 - r0, ColumnSpan = c1 - c0 };
        var keeper = selected.OrderBy(z => z.Row).ThenBy(z => z.Column).FirstOrDefault(z => z.WindowKey is not null);
        if (keeper is not null)
        {
            CopyAssignment(keeper, merged);
        }

        grid.Zones.RemoveAll(selectedSet.Contains);
        grid.Zones.Add(merged);
        SortZones(grid);
        return merged;
    }

    /// <summary>Splits a merged zone back into single cells; the top-left cell keeps the assignment.</summary>
    public static IReadOnlyList<ZoneDefinition> Split(GridLayout grid, ZoneDefinition zone)
    {
        var index = grid.Zones.IndexOf(zone);
        if (index < 0 || (zone.RowSpan == 1 && zone.ColumnSpan == 1))
        {
            return index < 0 ? [] : [zone];
        }

        var cells = new List<ZoneDefinition>();
        for (var r = zone.Row; r < zone.Row + zone.RowSpan; r++)
        {
            for (var c = zone.Column; c < zone.Column + zone.ColumnSpan; c++)
            {
                cells.Add(new ZoneDefinition { Row = r, Column = c });
            }
        }

        CopyAssignment(zone, cells[0]);
        grid.Zones.RemoveAt(index);
        grid.Zones.AddRange(cells);
        SortZones(grid);
        return cells;
    }

    /// <summary>
    /// Moves the boundary between track <paramref name="index"/> and the next one so it sits at
    /// <paramref name="position"/> (pixels, same space as <paramref name="origin"/>). Only the two
    /// neighbouring tracks change size.
    /// </summary>
    public static bool MoveBoundary(List<double> weights, int index, int origin, int length, int gap, double position)
    {
        var count = weights.Count;
        if (index < 0 || index >= count - 1)
        {
            return false;
        }

        var normalized = NormalizeWeights(weights, count);
        gap = EffectiveGap(length, count, gap);
        double available = Math.Max(count, length - gap * (count - 1));
        var fraction = (position - origin - gap * index - gap / 2.0) / available;
        var before = normalized.Take(index).Sum();
        var pair = normalized[index] + normalized[index + 1];
        var min = Math.Min(MinTrackFraction, pair * 0.45);
        var first = Math.Clamp(fraction - before, min, pair - min);
        normalized[index] = first;
        normalized[index + 1] = pair - first;
        weights.Clear();
        weights.AddRange(normalized);
        return true;
    }

    private static void CopyAssignment(ZoneDefinition from, ZoneDefinition to)
    {
        to.WindowKey = from.WindowKey;
        to.WindowTitle = from.WindowTitle;
        to.FolderUri = from.FolderUri;
    }
}
