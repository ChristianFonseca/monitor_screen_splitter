using VscSplitter.Core.Models;

namespace VscSplitter.Core.Layout;

public sealed record LayoutPreset(
    string Id,
    string Name,
    int Rows,
    int Columns,
    IReadOnlyList<(int Row, int Column, int RowSpan, int ColumnSpan)> Merges,
    IReadOnlyList<double>? ColumnWeights = null,
    IReadOnlyList<double>? RowWeights = null)
{
    public GridLayout Create()
    {
        var grid = LayoutEngine.CreateGrid(Rows, Columns);
        if (ColumnWeights is not null)
        {
            grid.ColumnWeights = LayoutEngine.NormalizeWeights(ColumnWeights, grid.Columns);
        }

        if (RowWeights is not null)
        {
            grid.RowWeights = LayoutEngine.NormalizeWeights(RowWeights, grid.Rows);
        }

        foreach (var m in Merges)
        {
            var indices = grid.Zones
                .Select((z, i) => (z, i))
                .Where(t => t.z.Row >= m.Row && t.z.Row < m.Row + m.RowSpan &&
                            t.z.Column >= m.Column && t.z.Column < m.Column + m.ColumnSpan)
                .Select(t => t.i)
                .ToList();
            LayoutEngine.TryMerge(grid, indices);
        }

        return grid;
    }
}

public static class LayoutPresets
{
    public static IReadOnlyList<LayoutPreset> All { get; } =
    [
        new("full", "Completa", 1, 1, []),
        new("2c", "2 columnas", 1, 2, []),
        new("3c", "3 columnas", 1, 3, []),
        new("4c", "4 columnas", 1, 4, []),
        new("2r", "2 filas", 2, 1, []),
        new("2x2", "2 × 2", 2, 2, []),
        new("3x2", "3 columnas × 2 filas", 2, 3, []),
        new("2x3", "2 columnas × 3 filas", 3, 2, []),
        new("4x2", "4 columnas × 2 filas", 2, 4, []),
        new("3x3", "3 × 3", 3, 3, []),
        new("main2", "Principal + 2", 2, 2, [(0, 0, 2, 1)], ColumnWeights: [0.6, 0.4]),
        new("main4", "Principal + 4", 2, 3, [(0, 0, 2, 1)], ColumnWeights: [0.4, 0.3, 0.3]),
        new("center", "Centro amplio", 1, 3, [], ColumnWeights: [0.25, 0.5, 0.25]),
        new("center4", "Centro + 4", 2, 3, [(0, 1, 2, 1)], ColumnWeights: [0.25, 0.5, 0.25]),
    ];
}
