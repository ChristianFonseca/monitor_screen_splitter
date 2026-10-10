using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using VscSplitter.Core.Layout;
using VscSplitter.Core.Models;
using VscSplitter.Core.Services;

namespace VscSplitter.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    /// <summary>VS Code restores its own window bounds right after creating a window; wait before moving it.</summary>
    private static readonly TimeSpan AutoPlaceDelay = TimeSpan.FromMilliseconds(1200);

    private readonly ConfigStore _store;
    private readonly WindowService _windowService = new();
    private readonly DispatcherTimer _saveTimer;
    private readonly DispatcherTimer _pollTimer;
    private readonly DispatcherTimer _liveApplyTimer;
    private readonly DispatcherTimer _displayChangeTimer;
    private readonly HashSet<MonitorViewModel> _liveApplyPending = [];
    private readonly Dictionary<IntPtr, DateTime> _autoPlaceQueue = [];

    private AppConfig _config = new();
    private Profile _profile = new();
    private IReadOnlyList<WorkspaceEntry> _catalog = [];
    private IReadOnlyList<MonitorInfo> _monitorInfos = [];
    private MonitorViewModel? _selectedMonitor;
    private string _selectedProfileName = "";
    private string _searchText = "";
    private bool _onlyUnassigned;
    private string _statusText = "Listo.";
    private string _windowsSummary = "";
    private bool _isBusy;
    private bool _showZoneOverlay;
    private double _desktopAspect = 16.0 / 9.0;
    private bool _startWithWindows;
    private bool _suppressProfileSwitch;
    private bool _initialized;

    public MainViewModel(ConfigStore store)
    {
        _store = store;

        WindowsView = CollectionViewSource.GetDefaultView(Windows);
        WindowsView.Filter = FilterWindow;
        WindowsView.SortDescriptions.Add(new SortDescription(nameof(WindowItemViewModel.IsMissing), ListSortDirection.Ascending));
        WindowsView.SortDescriptions.Add(new SortDescription(nameof(WindowItemViewModel.IsAssigned), ListSortDirection.Ascending));
        WindowsView.SortDescriptions.Add(new SortDescription(nameof(WindowItemViewModel.Key), ListSortDirection.Ascending));
        if (WindowsView is ICollectionViewLiveShaping live)
        {
            live.IsLiveSorting = true;
            live.LiveSortingProperties.Add(nameof(WindowItemViewModel.IsMissing));
            live.LiveSortingProperties.Add(nameof(WindowItemViewModel.IsAssigned));
            live.LiveSortingProperties.Add(nameof(WindowItemViewModel.Key));
            live.IsLiveFiltering = true;
            live.LiveFilteringProperties.Add(nameof(WindowItemViewModel.IsAssigned));
            live.LiveFilteringProperties.Add(nameof(WindowItemViewModel.IsMissing));
            live.LiveFilteringProperties.Add(nameof(WindowItemViewModel.Key));
        }

        Presets = LayoutPresets.All.Select(p => new PresetViewModel(p)).ToList();

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            SaveNow();
        };
        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        _pollTimer.Tick += (_, _) => PollWindows();
        _liveApplyTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        _liveApplyTimer.Tick += (_, _) =>
        {
            _liveApplyTimer.Stop();
            FlushLiveApply();
        };
        _displayChangeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        _displayChangeTimer.Tick += (_, _) =>
        {
            _displayChangeTimer.Stop();
            ReloadMonitors();
        };

        RefreshCommand = new RelayCommand(RefreshAll);
        ApplyCommand = new RelayCommand(() => _ = ApplyAllAsync(), () => !IsBusy);
        ApplyMonitorCommand = new RelayCommand(
            () =>
            {
                if (SelectedMonitor is { } monitor)
                {
                    _ = ApplyAsync([monitor]);
                }
            },
            () => !IsBusy && SelectedMonitor is not null);
        CaptureCommand = new RelayCommand(() => CaptureCurrentPositions(), () => !IsBusy);
        FillEmptyCommand = new RelayCommand(FillEmptyZones);
        ClearMonitorCommand = new RelayCommand(ClearSelectedMonitor, () => SelectedMonitor is not null);
        OpenMissingCommand = new RelayCommand(() => _ = OpenMissingAsync(), () => !IsBusy);
        IdentifyCommand = new RelayCommand(() => IdentifyRequested?.Invoke(this, EventArgs.Empty));
        NewProfileCommand = new RelayCommand(NewProfile);
        DuplicateProfileCommand = new RelayCommand(DuplicateProfile);
        RenameProfileCommand = new RelayCommand(RenameProfile);
        DeleteProfileCommand = new RelayCommand(DeleteProfile, () => _config.Profiles.Count > 1);
        ApplyPresetCommand = new RelayCommand(p =>
        {
            if (p is PresetViewModel preset)
            {
                ApplyPreset(preset);
            }
        });
        AddColumnCommand = new RelayCommand(() => ChangeGrid(0, 1), () => SelectedMonitor is { Columns: < LayoutEngine.MaxTracks });
        RemoveColumnCommand = new RelayCommand(() => ChangeGrid(0, -1), () => SelectedMonitor is { Columns: > 1 });
        AddRowCommand = new RelayCommand(() => ChangeGrid(1, 0), () => SelectedMonitor is { Rows: < LayoutEngine.MaxTracks });
        RemoveRowCommand = new RelayCommand(() => ChangeGrid(-1, 0), () => SelectedMonitor is { Rows: > 1 });
        EqualizeCommand = new RelayCommand(() => SelectedMonitor?.EqualizeTracks(), () => SelectedMonitor is not null);
        MergeCommand = new RelayCommand(MergeSelectedZones, () => SelectedMonitor?.Zones.Count(z => z.IsSelected) >= 2);
        SplitCommand = new RelayCommand(SplitSelectedZones, () => SelectedMonitor?.Zones.Any(z => z.IsSelected && z.IsMerged) == true);
        UnassignZoneCommand = new RelayCommand(p =>
        {
            if (p is ZoneViewModel { Window: { } window })
            {
                UnassignWindow(window);
            }
        });
        UnassignSelectedCommand = new RelayCommand(UnassignSelectedZones);
        FocusWindowCommand = new RelayCommand(p =>
        {
            if (p is WindowItemViewModel { IsMissing: false } window)
            {
                WindowPlacer.BringToFront(window.Handle);
            }
        });
        SaveCommand = new RelayCommand(() =>
        {
            SaveNow();
            StatusText = $"Perfil «{_profile.Name}» guardado.";
        });
    }

    public event EventHandler? IdentifyRequested;

    /// <summary>Monitors were (re)created; overlays bound to the old ones must be rebuilt.</summary>
    public event EventHandler? MonitorsRebuilt;

    /// <summary>(title, initial value) → entered text, or null if cancelled. Provided by the view.</summary>
    public Func<string, string, string?>? PromptText { get; set; }

    /// <summary>Yes/no question. Provided by the view.</summary>
    public Func<string, bool>? Confirm { get; set; }

    public ObservableCollection<MonitorViewModel> Monitors { get; } = [];

    public ObservableCollection<WindowItemViewModel> Windows { get; } = [];

    public ICollectionView WindowsView { get; }

    public ObservableCollection<string> ProfileNames { get; } = [];

    public IReadOnlyList<PresetViewModel> Presets { get; }

    public RelayCommand RefreshCommand { get; }

    public RelayCommand ApplyCommand { get; }

    public RelayCommand ApplyMonitorCommand { get; }

    public RelayCommand CaptureCommand { get; }

    public RelayCommand FillEmptyCommand { get; }

    public RelayCommand ClearMonitorCommand { get; }

    public RelayCommand OpenMissingCommand { get; }

    public RelayCommand IdentifyCommand { get; }

    public RelayCommand NewProfileCommand { get; }

    public RelayCommand DuplicateProfileCommand { get; }

    public RelayCommand RenameProfileCommand { get; }

    public RelayCommand DeleteProfileCommand { get; }

    public RelayCommand ApplyPresetCommand { get; }

    public RelayCommand AddColumnCommand { get; }

    public RelayCommand RemoveColumnCommand { get; }

    public RelayCommand AddRowCommand { get; }

    public RelayCommand RemoveRowCommand { get; }

    public RelayCommand EqualizeCommand { get; }

    public RelayCommand MergeCommand { get; }

    public RelayCommand SplitCommand { get; }

    public RelayCommand UnassignZoneCommand { get; }

    public RelayCommand UnassignSelectedCommand { get; }

    public RelayCommand FocusWindowCommand { get; }

    public RelayCommand SaveCommand { get; }

    public MonitorViewModel? SelectedMonitor
    {
        get => _selectedMonitor;
        set
        {
            if (ReferenceEquals(_selectedMonitor, value))
            {
                return;
            }

            if (_selectedMonitor is not null)
            {
                _selectedMonitor.IsSelected = false;
                _selectedMonitor.ClearZoneSelection();
            }

            _selectedMonitor = value;
            if (value is not null)
            {
                value.IsSelected = true;
                if (_initialized)
                {
                    _config.Settings.SelectedMonitorId = value.Info.Id;
                    ScheduleSave();
                }
            }

            OnPropertyChanged();
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public string SelectedProfileName
    {
        get => _selectedProfileName;
        set
        {
            if (value is null || value == _selectedProfileName)
            {
                return;
            }

            _selectedProfileName = value;
            OnPropertyChanged();
            if (!_suppressProfileSwitch)
            {
                SwitchProfile(value);
            }
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? ""))
            {
                WindowsView.Refresh();
            }
        }
    }

    public bool OnlyUnassigned
    {
        get => _onlyUnassigned;
        set
        {
            if (SetProperty(ref _onlyUnassigned, value))
            {
                WindowsView.Refresh();
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string WindowsSummary
    {
        get => _windowsSummary;
        private set => SetProperty(ref _windowsSummary, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool ShowZoneOverlay
    {
        get => _showZoneOverlay;
        set => SetProperty(ref _showZoneOverlay, value);
    }

    /// <summary>Width / height of the bounding box of all monitors (for the overview).</summary>
    public double DesktopAspect
    {
        get => _desktopAspect;
        private set => SetProperty(ref _desktopAspect, value);
    }

    public string ConfigPath => _store.FilePath;

    /// <summary>Config path shortened with %APPDATA% (keeps the user name out of the status bar).</summary>
    public string ConfigPathDisplay
    {
        get
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return _store.FilePath.StartsWith(appData, StringComparison.OrdinalIgnoreCase)
                ? "%APPDATA%" + _store.FilePath[appData.Length..]
                : Path.GetFileName(_store.FilePath);
        }
    }

    public bool AutoPlaceEnabled
    {
        get => _config.Settings.AutoPlaceNewWindows;
        set
        {
            if (_config.Settings.AutoPlaceNewWindows == value)
            {
                return;
            }

            _config.Settings.AutoPlaceNewWindows = value;
            if (!value)
            {
                _autoPlaceQueue.Clear();
            }

            OnPropertyChanged();
            ScheduleSave();
        }
    }

    public bool LiveApply
    {
        get => _config.Settings.LiveApply;
        set
        {
            if (_config.Settings.LiveApply == value)
            {
                return;
            }

            _config.Settings.LiveApply = value;
            OnPropertyChanged();
            ScheduleSave();
        }
    }

    public bool ApplyOnStartup
    {
        get => _config.Settings.ApplyOnStartup;
        set
        {
            if (_config.Settings.ApplyOnStartup == value)
            {
                return;
            }

            _config.Settings.ApplyOnStartup = value;
            OnPropertyChanged();
            ScheduleSave();
        }
    }

    public bool MinimizeToTray
    {
        get => _config.Settings.MinimizeToTray;
        set
        {
            if (_config.Settings.MinimizeToTray == value)
            {
                return;
            }

            _config.Settings.MinimizeToTray = value;
            OnPropertyChanged();
            ScheduleSave();
        }
    }

    public bool StartWithWindows
    {
        get => _startWithWindows;
        set
        {
            if (_startWithWindows == value)
            {
                return;
            }

            try
            {
                StartupManager.SetEnabled(value, Environment.ProcessPath ?? "VscSplitter.exe");
                _startWithWindows = value;
                StatusText = value
                    ? "Se iniciará con Windows (en la bandeja) y colocará las ventanas al abrirse."
                    : "Ya no se iniciará con Windows.";
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
            {
                StatusText = $"No se pudo cambiar el inicio automático: {ex.Message}";
            }

            OnPropertyChanged();
        }
    }

    public string ProcessNamesText
    {
        get => string.Join(", ", _config.Settings.ProcessNames);
        set
        {
            var names = (value ?? "")
                .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (names.Count == 0)
            {
                names = ["Code"];
            }

            _config.Settings.ProcessNames = names;
            OnPropertyChanged();
            ScheduleSave();
            PollWindows();
        }
    }

    public WindowPlacementSettings? MainWindowPlacement
    {
        get => _config.Settings.MainWindow;
        set
        {
            _config.Settings.MainWindow = value;
            ScheduleSave();
        }
    }

    private IReadOnlyList<string> ProcessNames => _config.Settings.ProcessNames;

    public void Initialize()
    {
        var firstRun = !_store.Exists;
        _config = _store.Load();
        _profile = FindProfile(_config.ActiveProfile) ?? _config.Profiles[0];
        _startWithWindows = SafeIsStartupEnabled();

        _suppressProfileSwitch = true;
        foreach (var profile in _config.Profiles)
        {
            ProfileNames.Add(profile.Name);
        }

        SelectedProfileName = _profile.Name;
        _suppressProfileSwitch = false;

        _catalog = WorkspaceCatalog.Load();
        MergeWindowSnapshot(_windowService.GetEditorWindows(ProcessNames), allowAutoPlace: false);
        BuildMonitors(MonitorService.GetMonitors());
        _initialized = true;

        if (firstRun && Windows.Any(w => !w.IsMissing))
        {
            var captured = CaptureCurrentPositions(quiet: true);
            StatusText = $"Bienvenido. Se asignaron {captured} ventanas según su posición actual: revisa las zonas y pulsa «Aplicar distribución» para alinearlas.";
            SaveNow();
        }
        else
        {
            StatusText = $"{Windows.Count(w => !w.IsMissing)} ventanas de VS Code en {Monitors.Count} monitores · perfil «{_profile.Name}».";
        }

        UpdateSummary();
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        _pollTimer.Start();
        OnPropertyChanged(string.Empty);
    }

    public void Shutdown()
    {
        _pollTimer.Stop();
        _liveApplyTimer.Stop();
        _displayChangeTimer.Stop();
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        if (_saveTimer.IsEnabled)
        {
            _saveTimer.Stop();
        }

        SaveNow();
    }

    public void SaveNow()
    {
        if (!_initialized)
        {
            return;
        }

        try
        {
            SyncProfileFromViewModels();
            _profile.UpdatedUtc = DateTime.UtcNow;
            _config.ActiveProfile = _profile.Name;
            _store.Save(_config);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText = $"No se pudo guardar la configuración: {ex.Message}";
        }
    }

    public Task ApplyAllAsync() => ApplyAsync(Monitors.ToList());

    public async Task ApplyAsync(IReadOnlyCollection<MonitorViewModel> monitors, bool quiet = false)
    {
        var jobs = monitors
            .SelectMany(m => m.Zones)
            .Where(z => z.Window is { IsMissing: false })
            .Select(z => (z.Window!, z.PixelRect))
            .ToList();
        if (jobs.Count == 0)
        {
            if (!quiet)
            {
                StatusText = "No hay ventanas abiertas asignadas a zonas. Arrastra ventanas a las zonas primero.";
            }

            return;
        }

        await PlaceAsync(jobs, auto: false, quiet);
    }

    public void SelectMonitor(MonitorViewModel monitor) => SelectedMonitor = monitor;

    public void ToggleZoneSelection(ZoneViewModel zone, bool additive)
    {
        SelectedMonitor = zone.Monitor;
        if (additive)
        {
            zone.IsSelected = !zone.IsSelected;
        }
        else
        {
            var onlyThis = zone.IsSelected && zone.Monitor.Zones.Count(z => z.IsSelected) == 1;
            zone.Monitor.ClearZoneSelection();
            zone.IsSelected = !onlyThis;
        }

        CommandManager.InvalidateRequerySuggested();
    }

    public void ClearZoneSelection()
    {
        SelectedMonitor?.ClearZoneSelection();
        CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>Drag &amp; drop: puts <paramref name="window"/> in <paramref name="target"/>; an occupant swaps places.</summary>
    public void DropWindowOnZone(WindowItemViewModel window, ZoneViewModel target)
    {
        var source = window.Zone;
        if (ReferenceEquals(source, target))
        {
            return;
        }

        var displaced = target.Window;
        target.Window = window;
        var message = $"«{window.Key}» → monitor {target.Monitor.Number}, zona {target.DisplayNumber}.";
        if (displaced is not null && !ReferenceEquals(displaced, window))
        {
            if (source is not null)
            {
                source.Window = displaced;
                message += $" «{displaced.Key}» pasó a la zona {source.DisplayNumber}.";
            }
            else if (displaced.IsMissing)
            {
                Windows.Remove(displaced);
            }
            else
            {
                message += $" «{displaced.Key}» quedó sin asignar.";
            }
        }

        StatusText = message;
        AfterAssignmentChange(target.Monitor, source?.Monitor);
    }

    public void UnassignWindow(WindowItemViewModel window)
    {
        var zone = window.Zone;
        if (zone is null)
        {
            return;
        }

        zone.Window = null;
        if (window.IsMissing)
        {
            Windows.Remove(window);
        }

        StatusText = $"«{window.Key}» se quitó de la zona {zone.DisplayNumber} del monitor {zone.Monitor.Number}.";
        AfterAssignmentChange(zone.Monitor, null);
    }

    public void ClearDragOver()
    {
        foreach (var zone in Monitors.SelectMany(m => m.Zones))
        {
            zone.IsDragOver = false;
        }
    }

    public void OnBoundaryDragCompleted(MonitorViewModel monitor)
    {
        StatusText = $"Monitor {monitor.Number}: tamaños ajustados.";
    }

    private void PollWindows()
    {
        if (!_initialized)
        {
            return;
        }

        try
        {
            MergeWindowSnapshot(_windowService.GetEditorWindows(ProcessNames), allowAutoPlace: true);
            ProcessAutoPlaceQueue();
            UpdateSummary();
        }
        catch (Exception ex)
        {
            StatusText = $"Error al detectar ventanas: {ex.Message}";
        }
    }

    /// <summary>
    /// Reconciles the window list with a fresh snapshot: updates known windows (by handle), adds new ones,
    /// turns closed-but-assigned windows into placeholders and reconnects placeholders when their
    /// workspace reappears. Returns how many windows are new.
    /// </summary>
    private int MergeWindowSnapshot(IReadOnlyList<EditorWindowInfo> snapshot, bool allowAutoPlace)
    {
        var known = Windows.Where(w => !w.IsMissing).ToDictionary(w => w.Handle);
        var seen = new HashSet<IntPtr>();
        var added = 0;
        foreach (var info in snapshot)
        {
            seen.Add(info.Handle);
            if (!known.TryGetValue(info.Handle, out var window))
            {
                window = new WindowItemViewModel(info.WorkspaceKey);
                Windows.Add(window);
                added++;
            }

            window.Update(info);
        }

        foreach (var window in known.Values.Where(w => !seen.Contains(w.Handle)))
        {
            _autoPlaceQueue.Remove(window.Handle);
            if (window.IsAssigned)
            {
                window.MarkClosed();
            }
            else
            {
                Windows.Remove(window);
            }
        }

        if (added > 0)
        {
            _catalog = WorkspaceCatalog.Load();
        }

        foreach (var window in Windows.Where(w => w.FolderUri is null))
        {
            window.FolderUri = FindUri(window.Key);
        }

        foreach (var placeholder in Windows.Where(w => w.IsMissing && w.IsAssigned).ToList())
        {
            var live = Windows.FirstOrDefault(w => !w.IsMissing && !w.IsAssigned && KeysMatch(w.Key, placeholder.Key));
            if (live is null)
            {
                continue;
            }

            var zone = placeholder.Zone!;
            zone.Window = live;
            Windows.Remove(placeholder);
            if (allowAutoPlace && AutoPlaceEnabled)
            {
                _autoPlaceQueue[live.Handle] = DateTime.UtcNow + AutoPlaceDelay;
                StatusText = $"«{live.Key}» apareció: se colocará en el monitor {zone.Monitor.Number}, zona {zone.DisplayNumber}.";
            }
        }

        return added;
    }

    private void ProcessAutoPlaceQueue()
    {
        if (_autoPlaceQueue.Count == 0 || IsBusy)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var due = _autoPlaceQueue.Where(kv => kv.Value <= now).Select(kv => kv.Key).ToList();
        if (due.Count == 0)
        {
            return;
        }

        foreach (var handle in due)
        {
            _autoPlaceQueue.Remove(handle);
        }

        var jobs = Windows
            .Where(w => !w.IsMissing && w.Zone is not null && due.Contains(w.Handle))
            .Select(w => (w, w.Zone!.PixelRect))
            .ToList();
        if (jobs.Count > 0)
        {
            _ = PlaceAsync(jobs, auto: true, quiet: false);
        }
    }

    private async Task PlaceAsync(List<(WindowItemViewModel Window, PixelRect Rect)> jobs, bool auto, bool quiet)
    {
        IsBusy = true;
        try
        {
            var work = jobs.Select(j => (j.Window.Handle, j.Rect, j.Window.Key)).ToList();
            var results = await Task.Run(() => work.Select(j => (j.Key, Result: WindowPlacer.Place(j.Handle, j.Rect))).ToList());
            var failed = results.Where(r => !r.Result.Success).ToList();
            var adjusted = results.Count(r => r.Result.Success && !r.Result.Exact);
            var placed = results.Count - failed.Count;

            var text = auto
                ? $"Auto-colocadas {placed} {(placed == 1 ? "ventana" : "ventanas")}."
                : $"✓ {placed} {(placed == 1 ? "ventana colocada" : "ventanas colocadas")} en su zona.";
            if (adjusted > 0)
            {
                text += $" {adjusted} no aceptaron el tamaño exacto (zona más pequeña que el mínimo de VS Code).";
            }

            if (failed.Count > 0)
            {
                text += $" {failed.Count} fallaron: " + string.Join("; ", failed.Select(f => $"{f.Key}: {f.Result.Error}"));
            }

            if (!quiet || failed.Count > 0)
            {
                StatusText = text;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RefreshAll()
    {
        _catalog = WorkspaceCatalog.Load();
        var monitors = MonitorService.GetMonitors();
        var monitorsChanged = !monitors.SequenceEqual(_monitorInfos);
        if (monitorsChanged)
        {
            SyncProfileFromViewModels();
            BuildMonitors(monitors);
        }

        var added = MergeWindowSnapshot(_windowService.GetEditorWindows(ProcessNames), allowAutoPlace: false);
        UpdateSummary();

        var live = Windows.Count(w => !w.IsMissing);
        var unassigned = Windows.Count(w => !w.IsMissing && !w.IsAssigned);
        var missing = Windows.Count(w => w.IsMissing);
        StatusText = $"Revisión completa: {live} ventanas abiertas ({added} nuevas), {unassigned} sin asignar" +
                     (missing > 0 ? $", {missing} del perfil no abiertas" : "") +
                     (monitorsChanged ? ". Los monitores cambiaron y se actualizaron." : ".");
    }

    private void ReloadMonitors()
    {
        var monitors = MonitorService.GetMonitors();
        if (monitors.SequenceEqual(_monitorInfos))
        {
            return;
        }

        SyncProfileFromViewModels();
        BuildMonitors(monitors);
        UpdateSummary();
        ScheduleSave();
        StatusText = "Cambió la configuración de pantallas: monitores actualizados.";
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) =>
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            _displayChangeTimer.Stop();
            _displayChangeTimer.Start();
        });

    /// <summary>Recreates the monitor view models from the active profile and re-assigns windows by workspace key.</summary>
    private void BuildMonitors(IReadOnlyList<MonitorInfo> infos)
    {
        var selectedId = _selectedMonitor?.Info.Id ?? _config.Settings.SelectedMonitorId;
        foreach (var monitor in Monitors)
        {
            monitor.GeometryEdited -= OnMonitorGeometryEdited;
            foreach (var zone in monitor.Zones)
            {
                zone.Window = null;
            }
        }

        foreach (var placeholder in Windows.Where(w => w.IsMissing).ToList())
        {
            Windows.Remove(placeholder);
        }

        _selectedMonitor = null;
        Monitors.Clear();
        _monitorInfos = infos;

        var layouts = MonitorMatcher.Match(infos, _profile.Monitors);
        for (var i = 0; i < infos.Count; i++)
        {
            var layout = layouts[i];
            if (layout is null)
            {
                layout = CreateDefaultLayout(infos[i]);
                _profile.Monitors.Add(layout);
            }

            var monitor = new MonitorViewModel(infos[i], layout);
            monitor.GeometryEdited += OnMonitorGeometryEdited;
            Monitors.Add(monitor);
        }

        UpdateOverviewGeometry();
        AssignFromProfile();

        SelectedMonitor = Monitors.FirstOrDefault(m => m.Info.Id == selectedId)
                          ?? Monitors.FirstOrDefault(m => m.AssignedWindows.Any())
                          ?? Monitors.FirstOrDefault();
        MonitorsRebuilt?.Invoke(this, EventArgs.Empty);
    }

    private void AssignFromProfile()
    {
        foreach (var zone in Monitors.SelectMany(m => m.Zones))
        {
            var key = zone.Definition.WindowKey;
            if (string.IsNullOrEmpty(key))
            {
                continue;
            }

            var live = Windows.FirstOrDefault(w => !w.IsMissing && !w.IsAssigned && KeysMatch(w.Key, key));
            if (live is not null)
            {
                zone.Window = live;
                continue;
            }

            var placeholder = new WindowItemViewModel(key)
            {
                Title = zone.Definition.WindowTitle ?? key,
                FolderUri = zone.Definition.FolderUri ?? FindUri(key),
            };
            Windows.Add(placeholder);
            zone.Window = placeholder;
        }
    }

    private void SyncProfileFromViewModels()
    {
        foreach (var monitor in Monitors)
        {
            var layout = monitor.Layout;
            layout.MonitorId = monitor.Info.Id;
            layout.DeviceName = monitor.Info.DeviceName;
            layout.FriendlyName = monitor.Info.FriendlyName;
            layout.Bounds = monitor.Info.Bounds;
            foreach (var zone in monitor.Zones)
            {
                zone.Definition.WindowKey = zone.Window?.Key;
                zone.Definition.WindowTitle = zone.Window?.Title;
                zone.Definition.FolderUri = zone.Window?.FolderUri;
            }
        }
    }

    private static MonitorLayout CreateDefaultLayout(MonitorInfo info) => new()
    {
        MonitorId = info.Id,
        DeviceName = info.DeviceName,
        FriendlyName = info.FriendlyName,
        Bounds = info.Bounds,
        // Large monitors start as 3 × 2 (six windows), smaller ones as two columns.
        Grid = info.Bounds.Width >= 2560 ? LayoutEngine.CreateGrid(2, 3) : LayoutEngine.CreateGrid(1, 2),
    };

    private void UpdateOverviewGeometry()
    {
        if (Monitors.Count == 0)
        {
            return;
        }

        var left = Monitors.Min(m => m.Info.Bounds.X);
        var top = Monitors.Min(m => m.Info.Bounds.Y);
        var right = Monitors.Max(m => m.Info.Bounds.Right);
        var bottom = Monitors.Max(m => m.Info.Bounds.Bottom);
        double width = Math.Max(1, right - left);
        double height = Math.Max(1, bottom - top);
        DesktopAspect = width / height;
        foreach (var monitor in Monitors)
        {
            var b = monitor.Info.Bounds;
            monitor.OverviewRect = new Rect((b.X - left) / width, (b.Y - top) / height, b.Width / width, b.Height / height);
        }
    }

    /// <summary>Assigns every visible window to the zone it currently overlaps most.</summary>
    private int CaptureCurrentPositions(bool quiet = false)
    {
        MergeWindowSnapshot(_windowService.GetEditorWindows(ProcessNames), allowAutoPlace: false);
        var zones = Monitors.SelectMany(m => m.Zones).ToList();
        var windows = Windows.Where(w => !w.IsMissing && !w.IsMinimized).ToList();
        var assignment = ZoneCapture.Assign(zones.Select(z => z.PixelRect).ToList(), windows.Select(w => w.Bounds).ToList());

        foreach (var window in windows)
        {
            if (window.Zone is { } zone)
            {
                zone.Window = null;
            }
        }

        var captured = 0;
        for (var i = 0; i < windows.Count; i++)
        {
            if (assignment[i] < 0)
            {
                continue;
            }

            var zone = zones[assignment[i]];
            if (zone.Window is { IsMissing: true } placeholder)
            {
                zone.Window = null;
                Windows.Remove(placeholder);
            }

            zone.Window = windows[i];
            captured++;
        }

        UpdateSummary();
        ScheduleSave();
        if (!quiet)
        {
            var left = windows.Count - captured;
            StatusText = $"Capturadas {captured} ventanas en la zona que ocupan ahora" +
                         (left > 0 ? $"; {left} no encajan en ninguna zona libre" : "") +
                         ". Pulsa «Aplicar distribución» para alinearlas al píxel.";
        }

        return captured;
    }

    private void FillEmptyZones()
    {
        var waiting = new Queue<WindowItemViewModel>(Windows
            .Where(w => !w.IsMissing && !w.IsAssigned)
            .OrderBy(w => MonitorIndexOf(w.Bounds))
            .ThenBy(w => w.Bounds.Y / 200)
            .ThenBy(w => w.Bounds.X));
        if (waiting.Count == 0)
        {
            StatusText = "No hay ventanas sin asignar.";
            return;
        }

        var monitors = Monitors.OrderBy(m => ReferenceEquals(m, SelectedMonitor) ? 0 : 1).ToList();
        var filled = 0;
        var touched = new HashSet<MonitorViewModel>();
        foreach (var zone in monitors.SelectMany(m => m.Zones).Where(z => z.Window is null))
        {
            if (waiting.Count == 0)
            {
                break;
            }

            zone.Window = waiting.Dequeue();
            touched.Add(zone.Monitor);
            filled++;
        }

        StatusText = filled == 0
            ? "No quedan zonas vacías. Añade columnas/filas o elige otra distribución."
            : $"Se asignaron {filled} ventanas a zonas vacías" + (waiting.Count > 0 ? $"; {waiting.Count} siguen sin zona." : ".");
        AfterAssignmentChange(touched.ToArray());
    }

    private void ClearSelectedMonitor()
    {
        if (SelectedMonitor is not { } monitor)
        {
            return;
        }

        foreach (var zone in monitor.Zones)
        {
            if (zone.Window is { } window)
            {
                zone.Window = null;
                if (window.IsMissing)
                {
                    Windows.Remove(window);
                }
            }
        }

        StatusText = $"Monitor {monitor.Number}: todas las zonas vaciadas.";
        AfterAssignmentChange(monitor);
    }

    private void UnassignSelectedZones()
    {
        if (SelectedMonitor is not { } monitor)
        {
            return;
        }

        foreach (var zone in monitor.Zones.Where(z => z.IsSelected && z.Window is not null).ToList())
        {
            UnassignWindow(zone.Window!);
        }
    }

    private async Task OpenMissingAsync()
    {
        _catalog = WorkspaceCatalog.Load();
        var missing = Monitors.SelectMany(m => m.Zones)
            .Select(z => z.Window)
            .OfType<WindowItemViewModel>()
            .Where(w => w.IsMissing)
            .ToList();
        if (missing.Count == 0)
        {
            StatusText = "Todas las ventanas del perfil están abiertas.";
            return;
        }

        var executable = VsCodeLauncher.FindExecutable(Windows.Select(w => w.ProcessPath));
        if (executable is null)
        {
            StatusText = "No se encontró Code.exe. Abre VS Code una vez y vuelve a intentarlo.";
            return;
        }

        IsBusy = true;
        var opened = 0;
        var unknown = new List<string>();
        var failed = new List<string>();
        try
        {
            foreach (var window in missing)
            {
                var uri = window.FolderUri ?? FindUri(window.Key);
                if (uri is null)
                {
                    unknown.Add(window.Key);
                    continue;
                }

                window.FolderUri = uri;
                try
                {
                    VsCodeLauncher.Open(executable, uri);
                    opened++;
                    await Task.Delay(400);
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
                {
                    failed.Add($"{window.Key} ({ex.Message})");
                }
            }
        }
        finally
        {
            IsBusy = false;
        }

        var text = $"Abriendo {opened} {(opened == 1 ? "ventana" : "ventanas")} en VS Code.";
        text += AutoPlaceEnabled
            ? " Se colocarán solas al aparecer."
            : " Activa «Auto-colocar» o pulsa «Aplicar» cuando aparezcan.";
        if (unknown.Count > 0)
        {
            text += $" Sin ruta conocida: {string.Join(", ", unknown)}.";
        }

        if (failed.Count > 0)
        {
            text += $" Errores: {string.Join("; ", failed)}.";
        }

        StatusText = text;
    }

    private void ApplyPreset(PresetViewModel preset)
    {
        if (SelectedMonitor is not { } monitor)
        {
            return;
        }

        var leftovers = monitor.ReplaceGrid(preset.Preset.Create());
        AfterLayoutChange(monitor, leftovers, $"Monitor {monitor.Number}: distribución «{preset.Preset.Name}».");
    }

    private void ChangeGrid(int deltaRows, int deltaColumns)
    {
        if (SelectedMonitor is not { } monitor)
        {
            return;
        }

        var old = monitor.Layout.Grid;
        var grid = LayoutEngine.CreateGrid(old.Rows + deltaRows, old.Columns + deltaColumns);
        if (deltaRows == 0)
        {
            grid.RowWeights = [.. old.RowWeights];
        }

        if (deltaColumns == 0)
        {
            grid.ColumnWeights = [.. old.ColumnWeights];
        }

        var leftovers = monitor.ReplaceGrid(grid);
        AfterLayoutChange(monitor, leftovers, $"Monitor {monitor.Number}: {monitor.GridLabel}.");
    }

    private void MergeSelectedZones()
    {
        if (SelectedMonitor is not { } monitor)
        {
            return;
        }

        var displaced = monitor.MergeSelected();
        if (displaced is null)
        {
            StatusText = "Para unir, selecciona con Ctrl+clic zonas contiguas que formen un rectángulo.";
            return;
        }

        AfterLayoutChange(monitor, displaced, $"Monitor {monitor.Number}: zonas unidas.");
    }

    private void SplitSelectedZones()
    {
        if (SelectedMonitor is not { } monitor)
        {
            return;
        }

        if (monitor.SplitSelected())
        {
            AfterLayoutChange(monitor, [], $"Monitor {monitor.Number}: zona separada en celdas.");
        }
    }

    private void AfterLayoutChange(MonitorViewModel monitor, IEnumerable<WindowItemViewModel> displaced, string message)
    {
        var unassigned = 0;
        foreach (var window in displaced)
        {
            if (window.IsMissing)
            {
                Windows.Remove(window);
            }
            else
            {
                unassigned++;
            }
        }

        StatusText = unassigned > 0 ? $"{message} {unassigned} ventanas quedaron sin zona." : message;
        AfterAssignmentChange(monitor);
        CommandManager.InvalidateRequerySuggested();
    }

    private void AfterAssignmentChange(params MonitorViewModel?[] monitors)
    {
        UpdateSummary();
        ScheduleSave();
        if (LiveApply)
        {
            QueueLiveApply(monitors);
        }
    }

    private void OnMonitorGeometryEdited(object? sender, EventArgs e)
    {
        ScheduleSave();
        if (LiveApply && sender is MonitorViewModel monitor)
        {
            QueueLiveApply(monitor);
        }
    }

    private void QueueLiveApply(params MonitorViewModel?[] monitors)
    {
        foreach (var monitor in monitors)
        {
            if (monitor is not null)
            {
                _liveApplyPending.Add(monitor);
            }
        }

        _liveApplyTimer.Stop();
        _liveApplyTimer.Start();
    }

    private void FlushLiveApply()
    {
        if (IsBusy)
        {
            _liveApplyTimer.Start();
            return;
        }

        var monitors = _liveApplyPending.Where(Monitors.Contains).ToList();
        _liveApplyPending.Clear();
        if (monitors.Count > 0)
        {
            _ = ApplyAsync(monitors, quiet: true);
        }
    }

    private void SwitchProfile(string name)
    {
        var target = FindProfile(name);
        if (target is null || ReferenceEquals(target, _profile))
        {
            return;
        }

        SyncProfileFromViewModels();
        _profile = target;
        _config.ActiveProfile = target.Name;
        BuildMonitors(MonitorService.GetMonitors());
        UpdateSummary();
        ScheduleSave();
        StatusText = $"Perfil «{name}» cargado. Pulsa «Aplicar distribución» para colocar las ventanas.";
        if (LiveApply)
        {
            _ = ApplyAllAsync();
        }
    }

    private void NewProfile()
    {
        var name = PromptText?.Invoke("Nombre del nuevo perfil", UniqueProfileName("Perfil"));
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        AddAndSelectProfile(new Profile { Name = UniqueProfileName(name.Trim()) });
    }

    private void DuplicateProfile()
    {
        var name = PromptText?.Invoke("Nombre de la copia", UniqueProfileName($"{_profile.Name} (copia)"));
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        SyncProfileFromViewModels();
        var copy = ConfigStore.Clone(_profile);
        copy.Name = UniqueProfileName(name.Trim());
        AddAndSelectProfile(copy);
    }

    private void AddAndSelectProfile(Profile profile)
    {
        _config.Profiles.Add(profile);
        ProfileNames.Add(profile.Name);
        SelectedProfileName = profile.Name;
        CommandManager.InvalidateRequerySuggested();
    }

    private void RenameProfile()
    {
        var name = PromptText?.Invoke("Nuevo nombre del perfil", _profile.Name)?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name == _profile.Name)
        {
            return;
        }

        if (_config.Profiles.Any(p => !ReferenceEquals(p, _profile) && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            StatusText = $"Ya existe un perfil llamado «{name}».";
            return;
        }

        var index = ProfileNames.IndexOf(_profile.Name);
        _profile.Name = name;
        _config.ActiveProfile = name;
        _suppressProfileSwitch = true;
        if (index >= 0)
        {
            ProfileNames[index] = name;
        }

        _selectedProfileName = "";
        SelectedProfileName = name;
        _suppressProfileSwitch = false;
        ScheduleSave();
        StatusText = $"Perfil renombrado a «{name}».";
    }

    private void DeleteProfile()
    {
        if (_config.Profiles.Count <= 1)
        {
            return;
        }

        if (Confirm?.Invoke($"¿Eliminar el perfil «{_profile.Name}»? Esta acción no se puede deshacer.") != true)
        {
            return;
        }

        var deleted = _profile;
        var next = _config.Profiles.First(p => !ReferenceEquals(p, deleted));
        SelectedProfileName = next.Name;
        _config.Profiles.Remove(deleted);
        ProfileNames.Remove(deleted.Name);
        ScheduleSave();
        StatusText = $"Perfil «{deleted.Name}» eliminado.";
        CommandManager.InvalidateRequerySuggested();
    }

    private string UniqueProfileName(string baseName)
    {
        var name = baseName;
        for (var i = 2; _config.Profiles.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)); i++)
        {
            name = $"{baseName} {i}";
        }

        return name;
    }

    private Profile? FindProfile(string name) =>
        _config.Profiles.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    private string? FindUri(string key) => WorkspaceCatalog.Find(_catalog, key)?.Uri;

    private static bool KeysMatch(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private int MonitorIndexOf(PixelRect bounds)
    {
        var cx = bounds.X + bounds.Width / 2;
        var cy = bounds.Y + bounds.Height / 2;
        for (var i = 0; i < Monitors.Count; i++)
        {
            if (Monitors[i].Info.Bounds.Contains(cx, cy))
            {
                return i;
            }
        }

        return int.MaxValue;
    }

    private bool FilterWindow(object item)
    {
        if (item is not WindowItemViewModel window)
        {
            return false;
        }

        if (_onlyUnassigned && (window.IsAssigned || window.IsMissing))
        {
            return false;
        }

        return _searchText.Length == 0 ||
               window.Key.Contains(_searchText, StringComparison.OrdinalIgnoreCase) ||
               window.Title.Contains(_searchText, StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateSummary()
    {
        var live = Windows.Count(w => !w.IsMissing);
        var unassigned = Windows.Count(w => !w.IsMissing && !w.IsAssigned);
        var missing = Windows.Count(w => w.IsMissing);
        WindowsSummary = $"{live} abiertas · {unassigned} sin asignar" + (missing > 0 ? $" · {missing} no abiertas" : "");
    }

    private void ScheduleSave()
    {
        if (!_initialized)
        {
            return;
        }

        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private static bool SafeIsStartupEnabled()
    {
        try
        {
            return StartupManager.IsEnabled();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            return false;
        }
    }
}
