using System.Windows;
using Forms = System.Windows.Forms;

namespace VscSplitter.App.Services;

/// <summary>Notification-area icon so the app can keep auto-placing windows in the background.</summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private bool _backgroundNoticeShown;

    public TrayIcon(Action show, Action apply, Action openMissing, Func<bool> getAutoPlace, Action<bool> setAutoPlace, Action exit)
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Abrir VSC Splitter", null, (_, _) => show());
        menu.Items.Add("Aplicar distribución", null, (_, _) => apply());
        menu.Items.Add("Abrir ventanas faltantes", null, (_, _) => openMissing());
        var autoPlace = new Forms.ToolStripMenuItem("Auto-colocar ventanas al abrirse") { CheckOnClick = true };
        autoPlace.CheckedChanged += (_, _) => setAutoPlace(autoPlace.Checked);
        menu.Items.Add(autoPlace);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Salir", null, (_, _) => exit());
        menu.Opening += (_, _) => autoPlace.Checked = getAutoPlace();

        _icon = new Forms.NotifyIcon
        {
            Text = "VSC Splitter",
            Icon = LoadIcon(),
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
            {
                show();
            }
        };
    }

    public void NotifyRunningInBackground()
    {
        if (_backgroundNoticeShown)
        {
            return;
        }

        _backgroundNoticeShown = true;
        _icon.ShowBalloonTip(
            4000,
            "VSC Splitter sigue activo",
            "Continúa en la bandeja colocando las ventanas de VS Code. Usa «Salir» en su menú para cerrarlo.",
            Forms.ToolTipIcon.Info);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
    }

    private static System.Drawing.Icon LoadIcon()
    {
        var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"));
        if (resource?.Stream is { } stream)
        {
            using (stream)
            {
                return new System.Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
            }
        }

        return System.Drawing.SystemIcons.Application;
    }
}
