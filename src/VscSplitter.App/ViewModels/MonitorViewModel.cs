using System.Collections.ObjectModel;
using System.Windows;
using VscSplitter.Core.Layout;
using VscSplitter.Core.Models;

namespace VscSplitter.App.ViewModels;

/// <summary>A connected monitor with its editable layout (the profile's <see cref="MonitorLayout"/>).</summary>
public sealed class MonitorViewModel : ObservableObject
{
    private Rect _overviewRect;
    private Rect _zoneAreaRect;
    private bool _isSelected;

    public MonitorViewModel(MonitorInfo info, MonitorLayout layout)
    {
        Info = info;
        Layout = layout;
        LayoutEngine.Normalize(layout.Grid);
        RebuildZones(_ => null);
    }

    /// <summary>Raised when the user changes geometry (gap, margin, boundaries) without changing the zone set.</summary>
    public event EventHandler? GeometryEdited;

    public MonitorInfo Info { get; }

    public MonitorLayout Layout { get; }

    public ObservableCollection<ZoneViewModel> Zones { get; } = [];

    public ObservableCollection<BoundaryHandleViewModel> ColumnHandles { get; } = [];

    public ObservableCollection<BoundaryHandleViewModel> RowHandles { get; } = [];

    public int Number => Info.Number;

    public string Title => $"Monitor {Info.Number}";

    public string Resolution => $"{Info.Bounds.Width} × {Info.Bounds.Height}";

    public string Caption => $"{Info.Number} · {Info.Bounds.Width}×{Info.Bounds.Height}";

    public string Details =>
        $"{Info.FriendlyName} · escala {Info.ScalePercent}% · posición ({Info.Bounds.X}, {Info.Bounds.Y})" +
        (Info.IsPrimary ? " · principal" : "");

    public double AspectRatio => Info.Bounds.Width / (double)Math.Max(1, Info.Bounds.Height);

    public int Rows => Layout.Grid.Rows;

    public int Columns => Layout.Grid.Columns;

    public string GridLabel => $"{Columns} {(Columns == 1 ? "columna" : "columnas")} × {Rows} {(Rows == 1 ? "fila" : "filas")} · {Zones.Count} {(Zones.Count == 1 ? "zona" : "zonas")}";

    /// <summary>Position of this monitor in the desktop overview (fractions of the whole desktop).</summary>
    public Rect OverviewRect
    {
        get => _overviewRect;
        set => SetProperty(ref _overviewRect, value);
    }

    /// <summary>Usable area (work area or full monitor) as fractions of the monitor bounds.</summary>
    public Rect ZoneAreaRect
    {
        get => _zoneAreaRect;
        private set => SetProperty(ref _zoneAreaRect, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public int Gap
    {
        get => Layout.Gap;
        set
        {
            value = Math.Clamp(value, 0, 200);
            if (Layout.Gap == value)
            {
                return;
            }

            Layout.Gap = value;
            OnPropertyChanged();
            RecalculateGeometry();
            GeometryEdited?.Invoke(this, EventArgs.Empty);
        }
    }

    public int Margin
    {
        get => Layout.Margin;
        set
        {
            value = Math.Clamp(value, 0, 400);
            if (Layout.Margin == value)
            {
                return;
            }

            Layout.Margin = value;
            OnPropertyChanged();
            RecalculateGeometry();
            GeometryEdited?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool UseWorkArea
    {
        get => Layout.UseWorkArea;
        set
        {
            if (Layout.UseWorkArea == value)
            {
                return;
            }

            Layout.UseWorkArea = value;
            OnPropertyChanged();
            RecalculateGeometry();
            GeometryEdited?.Invoke(this, EventArgs.Empty);
        }
    }

    public PixelRect ZoneArea => Layout.UseWorkArea ? Info.WorkArea : Info.Bounds;

    public IEnumerable<WindowItemViewModel> AssignedWindows =>
        Zones.Select(z => z.Window).OfType<WindowItemViewModel>();

    /// <summary>Replaces the grid; windows are re-flowed into the new zones in reading order. Returns those that no longer fit.</summary>
    public List<WindowItemViewModel> ReplaceGrid(GridLayout grid)
    {
        var windows = AssignedWindows.ToList();
        LayoutEngine.Normalize(grid);
        Layout.Grid = grid;
        var queue = new Queue<WindowItemViewModel>(windows);
        RebuildZones(_ => queue.Count > 0 ? queue.Dequeue() : null);
        return queue.ToList();
    }

    /// <summary>Merges the selected zones. Returns the windows displaced by the merge, or null if the selection is not a rectangle.</summary>
    public List<WindowItemViewModel>? MergeSelected()
    {
        var selected = Zones.Where(z => z.IsSelected).ToList();
        if (selected.Count < 2)
        {
            return null;
        }

        var windows = CurrentWindowsByDefinition();
        var merged = LayoutEngine.TryMerge(Layout.Grid, selected.Select(z => z.Index));
        if (merged is null)
        {
            return null;
        }

        var inMerge = selected
            .OrderBy(z => z.Definition.Row).ThenBy(z => z.Definition.Column)
            .Select(z => z.Window)
            .OfType<WindowItemViewModel>()
            .ToList();
        var keep = inMerge.FirstOrDefault();
        RebuildZones(d => ReferenceEquals(d, merged) ? keep : windows.GetValueOrDefault(d));
        var mergedZone = Zones.FirstOrDefault(z => ReferenceEquals(z.Definition, merged));
        if (mergedZone is not null)
        {
            mergedZone.IsSelected = true;
        }

        return inMerge.Skip(1).ToList();
    }

    /// <summary>Splits every selected merged zone back into single cells.</summary>
    public bool SplitSelected()
    {
        var targets = Zones.Where(z => z.IsSelected && z.IsMerged).ToList();
        if (targets.Count == 0)
        {
            return false;
        }

        var windows = CurrentWindowsByDefinition();
        foreach (var zone in targets)
        {
            var cells = LayoutEngine.Split(Layout.Grid, zone.Definition);
            if (windows.Remove(zone.Definition, out var window) && cells.Count > 0)
            {
                windows[cells[0]] = window;
            }
        }

        RebuildZones(d => windows.GetValueOrDefault(d));
        return true;
    }

    public void EqualizeTracks()
    {
        Layout.Grid.ColumnWeights = LayoutEngine.EqualWeights(Columns);
        Layout.Grid.RowWeights = LayoutEngine.EqualWeights(Rows);
        RecalculateGeometry();
        GeometryEdited?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Moves a column boundary to <paramref name="relativeX"/> (fraction of the monitor width).</summary>
    public void MoveColumnBoundary(int index, double relativeX)
    {
        var area = LayoutEngine.GetContentArea(ZoneArea, Layout.Margin);
        var x = Info.Bounds.X + relativeX * Info.Bounds.Width;
        if (LayoutEngine.MoveBoundary(Layout.Grid.ColumnWeights, index, area.X, area.Width, Layout.Gap, x))
        {
            RecalculateGeometry();
            GeometryEdited?.Invoke(this, EventArgs.Empty);
        }
    }

    public void MoveRowBoundary(int index, double relativeY)
    {
        var area = LayoutEngine.GetContentArea(ZoneArea, Layout.Margin);
        var y = Info.Bounds.Y + relativeY * Info.Bounds.Height;
        if (LayoutEngine.MoveBoundary(Layout.Grid.RowWeights, index, area.Y, area.Height, Layout.Gap, y))
        {
            RecalculateGeometry();
            GeometryEdited?.Invoke(this, EventArgs.Empty);
        }
    }

    public void ClearZoneSelection()
    {
        foreach (var zone in Zones)
        {
            zone.IsSelected = false;
        }
    }

    public void RecalculateGeometry()
    {
        var area = ZoneArea;
        var rects = LayoutEngine.ComputeZoneRects(Layout.Grid, area, Layout.Gap, Layout.Margin);
        for (var i = 0; i < Zones.Count && i < rects.Count; i++)
        {
            Zones[i].PixelRect = rects[i];
            Zones[i].RelRect = ToRelative(rects[i]);
        }

        ZoneAreaRect = ToRelative(area);

        var content = LayoutEngine.GetContentArea(area, Layout.Margin);
        var contentRel = ToRelative(content);
        var columns = LayoutEngine.ComputeColumnTracks(Layout.Grid, area, Layout.Gap, Layout.Margin);
        var rows = LayoutEngine.ComputeRowTracks(Layout.Grid, area, Layout.Gap, Layout.Margin);
        SyncHandles(ColumnHandles, columns, isColumn: true, contentRel);
        SyncHandles(RowHandles, rows, isColumn: false, contentRel);
    }

    private Dictionary<ZoneDefinition, WindowItemViewModel> CurrentWindowsByDefinition()
    {
        var map = new Dictionary<ZoneDefinition, WindowItemViewModel>(ReferenceEqualityComparer.Instance);
        foreach (var zone in Zones)
        {
            if (zone.Window is { } window)
            {
                map[zone.Definition] = window;
            }
        }

        return map;
    }

    private void RebuildZones(Func<ZoneDefinition, WindowItemViewModel?> windowFor)
    {
        // Resolve the new assignment before detaching anything (windowFor may read the current zones).
        var assignments = Layout.Grid.Zones.Select(windowFor).ToList();
        foreach (var zone in Zones)
        {
            zone.Window = null;
        }

        Zones.Clear();
        for (var i = 0; i < Layout.Grid.Zones.Count; i++)
        {
            var zone = new ZoneViewModel(this, Layout.Grid.Zones[i], i);
            Zones.Add(zone);
            zone.Window = assignments[i];
        }

        RecalculateGeometry();
        OnPropertyChanged(nameof(Rows));
        OnPropertyChanged(nameof(Columns));
        OnPropertyChanged(nameof(GridLabel));
    }

    private void SyncHandles(ObservableCollection<BoundaryHandleViewModel> handles, LayoutEngine.Track[] tracks, bool isColumn, Rect contentRel)
    {
        var count = Math.Max(0, tracks.Length - 1);
        while (handles.Count > count)
        {
            handles.RemoveAt(handles.Count - 1);
        }

        while (handles.Count < count)
        {
            handles.Add(new BoundaryHandleViewModel(handles.Count, isColumn));
        }

        var bounds = Info.Bounds;
        for (var i = 0; i < count; i++)
        {
            var position = (tracks[i].End + tracks[i + 1].Start) / 2.0;
            handles[i].RelRect = isColumn
                ? new Rect((position - bounds.X) / bounds.Width, contentRel.Y, 0, contentRel.Height)
                : new Rect(contentRel.X, (position - bounds.Y) / bounds.Height, contentRel.Width, 0);
            handles[i].Tooltip = isColumn
                ? $"Arrastra para cambiar el ancho · {tracks[i].Length} px | {tracks[i + 1].Length} px"
                : $"Arrastra para cambiar el alto · {tracks[i].Length} px / {tracks[i + 1].Length} px";
        }
    }

    private Rect ToRelative(PixelRect r)
    {
        var b = Info.Bounds;
        return new Rect(
            (r.X - b.X) / (double)b.Width,
            (r.Y - b.Y) / (double)b.Height,
            r.Width / (double)b.Width,
            r.Height / (double)b.Height);
    }
}
