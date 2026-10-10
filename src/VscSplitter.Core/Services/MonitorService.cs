using System.Runtime.InteropServices;
using VscSplitter.Core.Models;
using VscSplitter.Core.Native;

namespace VscSplitter.Core.Services;

public static class MonitorService
{
    /// <summary>All connected monitors, ordered left to right (then top to bottom).</summary>
    public static IReadOnlyList<MonitorInfo> GetMonitors()
    {
        using var dpiScope = DpiAwarenessScope.PerMonitorV2();
        var names = DisplayConfig.GetTargetNames();
        var monitors = new List<MonitorInfo>();

        Win32.MonitorEnumProc callback = (IntPtr hMonitor, IntPtr hdc, ref Win32.RECT rect, IntPtr data) =>
        {
            var info = new Win32.MONITORINFOEX { cbSize = Marshal.SizeOf<Win32.MONITORINFOEX>() };
            if (!Win32.GetMonitorInfo(hMonitor, ref info))
            {
                return true;
            }

            var dpi = 96u;
            try
            {
                if (Win32.GetDpiForMonitor(hMonitor, Win32.MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0)
                {
                    dpi = dpiX;
                }
            }
            catch (DllNotFoundException)
            {
            }

            names.TryGetValue(info.szDevice, out var target);
            monitors.Add(new MonitorInfo
            {
                Id = string.IsNullOrEmpty(target.DevicePath) ? info.szDevice : target.DevicePath,
                DeviceName = info.szDevice,
                FriendlyName = string.IsNullOrWhiteSpace(target.FriendlyName) ? "Monitor genérico" : target.FriendlyName,
                Number = ParseDisplayNumber(info.szDevice),
                Bounds = ToPixelRect(info.rcMonitor),
                WorkArea = ToPixelRect(info.rcWork),
                Dpi = (int)dpi,
                IsPrimary = (info.dwFlags & Win32.MONITORINFOF_PRIMARY) != 0,
            });
            return true;
        };

        Win32.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
        GC.KeepAlive(callback);

        return monitors.OrderBy(m => m.Bounds.X).ThenBy(m => m.Bounds.Y).ToList();
    }

    public static PixelRect ToPixelRect(Win32.RECT r) => PixelRect.FromLtrb(r.Left, r.Top, r.Right, r.Bottom);

    internal static int ParseDisplayNumber(string deviceName)
    {
        var end = deviceName.Length;
        var start = end;
        while (start > 0 && char.IsDigit(deviceName[start - 1]))
        {
            start--;
        }

        return int.TryParse(deviceName.AsSpan(start, end - start), out var number) ? number : 0;
    }
}
