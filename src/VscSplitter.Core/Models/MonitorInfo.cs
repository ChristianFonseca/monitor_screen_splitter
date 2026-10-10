namespace VscSplitter.Core.Models;

/// <summary>Snapshot of a connected monitor as Windows reports it (physical pixels).</summary>
public sealed record MonitorInfo
{
    /// <summary>Stable identifier (monitor device path) used to recognise the monitor across restarts.</summary>
    public required string Id { get; init; }

    /// <summary>GDI device name, e.g. <c>\\.\DISPLAY2</c>.</summary>
    public required string DeviceName { get; init; }

    public string FriendlyName { get; init; } = "";

    /// <summary>The N in <c>\\.\DISPLAYN</c>; shown to the user as the monitor number.</summary>
    public int Number { get; init; }

    public PixelRect Bounds { get; init; }

    /// <summary>Bounds minus the taskbar and docked app bars.</summary>
    public PixelRect WorkArea { get; init; }

    public int Dpi { get; init; } = 96;

    public bool IsPrimary { get; init; }

    public int ScalePercent => (int)Math.Round(Dpi * 100 / 96.0);
}
