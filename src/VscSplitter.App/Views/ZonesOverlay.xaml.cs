using System.Windows;
using VscSplitter.App.Services;
using VscSplitter.App.ViewModels;

namespace VscSplitter.App.Views;

/// <summary>Click-through layer that draws a monitor's zones on the real screen; updates live while editing.</summary>
public partial class ZonesOverlay : Window
{
    public ZonesOverlay(MonitorViewModel monitor)
    {
        InitializeComponent();
        DataContext = monitor;
        OverlayWindow.Configure(this, monitor.Info.Bounds);
    }
}
