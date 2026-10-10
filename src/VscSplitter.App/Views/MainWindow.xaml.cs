using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using VscSplitter.App.Services;
using VscSplitter.App.ViewModels;
using VscSplitter.Core.Models;

namespace VscSplitter.App.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly List<Window> _zoneOverlays = [];
    private Point _pressPoint;
    private ZoneViewModel? _pressedZone;
    private WindowItemViewModel? _listDragCandidate;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _vm = viewModel;
        DataContext = viewModel;

        viewModel.PromptText = (prompt, initial) => InputDialog.Ask(this, prompt, initial);
        viewModel.Confirm = message =>
            MessageBox.Show(this, message, "VSC Splitter", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        viewModel.IdentifyRequested += (_, _) => IdentifyMonitors();
        viewModel.MonitorsRebuilt += (_, _) => RefreshZoneOverlays();
        viewModel.PropertyChanged += OnViewModelPropertyChanged;

        RestorePlacement();
        SourceInitialized += (_, _) => DarkTitleBar.Apply(this);
        Closing += (_, _) => SavePlacement();
        Closed += (_, _) => CloseZoneOverlays();
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible)
            {
                _vm.ShowZoneOverlay = false;
            }
        };
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.ShowZoneOverlay))
        {
            RefreshZoneOverlays();
        }
    }

    // ---------- Overlays ----------

    private void IdentifyMonitors()
    {
        foreach (var monitor in _vm.Monitors)
        {
            new IdentifyOverlay(monitor).Show();
        }
    }

    private void RefreshZoneOverlays()
    {
        CloseZoneOverlays();
        if (!_vm.ShowZoneOverlay)
        {
            return;
        }

        foreach (var monitor in _vm.Monitors)
        {
            var overlay = new ZonesOverlay(monitor);
            overlay.Show();
            _zoneOverlays.Add(overlay);
        }
    }

    private void CloseZoneOverlays()
    {
        foreach (var overlay in _zoneOverlays)
        {
            overlay.Close();
        }

        _zoneOverlays.Clear();
    }

    // ---------- Window placement of the app itself ----------

    private void RestorePlacement()
    {
        var saved = _vm.MainWindowPlacement;
        var desktop = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        if (saved is null || saved.Width < 600 || saved.Height < 400)
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            return;
        }

        var rect = new Rect(saved.Left, saved.Top, saved.Width, saved.Height);
        var visible = Rect.Intersect(desktop, rect);
        if (visible.IsEmpty || visible.Width < 200 || visible.Height < 150)
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            return;
        }

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = saved.Left;
        Top = saved.Top;
        Width = saved.Width;
        Height = saved.Height;
        if (saved.Maximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    private void SavePlacement()
    {
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;
        if (bounds.IsEmpty || double.IsNaN(bounds.Left))
        {
            return;
        }

        _vm.MainWindowPlacement = new WindowPlacementSettings
        {
            Left = bounds.Left,
            Top = bounds.Top,
            Width = bounds.Width,
            Height = bounds.Height,
            Maximized = WindowState == WindowState.Maximized,
        };
    }

    // ---------- Keyboard ----------

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is TextBox)
        {
            return;
        }

        if (e.Key == Key.Delete)
        {
            _vm.UnassignSelectedCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            _vm.ClearZoneSelection();
            e.Handled = true;
        }
    }

    // ---------- Monitor selection ----------

    private void OverviewMonitor_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is MonitorViewModel monitor)
        {
            _vm.SelectMonitor(monitor);
        }
    }

    private void DetailSurface_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Click on the monitor background (not on a zone): clear the zone selection.
        if ((e.OriginalSource as FrameworkElement)?.DataContext is not ZoneViewModel)
        {
            _vm.ClearZoneSelection();
        }
    }

    // ---------- Zones: click to select, drag to move the window ----------

    private void Zone_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _pressedZone = null;
        if (IsInsideButton(e.OriginalSource as DependencyObject, (DependencyObject)sender))
        {
            return;
        }

        _pressedZone = (sender as FrameworkElement)?.DataContext as ZoneViewModel;
        _pressPoint = e.GetPosition(this);
    }

    private void Zone_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _pressedZone is not { Window: { } window } ||
            !IsDragGesture(e.GetPosition(this)))
        {
            return;
        }

        _pressedZone = null;
        StartDrag((DependencyObject)sender, window);
    }

    private void Zone_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ZoneViewModel zone || !ReferenceEquals(zone, _pressedZone))
        {
            return;
        }

        _pressedZone = null;
        if (Equals((sender as FrameworkElement)?.Tag, "overview"))
        {
            _vm.SelectMonitor(zone.Monitor);
        }
        else
        {
            _vm.ToggleZoneSelection(zone, (Keyboard.Modifiers & ModifierKeys.Control) != 0);
        }

        e.Handled = true;
    }

    private void Zone_DragEnter(object sender, DragEventArgs e) => Zone_DragOver(sender, e);

    private void Zone_DragOver(object sender, DragEventArgs e)
    {
        var accepted = GetDraggedWindow(e) is not null;
        e.Effects = accepted ? DragDropEffects.Move : DragDropEffects.None;
        if (accepted && (sender as FrameworkElement)?.DataContext is ZoneViewModel zone)
        {
            zone.IsDragOver = true;
        }

        e.Handled = true;
    }

    private void Zone_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ZoneViewModel zone } element)
        {
            return;
        }

        // DragLeave also fires when moving onto a child element; only clear when really outside.
        var p = e.GetPosition(element);
        if (p.X > 0 && p.Y > 0 && p.X < element.ActualWidth && p.Y < element.ActualHeight)
        {
            return;
        }

        zone.IsDragOver = false;
    }

    private void Zone_Drop(object sender, DragEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ZoneViewModel zone && GetDraggedWindow(e) is { } window)
        {
            zone.IsDragOver = false;
            _vm.DropWindowOnZone(window, zone);
        }

        e.Handled = true;
    }

    // ---------- Window list ----------

    private void WindowList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _listDragCandidate = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext as WindowItemViewModel;
        _pressPoint = e.GetPosition(this);
    }

    private void WindowList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _listDragCandidate is not { } window || !IsDragGesture(e.GetPosition(this)))
        {
            return;
        }

        _listDragCandidate = null;
        StartDrag(WindowList, window);
    }

    private void WindowList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext is WindowItemViewModel window)
        {
            _vm.FocusWindowCommand.Execute(window);
        }
    }

    private void WindowList_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = GetDraggedWindow(e) is { IsAssigned: true } ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void WindowList_Drop(object sender, DragEventArgs e)
    {
        if (GetDraggedWindow(e) is { IsAssigned: true } window)
        {
            _vm.UnassignWindow(window);
        }

        e.Handled = true;
    }

    // ---------- Boundary grips ----------

    private void Handle_DragStarted(object sender, DragStartedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is BoundaryHandleViewModel handle)
        {
            handle.IsDragging = true;
        }
    }

    private void Handle_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not BoundaryHandleViewModel handle ||
            _vm.SelectedMonitor is not { } monitor ||
            DetailSurface.ActualWidth <= 0 || DetailSurface.ActualHeight <= 0)
        {
            return;
        }

        // Absolute mouse position: no drift while the grip itself moves.
        var p = Mouse.GetPosition(DetailSurface);
        if (handle.IsColumn)
        {
            monitor.MoveColumnBoundary(handle.Index, p.X / DetailSurface.ActualWidth);
        }
        else
        {
            monitor.MoveRowBoundary(handle.Index, p.Y / DetailSurface.ActualHeight);
        }
    }

    private void Handle_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is BoundaryHandleViewModel handle)
        {
            handle.IsDragging = false;
        }

        if (_vm.SelectedMonitor is { } monitor)
        {
            _vm.OnBoundaryDragCompleted(monitor);
        }
    }

    // ---------- Helpers ----------

    private void StartDrag(DependencyObject source, WindowItemViewModel window)
    {
        using var ghost = new DragGhost(this, window.Key, window.ColorBrush);

        void OnGiveFeedback(object s, GiveFeedbackEventArgs args) => ghost.Update();

        DragDrop.AddGiveFeedbackHandler(source, OnGiveFeedback);
        try
        {
            DragDrop.DoDragDrop(source, new DataObject(typeof(WindowItemViewModel), window), DragDropEffects.Move);
        }
        finally
        {
            DragDrop.RemoveGiveFeedbackHandler(source, OnGiveFeedback);
            _vm.ClearDragOver();
        }
    }

    private bool IsDragGesture(Point current) =>
        Math.Abs(current.X - _pressPoint.X) >= SystemParameters.MinimumHorizontalDragDistance ||
        Math.Abs(current.Y - _pressPoint.Y) >= SystemParameters.MinimumVerticalDragDistance;

    private static WindowItemViewModel? GetDraggedWindow(DragEventArgs e) =>
        e.Data.GetDataPresent(typeof(WindowItemViewModel))
            ? e.Data.GetData(typeof(WindowItemViewModel)) as WindowItemViewModel
            : null;

    private static T? FindAncestor<T>(DependencyObject? node)
        where T : DependencyObject
    {
        while (node is not null and not T)
        {
            node = GetParent(node);
        }

        return node as T;
    }

    private static bool IsInsideButton(DependencyObject? node, DependencyObject stopAt)
    {
        while (node is not null && !ReferenceEquals(node, stopAt))
        {
            if (node is ButtonBase)
            {
                return true;
            }

            node = GetParent(node);
        }

        return false;
    }

    private static DependencyObject? GetParent(DependencyObject node) =>
        node is Visual or System.Windows.Media.Media3D.Visual3D
            ? VisualTreeHelper.GetParent(node)
            : LogicalTreeHelper.GetParent(node);
}
