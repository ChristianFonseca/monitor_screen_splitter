namespace VscSplitter.Core.Models;

/// <summary>Root of the persisted configuration file.</summary>
public sealed class AppConfig
{
    public int Version { get; set; } = 1;

    public string ActiveProfile { get; set; } = "Principal";

    public AppSettings Settings { get; set; } = new();

    public List<Profile> Profiles { get; set; } = [];
}

public sealed class AppSettings
{
    /// <summary>Move VS Code windows into their zone as soon as they (re)appear.</summary>
    public bool AutoPlaceNewWindows { get; set; } = true;

    /// <summary>Apply the active profile when the app starts.</summary>
    public bool ApplyOnStartup { get; set; }

    /// <summary>Move windows immediately after each drag &amp; drop or layout edit.</summary>
    public bool LiveApply { get; set; }

    public bool MinimizeToTray { get; set; } = true;

    /// <summary>Process names (without .exe) whose windows are managed.</summary>
    public List<string> ProcessNames { get; set; } = ["Code", "Code - Insiders"];

    public WindowPlacementSettings? MainWindow { get; set; }

    /// <summary>Monitor shown in the editor when the app reopens.</summary>
    public string? SelectedMonitorId { get; set; }
}

public sealed class WindowPlacementSettings
{
    public double Left { get; set; }

    public double Top { get; set; }

    public double Width { get; set; }

    public double Height { get; set; }

    public bool Maximized { get; set; }
}

/// <summary>A named arrangement: one layout per monitor plus which window goes in each zone.</summary>
public sealed class Profile
{
    public string Name { get; set; } = "Principal";

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    public List<MonitorLayout> Monitors { get; set; } = [];
}

public sealed class MonitorLayout
{
    public string MonitorId { get; set; } = "";

    public string DeviceName { get; set; } = "";

    public string FriendlyName { get; set; } = "";

    /// <summary>Monitor bounds when the layout was saved; used as a fallback to recognise the monitor.</summary>
    public PixelRect Bounds { get; set; }

    public GridLayout Grid { get; set; } = new();

    /// <summary>Pixels between adjacent windows.</summary>
    public int Gap { get; set; }

    /// <summary>Pixels between the windows and the monitor edges.</summary>
    public int Margin { get; set; }

    /// <summary>Exclude the taskbar area (use the monitor work area).</summary>
    public bool UseWorkArea { get; set; } = true;
}

/// <summary>Rows × columns grid with adjustable track sizes; zones may span several cells.</summary>
public sealed class GridLayout
{
    public int Rows { get; set; } = 1;

    public int Columns { get; set; } = 1;

    public List<double> RowWeights { get; set; } = [];

    public List<double> ColumnWeights { get; set; } = [];

    public List<ZoneDefinition> Zones { get; set; } = [];
}

public sealed class ZoneDefinition
{
    public int Row { get; set; }

    public int Column { get; set; }

    public int RowSpan { get; set; } = 1;

    public int ColumnSpan { get; set; } = 1;

    /// <summary>Workspace key of the window assigned to this zone, if any.</summary>
    public string? WindowKey { get; set; }

    public string? WindowTitle { get; set; }

    /// <summary>VS Code folder/workspace URI, used to reopen the window if it is missing.</summary>
    public string? FolderUri { get; set; }
}
