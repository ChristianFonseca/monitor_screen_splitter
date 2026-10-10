using VscSplitter.Core.Layout;
using VscSplitter.Core.Models;

namespace VscSplitter.Tests;

public class LayoutEngineTests
{
    // Work area of a 4K monitor placed right of a 1080p primary monitor (taskbar 48px).
    private static readonly PixelRect Display2Work = new(1920, 0, 3840, 2112);

    [Fact]
    public void ThreeByTwoOn4kGivesExactSixths()
    {
        var grid = LayoutEngine.CreateGrid(2, 3);

        var rects = LayoutEngine.ComputeZoneRects(grid, Display2Work, gap: 0, margin: 0);

        Assert.Equal(
        [
            new PixelRect(1920, 0, 1280, 1056), new PixelRect(3200, 0, 1280, 1056), new PixelRect(4480, 0, 1280, 1056),
            new PixelRect(1920, 1056, 1280, 1056), new PixelRect(3200, 1056, 1280, 1056), new PixelRect(4480, 1056, 1280, 1056),
        ], rects);
    }

    [Theory]
    [InlineData(1, 7, 0, 0)]
    [InlineData(3, 7, 0, 0)]
    [InlineData(2, 3, 9, 0)]
    [InlineData(4, 5, 7, 13)]
    [InlineData(3, 3, 1, 1)]
    public void TracksTileTheAreaWithoutOverlapsOrHoles(int rows, int columns, int gap, int margin)
    {
        var area = new PixelRect(-3840, 0, 1919, 1033);
        var grid = LayoutEngine.CreateGrid(rows, columns);

        var cols = LayoutEngine.ComputeColumnTracks(grid, area, gap, margin);
        var rowTracks = LayoutEngine.ComputeRowTracks(grid, area, gap, margin);

        Assert.Equal(area.X + margin, cols[0].Start);
        Assert.Equal(area.Right - margin, cols[^1].End);
        Assert.Equal(area.Y + margin, rowTracks[0].Start);
        Assert.Equal(area.Bottom - margin, rowTracks[^1].End);
        for (var i = 1; i < cols.Length; i++)
        {
            Assert.Equal(gap, cols[i].Start - cols[i - 1].End);
        }

        for (var i = 1; i < rowTracks.Length; i++)
        {
            Assert.Equal(gap, rowTracks[i].Start - rowTracks[i - 1].End);
        }

        // Equal weights: sizes differ by at most one pixel.
        Assert.True(cols.Max(t => t.Length) - cols.Min(t => t.Length) <= 1);
    }

    [Fact]
    public void GapIsClampedSoTracksNeverVanish()
    {
        var grid = LayoutEngine.CreateGrid(1, 4);

        var rects = LayoutEngine.ComputeZoneRects(grid, new PixelRect(0, 0, 100, 50), gap: 500, margin: 0);

        Assert.All(rects, r => Assert.True(r.Width >= 1));
    }

    [Fact]
    public void MergeLeftColumnOfTwoByTwo()
    {
        var grid = LayoutEngine.CreateGrid(2, 2);
        grid.Zones[2].WindowKey = "bottom-left";

        var merged = LayoutEngine.TryMerge(grid, [0, 2]);

        Assert.NotNull(merged);
        Assert.Equal(3, grid.Zones.Count);
        Assert.True(LayoutEngine.IsValidTiling(grid));
        Assert.Equal((0, 0, 2, 1), (merged!.Row, merged.Column, merged.RowSpan, merged.ColumnSpan));
        Assert.Equal("bottom-left", merged.WindowKey);
        var rects = LayoutEngine.ComputeZoneRects(grid, new PixelRect(0, 0, 1000, 800), 0, 0);
        Assert.Equal(new PixelRect(0, 0, 500, 800), rects[grid.Zones.IndexOf(merged)]);
    }

    [Theory]
    [InlineData(new[] { 0, 3 })]    // diagonal
    [InlineData(new[] { 0, 1, 2 })] // L shape
    [InlineData(new[] { 1 })]       // single zone
    public void RejectsNonRectangularMerges(int[] indices)
    {
        var grid = LayoutEngine.CreateGrid(2, 2);

        Assert.Null(LayoutEngine.TryMerge(grid, indices));
        Assert.Equal(4, grid.Zones.Count);
    }

    [Fact]
    public void MergeCannotSwallowPartOfAnotherMergedZone()
    {
        var grid = LayoutEngine.CreateGrid(2, 3);
        LayoutEngine.TryMerge(grid, [1, 2]); // top-right two cells
        var topLeft = grid.Zones.FindIndex(z => z.Row == 0 && z.Column == 0);
        var bottomMiddle = grid.Zones.FindIndex(z => z.Row == 1 && z.Column == 1);

        // Bounding box of (0,0)-(1,1) cuts the merged zone in half.
        Assert.Null(LayoutEngine.TryMerge(grid, [topLeft, bottomMiddle, grid.Zones.FindIndex(z => z.Row == 1 && z.Column == 0)]));
    }

    [Fact]
    public void SplitRestoresCellsAndKeepsAssignmentTopLeft()
    {
        var grid = LayoutEngine.CreateGrid(2, 2);
        var merged = LayoutEngine.TryMerge(grid, [0, 1, 2, 3])!;
        merged.WindowKey = "big";

        var cells = LayoutEngine.Split(grid, merged);

        Assert.Equal(4, cells.Count);
        Assert.Equal(4, grid.Zones.Count);
        Assert.True(LayoutEngine.IsValidTiling(grid));
        Assert.Equal("big", grid.Zones[0].WindowKey);
        Assert.All(grid.Zones.Skip(1), z => Assert.Null(z.WindowKey));
    }

    [Fact]
    public void MoveBoundaryOnlyResizesNeighbours()
    {
        var grid = LayoutEngine.CreateGrid(1, 3);
        var area = new PixelRect(0, 0, 3000, 1000);

        LayoutEngine.MoveBoundary(grid.ColumnWeights, 0, area.X, area.Width, 0, 1500);
        var cols = LayoutEngine.ComputeColumnTracks(grid, area, 0, 0);

        Assert.Equal(1500, cols[0].End);
        Assert.Equal(2000, cols[1].End); // third column untouched
        Assert.Equal(1000, cols[2].Length);
    }

    [Fact]
    public void MoveBoundaryRespectsMinimumSize()
    {
        var grid = LayoutEngine.CreateGrid(1, 2);

        LayoutEngine.MoveBoundary(grid.ColumnWeights, 0, 0, 1000, 0, -500);

        Assert.True(grid.ColumnWeights[0] >= LayoutEngine.MinTrackFraction - 1e-9);
        Assert.Equal(1.0, grid.ColumnWeights.Sum(), 6);
    }

    [Fact]
    public void MoveBoundaryWithGapKeepsHandleUnderCursor()
    {
        var grid = LayoutEngine.CreateGrid(1, 2);
        var area = new PixelRect(100, 0, 1010, 500);

        LayoutEngine.MoveBoundary(grid.ColumnWeights, 0, area.X, area.Width, 10, 405);
        var cols = LayoutEngine.ComputeColumnTracks(grid, area, 10, 0);

        Assert.Equal(405, (cols[0].End + cols[1].Start) / 2.0, 0);
    }

    [Fact]
    public void NormalizeRepairsBrokenLayouts()
    {
        var grid = new GridLayout
        {
            Rows = 2,
            Columns = 2,
            RowWeights = [1],
            ColumnWeights = [double.NaN, 2],
            Zones = [new ZoneDefinition { Row = 0, Column = 0, RowSpan = 5, WindowKey = "kept" }],
        };

        LayoutEngine.Normalize(grid);

        Assert.True(LayoutEngine.IsValidTiling(grid));
        Assert.Equal(4, grid.Zones.Count);
        Assert.Equal("kept", grid.Zones[0].WindowKey);
        Assert.Equal([0.5, 0.5], grid.RowWeights);
    }

    [Fact]
    public void AllPresetsAreValid()
    {
        foreach (var preset in LayoutPresets.All)
        {
            var grid = preset.Create();
            Assert.True(LayoutEngine.IsValidTiling(grid), preset.Name);
        }

        Assert.Equal(6, LayoutPresets.All.Single(p => p.Id == "3x2").Create().Zones.Count);
        Assert.Equal(5, LayoutPresets.All.Single(p => p.Id == "main4").Create().Zones.Count);
        Assert.Equal(3, LayoutPresets.All.Single(p => p.Id == "main2").Create().Zones.Count);
    }
}
