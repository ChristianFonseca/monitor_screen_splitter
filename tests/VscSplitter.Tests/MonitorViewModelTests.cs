using VscSplitter.App.ViewModels;
using VscSplitter.Core.Layout;
using VscSplitter.Core.Models;

namespace VscSplitter.Tests;

public class MonitorViewModelTests
{
    private static MonitorViewModel Monitor(GridLayout grid) => new(
        new MonitorInfo
        {
            Id = "id",
            DeviceName = @"\\.\DISPLAY2",
            Number = 2,
            Bounds = new PixelRect(1920, 0, 3840, 2160),
            WorkArea = new PixelRect(1920, 0, 3840, 2112),
        },
        new MonitorLayout { Grid = grid });

    private static WindowItemViewModel Window(string key) => new(key);

    [Fact]
    public void ZonesUseTheWorkAreaAndExposeGrips()
    {
        var monitor = Monitor(LayoutEngine.CreateGrid(2, 3));

        Assert.Equal(new PixelRect(4480, 1056, 1280, 1056), monitor.Zones[5].PixelRect);
        Assert.Equal(2112 / 2160.0, monitor.ZoneAreaRect.Height, 6);
        Assert.Equal(2, monitor.ColumnHandles.Count);
        Assert.Single(monitor.RowHandles);
        Assert.Equal(1280 / 3840.0, monitor.ColumnHandles[0].RelRect.X, 6);
    }

    [Fact]
    public void AWindowLivesInOneZoneOnly()
    {
        var monitor = Monitor(LayoutEngine.CreateGrid(1, 2));
        var window = Window("api");

        monitor.Zones[0].Window = window;
        monitor.Zones[1].Window = window;

        Assert.Null(monitor.Zones[0].Window);
        Assert.Same(monitor.Zones[1], window.Zone);
        Assert.Equal("M2 · 2", window.AssignmentLabel);

        monitor.Zones[1].Window = null;
        Assert.Null(window.Zone);
        Assert.False(window.IsAssigned);
    }

    [Fact]
    public void MergeKeepsTheFirstWindowAndReturnsTheDisplacedOnes()
    {
        var monitor = Monitor(LayoutEngine.CreateGrid(2, 2));
        WindowItemViewModel a = Window("a"), c = Window("c"), d = Window("d");
        monitor.Zones[0].Window = a;
        monitor.Zones[2].Window = c;
        monitor.Zones[3].Window = d;
        monitor.Zones[0].IsSelected = true;
        monitor.Zones[2].IsSelected = true;

        var displaced = monitor.MergeSelected();

        Assert.Equal([c], displaced!);
        Assert.Equal(3, monitor.Zones.Count);
        Assert.True(monitor.Zones[0].IsMerged);
        Assert.Same(a, monitor.Zones[0].Window);
        Assert.Same(d, monitor.Zones.Single(z => z.Definition is { Row: 1, Column: 1 }).Window);
        Assert.Null(c.Zone);
        Assert.Equal(new PixelRect(1920, 0, 1920, 2112), monitor.Zones[0].PixelRect);
    }

    [Fact]
    public void MergeRejectsNonRectangularSelection()
    {
        var monitor = Monitor(LayoutEngine.CreateGrid(2, 2));
        monitor.Zones[0].IsSelected = true;
        monitor.Zones[3].IsSelected = true;

        Assert.Null(monitor.MergeSelected());
        Assert.Equal(4, monitor.Zones.Count);
    }

    [Fact]
    public void SplitGivesTheWindowToTheTopLeftCell()
    {
        var monitor = Monitor(LayoutPresets.All.Single(p => p.Id == "main4").Create());
        var big = Window("big");
        monitor.Zones[0].Window = big;
        monitor.Zones[0].IsSelected = true;

        Assert.True(monitor.SplitSelected());

        Assert.Equal(6, monitor.Zones.Count);
        Assert.Same(big, monitor.Zones[0].Window);
        Assert.Null(monitor.Zones[3].Window);
    }

    [Fact]
    public void ReplaceGridReflowsWindowsInReadingOrder()
    {
        var monitor = Monitor(LayoutEngine.CreateGrid(2, 3));
        var windows = Enumerable.Range(0, 6).Select(i => Window($"w{i}")).ToList();
        for (var i = 0; i < windows.Count; i++)
        {
            monitor.Zones[i].Window = windows[i];
        }

        var leftovers = monitor.ReplaceGrid(LayoutEngine.CreateGrid(2, 2));

        Assert.Equal(windows.Take(4), monitor.Zones.Select(z => z.Window));
        Assert.Equal(windows.Skip(4), leftovers);
        Assert.All(leftovers, w => Assert.Null(w.Zone));
        Assert.Equal(2, monitor.Columns);
    }

    [Fact]
    public void DraggingAColumnBoundaryOnlyResizesItsNeighbours()
    {
        var monitor = Monitor(LayoutEngine.CreateGrid(1, 3));

        monitor.MoveColumnBoundary(0, 0.5);

        Assert.Equal(1920, monitor.Zones[0].PixelRect.Width);
        Assert.Equal(3840, monitor.Zones[1].PixelRect.X);
        Assert.Equal(new PixelRect(4480, 0, 1280, 2112), monitor.Zones[2].PixelRect);
    }

    [Fact]
    public void GapAndMarginAreAppliedInPixels()
    {
        var monitor = Monitor(LayoutEngine.CreateGrid(1, 2));

        monitor.Margin = 10;
        monitor.Gap = 8;

        Assert.Equal(new PixelRect(1930, 10, 1906, 2092), monitor.Zones[0].PixelRect);
        Assert.Equal(8, monitor.Zones[1].PixelRect.X - monitor.Zones[0].PixelRect.Right);
        Assert.Equal(5750, monitor.Zones[1].PixelRect.Right);
    }

    [Fact]
    public void ClosedWindowBecomesPlaceholder()
    {
        var window = Window("api");
        window.Update(new EditorWindowInfo { Handle = new IntPtr(42), Title = "x - api - Visual Studio Code", WorkspaceKey = "api" });
        Assert.False(window.IsMissing);

        window.MarkClosed();

        Assert.True(window.IsMissing);
        Assert.StartsWith("No abierta", window.Subtitle);
    }
}
