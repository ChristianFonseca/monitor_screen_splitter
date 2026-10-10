using System.Runtime.InteropServices;
using System.Text;
using VscSplitter.Core.Models;
using VscSplitter.Core.Native;

namespace VscSplitter.Core.Services;

/// <summary>Enumerates the top-level windows that belong to the configured editor processes.</summary>
public sealed class WindowService
{
    private readonly Dictionary<uint, string?> _processPaths = [];

    public IReadOnlyList<EditorWindowInfo> GetEditorWindows(IEnumerable<string> processNames)
    {
        using var dpiScope = DpiAwarenessScope.PerMonitorV2();
        var names = new HashSet<string>(
            processNames.Select(n => n.Trim()).Where(n => n.Length > 0),
            StringComparer.OrdinalIgnoreCase);
        var windows = new List<EditorWindowInfo>();
        var seenProcesses = new HashSet<uint>();

        Win32.EnumWindowsProc callback = (hwnd, _) =>
        {
            if (!Win32.IsWindowVisible(hwnd) || Win32.GetWindow(hwnd, Win32.GW_OWNER) != IntPtr.Zero)
            {
                return true;
            }

            if ((Win32.GetWindowLong(hwnd, Win32.GWL_EXSTYLE) & Win32.WS_EX_TOOLWINDOW) != 0 || IsCloaked(hwnd))
            {
                return true;
            }

            Win32.GetWindowThreadProcessId(hwnd, out var pid);
            seenProcesses.Add(pid);
            var path = GetProcessPath(pid);
            var processName = path is null ? "" : Path.GetFileNameWithoutExtension(path);
            if (!names.Contains(processName))
            {
                return true;
            }

            var title = GetTitle(hwnd);
            if (string.IsNullOrWhiteSpace(title))
            {
                return true;
            }

            var parsed = TitleParser.Parse(title);
            windows.Add(new EditorWindowInfo
            {
                Handle = hwnd,
                Title = title,
                WorkspaceKey = parsed.WorkspaceKey,
                ActiveEditor = parsed.ActiveEditor,
                ProcessName = processName,
                ProcessPath = path,
                ProcessId = (int)pid,
                FrameBounds = GetFrameBounds(hwnd),
                IsMinimized = Win32.IsIconic(hwnd),
                IsMaximized = Win32.IsZoomed(hwnd),
            });
            return true;
        };

        Win32.EnumWindows(callback, IntPtr.Zero);
        GC.KeepAlive(callback);

        // Forget processes that no longer own visible windows (process ids get reused).
        foreach (var pid in _processPaths.Keys.Where(p => !seenProcesses.Contains(p)).ToList())
        {
            _processPaths.Remove(pid);
        }

        return windows;
    }

    public static string GetTitle(IntPtr hwnd)
    {
        var length = Win32.GetWindowTextLength(hwnd);
        if (length <= 0)
        {
            return "";
        }

        var buffer = new StringBuilder(length + 1);
        Win32.GetWindowText(hwnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    public static bool IsCloaked(IntPtr hwnd) =>
        Win32.DwmGetWindowAttribute(hwnd, Win32.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0;

    /// <summary>Visible bounds of the window: the DWM frame, without the invisible resize borders.</summary>
    public static PixelRect GetFrameBounds(IntPtr hwnd)
    {
        if (Win32.DwmGetWindowAttribute(hwnd, Win32.DWMWA_EXTENDED_FRAME_BOUNDS, out Win32.RECT frame, Marshal.SizeOf<Win32.RECT>()) == 0)
        {
            return MonitorService.ToPixelRect(frame);
        }

        return Win32.GetWindowRect(hwnd, out var rect) ? MonitorService.ToPixelRect(rect) : default;
    }

    private string? GetProcessPath(uint pid)
    {
        if (_processPaths.TryGetValue(pid, out var cached))
        {
            return cached;
        }

        string? path = null;
        var process = Win32.OpenProcess(Win32.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process != IntPtr.Zero)
        {
            try
            {
                var buffer = new StringBuilder(1024);
                var size = (uint)buffer.Capacity;
                if (Win32.QueryFullProcessImageName(process, 0, buffer, ref size))
                {
                    path = buffer.ToString(0, (int)size);
                }
            }
            finally
            {
                Win32.CloseHandle(process);
            }
        }

        _processPaths[pid] = path;
        return path;
    }
}
