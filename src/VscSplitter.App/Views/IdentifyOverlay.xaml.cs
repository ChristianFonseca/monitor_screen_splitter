using System.Windows;
using System.Windows.Threading;
using VscSplitter.App.Services;
using VscSplitter.App.ViewModels;
using VscSplitter.Core.Models;

namespace VscSplitter.App.Views;

/// <summary>Big monitor number shown for a few seconds in the centre of each screen (like Windows' "Identify").</summary>
public partial class IdentifyOverlay : Window
{
    public IdentifyOverlay(MonitorViewModel monitor)
    {
        InitializeComponent();
        DataContext = monitor;

        var bounds = monitor.Info.Bounds;
        var scale = monitor.Info.Dpi / 96.0;
        var width = (int)Math.Round(Width * scale);
        var height = (int)Math.Round(Height * scale);
        OverlayWindow.Configure(this, new PixelRect(
            bounds.X + (bounds.Width - width) / 2,
            bounds.Y + (bounds.Height - height) / 2,
            width,
            height));

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Close();
        };
        Loaded += (_, _) => timer.Start();
    }
}
