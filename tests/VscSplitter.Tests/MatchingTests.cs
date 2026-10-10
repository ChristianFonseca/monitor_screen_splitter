using VscSplitter.Core.Layout;
using VscSplitter.Core.Models;

namespace VscSplitter.Tests;

public class ZoneCaptureTests
{
    [Fact]
    public void CapturesHandPlacedWindowsOnA4kMonitor()
    {
        var zones = LayoutEngine.ComputeZoneRects(LayoutEngine.CreateGrid(2, 3), new PixelRect(1920, 0, 3840, 2112), 0, 0);
        // Visible frames measured on a real 4K monitor, arranged by hand (slightly misaligned).
        PixelRect[] windows =
        [
            PixelRect.FromLtrb(1929, 7, 3200, 1069),     // -> zone 0
            PixelRect.FromLtrb(3197, 5, 4486, 1066),     // -> zone 1
            PixelRect.FromLtrb(4487, 4, 5752, 1064),     // -> zone 2
            PixelRect.FromLtrb(1928, 1066, 3199, 2108),  // -> zone 3
            PixelRect.FromLtrb(3198, 1066, 4485, 2109),  // -> zone 4
            PixelRect.FromLtrb(4483, 1064, 5751, 2107),  // -> zone 5
            PixelRect.FromLtrb(2261, 789, 4123, 1977),   // big floating window: no free zone left
        ];

        var result = ZoneCapture.Assign(zones, windows);

        Assert.Equal([0, 1, 2, 3, 4, 5, -1], result);
    }

    [Fact]
    public void WindowOutsideEveryZoneIsNotAssigned()
    {
        var zones = new[] { new PixelRect(0, 0, 100, 100) };

        Assert.Equal([-1], ZoneCapture.Assign(zones, [new PixelRect(500, 500, 100, 100)]));
    }
}

public class MonitorMatcherTests
{
    private static MonitorInfo Monitor(string id, string device, int x) => new()
    {
        Id = id,
        DeviceName = device,
        Bounds = new PixelRect(x, 0, 3840, 2160),
        WorkArea = new PixelRect(x, 0, 3840, 2112),
    };

    [Fact]
    public void PrefersStableIdThenBoundsThenDeviceName()
    {
        var monitors = new[]
        {
            Monitor("id-A", @"\\.\DISPLAY4", -3840),
            Monitor("id-B", @"\\.\DISPLAY2", 1920),
            Monitor("id-C", @"\\.\DISPLAY3", 5760),
        };
        var byId = new MonitorLayout { MonitorId = "id-B", DeviceName = @"\\.\DISPLAY9", Bounds = new PixelRect(0, 0, 1, 1) };
        var byBounds = new MonitorLayout { MonitorId = "gone", Bounds = new PixelRect(5760, 0, 3840, 2160) };
        var byName = new MonitorLayout { MonitorId = "gone-too", DeviceName = @"\\.\DISPLAY4", Bounds = new PixelRect(9, 9, 9, 9) };

        var result = MonitorMatcher.Match(monitors, [byName, byBounds, byId]);

        Assert.Same(byName, result[0]);
        Assert.Same(byId, result[1]);
        Assert.Same(byBounds, result[2]);
    }

    [Fact]
    public void LayoutIsNeverUsedTwice()
    {
        var monitors = new[] { Monitor("a", @"\\.\DISPLAY1", 0), Monitor("b", @"\\.\DISPLAY2", 3840) };
        var only = new MonitorLayout { MonitorId = "x", DeviceName = @"\\.\DISPLAY1", Bounds = new PixelRect(3840, 0, 3840, 2160) };

        var result = MonitorMatcher.Match(monitors, [only]);

        Assert.Null(result[0]);
        Assert.Same(only, result[1]);
    }
}
