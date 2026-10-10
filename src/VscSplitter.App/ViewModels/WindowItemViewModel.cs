using System.Windows.Media;
using VscSplitter.Core.Models;

namespace VscSplitter.App.ViewModels;

/// <summary>
/// A VS Code window. Live windows have a handle; a window remembered by the profile but not
/// currently open is a placeholder (<see cref="IsMissing"/>) that keeps its zone until it reappears.
/// </summary>
public sealed class WindowItemViewModel : ObservableObject
{
    private IntPtr _handle;
    private string _key;
    private string _title = "";
    private string _activeEditor = "";
    private bool _isMinimized;
    private string? _folderUri;
    private ZoneViewModel? _zone;
    private Brush _colorBrush = Brushes.Gray;
    private Brush _fillBrush = Brushes.Transparent;

    public WindowItemViewModel(string key)
    {
        _key = key;
        UpdateColors();
    }

    public IntPtr Handle
    {
        get => _handle;
        private set
        {
            if (SetProperty(ref _handle, value))
            {
                OnPropertyChanged(nameof(IsMissing));
                OnPropertyChanged(nameof(Subtitle));
            }
        }
    }

    public bool IsMissing => _handle == IntPtr.Zero;

    /// <summary>Folder / workspace name: the identity saved in profiles.</summary>
    public string Key
    {
        get => _key;
        private set
        {
            if (SetProperty(ref _key, value))
            {
                UpdateColors();
            }
        }
    }

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public string ActiveEditor
    {
        get => _activeEditor;
        private set
        {
            if (SetProperty(ref _activeEditor, value))
            {
                OnPropertyChanged(nameof(Subtitle));
            }
        }
    }

    public bool IsMinimized
    {
        get => _isMinimized;
        private set
        {
            if (SetProperty(ref _isMinimized, value))
            {
                OnPropertyChanged(nameof(Subtitle));
            }
        }
    }

    public PixelRect Bounds { get; private set; }

    public string? ProcessPath { get; private set; }

    public string? FolderUri
    {
        get => _folderUri;
        set
        {
            if (SetProperty(ref _folderUri, value))
            {
                OnPropertyChanged(nameof(Subtitle));
            }
        }
    }

    public ZoneViewModel? Zone
    {
        get => _zone;
        internal set
        {
            if (SetProperty(ref _zone, value))
            {
                OnPropertyChanged(nameof(IsAssigned));
                OnPropertyChanged(nameof(AssignmentLabel));
            }
        }
    }

    public bool IsAssigned => _zone is not null;

    public string AssignmentLabel => _zone is null ? "Sin asignar" : $"M{_zone.Monitor.Number} · {_zone.DisplayNumber}";

    public string Subtitle
    {
        get
        {
            if (IsMissing)
            {
                return _folderUri is null ? "No abierta" : "No abierta · se puede reabrir";
            }

            var editor = string.IsNullOrEmpty(_activeEditor) ? "Sin editor activo" : _activeEditor;
            return _isMinimized ? $"Minimizada · {editor}" : editor;
        }
    }

    public Brush ColorBrush
    {
        get => _colorBrush;
        private set => SetProperty(ref _colorBrush, value);
    }

    public Brush FillBrush
    {
        get => _fillBrush;
        private set => SetProperty(ref _fillBrush, value);
    }

    public void Update(EditorWindowInfo info)
    {
        Handle = info.Handle;
        Key = info.WorkspaceKey;
        Title = info.Title;
        ActiveEditor = info.ActiveEditor;
        IsMinimized = info.IsMinimized;
        Bounds = info.FrameBounds;
        ProcessPath = info.ProcessPath;
    }

    /// <summary>The window was closed while assigned: keep it as a placeholder in its zone.</summary>
    public void MarkClosed()
    {
        Handle = IntPtr.Zero;
        IsMinimized = false;
    }

    private void UpdateColors()
    {
        var color = KeyColors.For(_key);
        ColorBrush = KeyColors.Brush(color, 0xFF);
        FillBrush = KeyColors.Brush(color, 0x38);
    }
}
