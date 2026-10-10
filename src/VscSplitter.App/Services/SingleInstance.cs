namespace VscSplitter.App.Services;

/// <summary>
/// Keeps one running instance. A second launch (e.g. from the Start menu while the app sits in the tray)
/// signals the first one to show its window and exits.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activate;
    private readonly RegisteredWaitHandle? _registration;

    public SingleInstance(string id)
    {
        _mutex = new Mutex(initiallyOwned: true, $@"Local\{id}.Instance", out var createdNew);
        IsFirstInstance = createdNew;
        _activate = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\{id}.Activate");
        if (IsFirstInstance)
        {
            _registration = ThreadPool.RegisterWaitForSingleObject(
                _activate,
                (_, _) => ActivationRequested?.Invoke(this, EventArgs.Empty),
                null,
                Timeout.Infinite,
                executeOnlyOnce: false);
        }
    }

    public event EventHandler? ActivationRequested;

    public bool IsFirstInstance { get; }

    public void SignalFirstInstance() => _activate.Set();

    public void Dispose()
    {
        _registration?.Unregister(null);
        _activate.Dispose();
        if (IsFirstInstance)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Not owned by this thread any more; the OS releases it on exit.
            }
        }

        _mutex.Dispose();
    }
}
