using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using VscSplitter.Core.Models;
using VscSplitter.Core.Native;

namespace VscSplitter.App.Services;

internal static class DarkTitleBar
{
    public static void Apply(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        var enabled = 1;
        Win32.DwmSetWindowAttribute(hwnd, Win32.DWMWA_USE_IMMERSIVE_DARK_MODE, ref enabled, sizeof(int));
    }
}

/// <summary>Turns a borderless WPF window into a click-through, non-activating, topmost overlay.</summary>
internal static class OverlayWindow
{
    /// <param name="bounds">Physical-pixel rectangle the overlay must cover.</param>
    public static void Configure(Window window, PixelRect bounds)
    {
        window.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            var exStyle = Win32.GetWindowLong(hwnd, Win32.GWL_EXSTYLE);
            Win32.SetWindowLong(hwnd, Win32.GWL_EXSTYLE,
                exStyle | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TRANSPARENT | Win32.WS_EX_LAYERED);
            Place(hwnd, bounds);

            // Moving to a monitor with another scale makes WPF resize the window; put it back.
            HwndSource.FromHwnd(hwnd)?.AddHook(Hook);

            IntPtr Hook(IntPtr h, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
            {
                if (message == Win32.WM_DPICHANGED)
                {
                    window.Dispatcher.BeginInvoke(() => Place(hwnd, bounds));
                }

                return IntPtr.Zero;
            }
        };
        window.Loaded += (_, _) => Place(new WindowInteropHelper(window).Handle, bounds);
    }

    private static void Place(IntPtr hwnd, PixelRect bounds)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        using var dpiScope = DpiAwarenessScope.PerMonitorV2();
        Win32.SetWindowPos(hwnd, Win32.HWND_TOPMOST, bounds.X, bounds.Y, bounds.Width, bounds.Height, Win32.SWP_NOACTIVATE);
    }
}

/// <summary>Small label that follows the cursor during drag &amp; drop.</summary>
internal sealed class DragGhost : IDisposable
{
    private readonly Popup _popup;
    private readonly Visual _reference;

    public DragGhost(Visual reference, string text, Brush accent)
    {
        _reference = reference;
        _popup = new Popup
        {
            Placement = PlacementMode.Absolute,
            AllowsTransparency = true,
            IsHitTestVisible = false,
            Focusable = false,
            Child = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xF0, 0x23, 0x23, 0x29)),
                BorderBrush = accent,
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 5, 10, 5),
                Child = new TextBlock
                {
                    Text = text,
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 13,
                },
            },
        };
        Update();
        _popup.IsOpen = true;
    }

    public void Update()
    {
        if (!Win32.GetCursorPos(out var cursor))
        {
            return;
        }

        // Offset so the popup never sits under the cursor (it would hide the drop target).
        var point = new Point(cursor.X + 16, cursor.Y + 18);
        if (PresentationSource.FromVisual(_reference)?.CompositionTarget is { } target)
        {
            point = target.TransformFromDevice.Transform(point);
        }

        _popup.HorizontalOffset = point.X;
        _popup.VerticalOffset = point.Y;
    }

    public void Dispose() => _popup.IsOpen = false;
}
