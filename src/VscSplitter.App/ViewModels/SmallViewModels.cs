using System.Windows;
using VscSplitter.Core.Layout;
using VscSplitter.Core.Models;

namespace VscSplitter.App.ViewModels;

/// <summary>Draggable grip on the boundary between two columns (or rows).</summary>
public sealed class BoundaryHandleViewModel : ObservableObject
{
    private Rect _relRect;
    private string _tooltip = "";
    private bool _isDragging;

    public BoundaryHandleViewModel(int index, bool isColumn)
    {
        Index = index;
        IsColumn = isColumn;
    }

    /// <summary>The boundary sits after track <see cref="Index"/>.</summary>
    public int Index { get; }

    public bool IsColumn { get; }

    public Rect RelRect
    {
        get => _relRect;
        set => SetProperty(ref _relRect, value);
    }

    public string Tooltip
    {
        get => _tooltip;
        set => SetProperty(ref _tooltip, value);
    }

    public bool IsDragging
    {
        get => _isDragging;
        set => SetProperty(ref _isDragging, value);
    }
}

public sealed class PresetViewModel
{
    public PresetViewModel(LayoutPreset preset)
    {
        Preset = preset;
        var grid = preset.Create();
        var area = new PixelRect(0, 0, 1600, 900);
        Rects = LayoutEngine.ComputeZoneRects(grid, area, 80, 0)
            .Select(r => new Rect(r.X / 1600.0, r.Y / 900.0, r.Width / 1600.0, r.Height / 900.0))
            .ToList();
        Tooltip = $"{preset.Name} · {grid.Zones.Count} {(grid.Zones.Count == 1 ? "zona" : "zonas")}";
    }

    public LayoutPreset Preset { get; }

    public IReadOnlyList<Rect> Rects { get; }

    public string Tooltip { get; }
}
