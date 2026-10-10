using System.Windows;
using VscSplitter.Core.Models;

namespace VscSplitter.App.ViewModels;

public sealed class ZoneViewModel : ObservableObject
{
    private WindowItemViewModel? _window;
    private Rect _relRect;
    private PixelRect _pixelRect;
    private bool _isSelected;
    private bool _isDragOver;

    public ZoneViewModel(MonitorViewModel monitor, ZoneDefinition definition, int index)
    {
        Monitor = monitor;
        Definition = definition;
        Index = index;
    }

    public MonitorViewModel Monitor { get; }

    public ZoneDefinition Definition { get; }

    public int Index { get; }

    public int DisplayNumber => Index + 1;

    /// <summary>Position inside the monitor, as fractions of the monitor bounds.</summary>
    public Rect RelRect
    {
        get => _relRect;
        set => SetProperty(ref _relRect, value);
    }

    /// <summary>Exact screen rectangle the window will get.</summary>
    public PixelRect PixelRect
    {
        get => _pixelRect;
        set
        {
            if (SetProperty(ref _pixelRect, value))
            {
                OnPropertyChanged(nameof(SizeLabel));
                OnPropertyChanged(nameof(Tooltip));
            }
        }
    }

    public string SizeLabel => $"{_pixelRect.Width} × {_pixelRect.Height}";

    public string Tooltip => $"Zona {DisplayNumber}: {_pixelRect.Width}×{_pixelRect.Height} px en ({_pixelRect.X}, {_pixelRect.Y})";

    public bool IsMerged => Definition.RowSpan > 1 || Definition.ColumnSpan > 1;

    /// <summary>Assigned window. Keeps <see cref="WindowItemViewModel.Zone"/> in sync (a window lives in one zone).</summary>
    public WindowItemViewModel? Window
    {
        get => _window;
        set
        {
            if (ReferenceEquals(_window, value))
            {
                return;
            }

            if (_window is not null && ReferenceEquals(_window.Zone, this))
            {
                _window.Zone = null;
            }

            _window = value;
            if (_window is not null)
            {
                if (_window.Zone is not null && !ReferenceEquals(_window.Zone, this))
                {
                    _window.Zone.Window = null;
                }

                _window.Zone = this;
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(HasWindow));
        }
    }

    public bool HasWindow => _window is not null;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public bool IsDragOver
    {
        get => _isDragOver;
        set => SetProperty(ref _isDragOver, value);
    }
}
