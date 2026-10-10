using System.Runtime.InteropServices;
using VscSplitter.Core.Models;
using VscSplitter.Core.Native;

namespace VscSplitter.Core.Services;

public readonly record struct PlacementResult(bool Success, bool Exact, PixelRect Actual, string? Error)
{
    public static PlacementResult Fail(string error) => new(false, false, default, error);
}

/// <summary>Moves and resizes windows so their <em>visible</em> frame matches a target rectangle exactly.</summary>
public static class WindowPlacer
{
    private const uint MoveFlags = Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE | Win32.SWP_NOOWNERZORDER;

    /// <summary>
    /// Windows 10/11 windows have invisible resize borders (7px at 100%) on the left, right and bottom.
    /// Positioning with SetWindowPos alone leaves visible gaps/overlaps, so the borders are measured
    /// (window rect vs. DWM frame) and added to the target. Repeats when a DPI change alters the borders.
    /// </summary>
    public static PlacementResult Place(IntPtr hwnd, PixelRect frame)
    {
        using var dpiScope = DpiAwarenessScope.PerMonitorV2();
        if (hwnd == IntPtr.Zero || !Win32.IsWindow(hwnd))
        {
            return PlacementResult.Fail("La ventana ya no existe.");
        }

        if (Win32.IsIconic(hwnd) || Win32.IsZoomed(hwnd))
        {
            Win32.ShowWindow(hwnd, Win32.SW_SHOWNOACTIVATE);
            if (Win32.IsIconic(hwnd) || Win32.IsZoomed(hwnd))
            {
                Win32.ShowWindow(hwnd, Win32.SW_RESTORE);
            }
        }

        var actual = default(PixelRect);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var (left, top, right, bottom) = GetInvisibleBorders(hwnd);
            var ok = Win32.SetWindowPos(
                hwnd,
                IntPtr.Zero,
                frame.X - left,
                frame.Y - top,
                frame.Width + left + right,
                frame.Height + top + bottom,
                MoveFlags);
            if (!ok)
            {
                var error = Marshal.GetLastWin32Error();
                return PlacementResult.Fail(error == 5
                    ? "Acceso denegado (¿VS Code se ejecuta como administrador?)."
                    : $"Error de Windows {error}.");
            }

            actual = WindowService.GetFrameBounds(hwnd);
            if (actual == frame)
            {
                return new PlacementResult(true, true, actual, null);
            }
        }

        // The window refused the exact size (e.g. minimum window size); report where it ended up.
        return new PlacementResult(true, false, actual, null);
    }

    public static void BringToFront(IntPtr hwnd)
    {
        if (!Win32.IsWindow(hwnd))
        {
            return;
        }

        if (Win32.IsIconic(hwnd))
        {
            Win32.ShowWindow(hwnd, Win32.SW_RESTORE);
        }

        Win32.SetForegroundWindow(hwnd);
    }

    private static (int Left, int Top, int Right, int Bottom) GetInvisibleBorders(IntPtr hwnd)
    {
        if (!Win32.GetWindowRect(hwnd, out var window) ||
            Win32.DwmGetWindowAttribute(hwnd, Win32.DWMWA_EXTENDED_FRAME_BOUNDS, out Win32.RECT frame, Marshal.SizeOf<Win32.RECT>()) != 0)
        {
            return default;
        }

        return (Math.Max(0, frame.Left - window.Left),
                Math.Max(0, frame.Top - window.Top),
                Math.Max(0, window.Right - frame.Right),
                Math.Max(0, window.Bottom - frame.Bottom));
    }
}
