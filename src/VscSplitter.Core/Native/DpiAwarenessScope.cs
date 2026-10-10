namespace VscSplitter.Core.Native;

/// <summary>
/// Temporarily switches the calling thread to Per-Monitor-V2 DPI awareness so every
/// coordinate read or written through Win32 is in physical pixels, whatever the host process declares.
/// </summary>
public readonly struct DpiAwarenessScope : IDisposable
{
    private readonly IntPtr _previous;

    private DpiAwarenessScope(IntPtr previous) => _previous = previous;

    public static DpiAwarenessScope PerMonitorV2()
    {
        try
        {
            return new DpiAwarenessScope(Win32.SetThreadDpiAwarenessContext(Win32.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2));
        }
        catch (EntryPointNotFoundException)
        {
            return default;
        }
    }

    public void Dispose()
    {
        if (_previous != IntPtr.Zero)
        {
            Win32.SetThreadDpiAwarenessContext(_previous);
        }
    }
}
