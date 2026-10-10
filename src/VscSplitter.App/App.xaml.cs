using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using VscSplitter.App.Services;
using VscSplitter.App.ViewModels;
using VscSplitter.App.Views;
using VscSplitter.Core.Services;

namespace VscSplitter.App;

/// <summary>
/// Command line: <c>--tray</c> starts hidden in the notification area (used by "Iniciar con Windows"),
/// <c>--apply</c> applies the active profile right after starting, <c>--config &lt;file&gt;</c> uses another
/// configuration file (a separate instance per file).
/// </summary>
public partial class App : Application
{
    private SingleInstance? _singleInstance;
    private TrayIcon? _tray;
    private MainViewModel? _viewModel;
    private MainWindow? _mainWindow;
    private bool _exiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var configPath = GetOption(e.Args, "--config");
        if (configPath is not null)
        {
            configPath = Path.GetFullPath(configPath);
        }

        _singleInstance = new SingleInstance(configPath is null ? "VscSplitter" : $"VscSplitter-{StableHash(configPath.ToLowerInvariant()):x8}");
        if (!_singleInstance.IsFirstInstance)
        {
            _singleInstance.SignalFirstInstance();
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        _singleInstance.ActivationRequested += (_, _) => Dispatcher.InvokeAsync(ShowMainWindow);

        var startInTray = e.Args.Any(a => string.Equals(a, "--tray", StringComparison.OrdinalIgnoreCase));
        var applyNow = e.Args.Any(a => string.Equals(a, "--apply", StringComparison.OrdinalIgnoreCase));

        _viewModel = new MainViewModel(new ConfigStore(configPath));
        _viewModel.Initialize();

        _mainWindow = new MainWindow(_viewModel);
        _mainWindow.Closing += OnMainWindowClosing;

        var viewModel = _viewModel;
        _tray = new TrayIcon(
            show: ShowMainWindow,
            apply: () => _ = viewModel.ApplyAllAsync(),
            openMissing: () => viewModel.OpenMissingCommand.Execute(null),
            getAutoPlace: () => viewModel.AutoPlaceEnabled,
            setAutoPlace: value => viewModel.AutoPlaceEnabled = value,
            exit: ExitApplication);

        if (!startInTray)
        {
            ShowMainWindow();
        }

        if (applyNow || viewModel.ApplyOnStartup)
        {
            _ = ApplyAfterStartupAsync(viewModel);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    /// <summary>Value of <c>--name value</c> or <c>--name=value</c>.</summary>
    private static string? GetOption(string[] args, string name)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                return args[i + 1];
            }

            if (args[i].StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
            {
                return args[i][(name.Length + 1)..];
            }
        }

        return null;
    }

    private static uint StableHash(string text)
    {
        var hash = 2166136261u;
        foreach (var ch in text)
        {
            hash ^= ch;
            hash *= 16777619u;
        }

        return hash;
    }

    private static async Task ApplyAfterStartupAsync(MainViewModel viewModel)
    {
        // Right after logon VS Code may still be restoring its windows.
        await Task.Delay(TimeSpan.FromSeconds(2));
        await viewModel.ApplyAllAsync();
    }

    private void ShowMainWindow()
    {
        if (_mainWindow is null || _exiting)
        {
            return;
        }

        _mainWindow.Show();
        if (_mainWindow.WindowState == WindowState.Minimized)
        {
            _mainWindow.WindowState = WindowState.Normal;
        }

        _mainWindow.Activate();
    }

    private void OnMainWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_exiting)
        {
            return;
        }

        if (_viewModel?.MinimizeToTray == true)
        {
            e.Cancel = true;
            _mainWindow?.Hide();
            _tray?.NotifyRunningInBackground();
            return;
        }

        _exiting = true;
        Dispatcher.InvokeAsync(ShutdownApplication);
    }

    private void ExitApplication()
    {
        if (_exiting)
        {
            return;
        }

        _exiting = true;
        _mainWindow?.Close();
        ShutdownApplication();
    }

    private void ShutdownApplication()
    {
        _viewModel?.Shutdown();
        _tray?.Dispose();
        _tray = null;
        Shutdown();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VscSplitter");
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, "error.log"), $"[{DateTime.Now:O}] {e.Exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        MessageBox.Show(
            $"Ocurrió un error inesperado:\n\n{e.Exception.Message}\n\nEl detalle se guardó en %APPDATA%\\VscSplitter\\error.log.",
            "VSC Splitter",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        e.Handled = true;
    }
}
