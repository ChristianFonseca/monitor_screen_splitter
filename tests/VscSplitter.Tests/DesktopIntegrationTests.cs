using VscSplitter.Core.Models;
using VscSplitter.Core.Services;

namespace VscSplitter.Tests;

/// <summary>Talks to the real desktop. Creates its own short-lived test window; never touches other apps.</summary>
[Trait("Category", "Desktop")]
public class DesktopIntegrationTests
{
    [Fact]
    public void EnumeratesConnectedMonitors()
    {
        var monitors = MonitorService.GetMonitors();

        Assert.NotEmpty(monitors);
        Assert.Single(monitors, m => m.IsPrimary);
        Assert.All(monitors, m =>
        {
            Assert.False(m.Bounds.IsEmpty);
            Assert.True(m.Bounds.Contains(m.WorkArea.X, m.WorkArea.Y));
            Assert.True(m.Number > 0);
            Assert.False(string.IsNullOrEmpty(m.Id));
        });
    }

    [Fact]
    public void PlacesVisibleFrameExactlyDespiteInvisibleBorders()
    {
        var primary = MonitorService.GetMonitors().Single(m => m.IsPrimary);
        var target = new PixelRect(primary.WorkArea.X + 40, primary.WorkArea.Y + 40, 640, 420);
        PlacementResult result = default;
        PixelRect frameAfter = default;

        RunSta(() =>
        {
            using var form = new Form
            {
                Text = "VscSplitter placement test",
                StartPosition = FormStartPosition.Manual,
                Location = new Point(primary.WorkArea.X + 200, primary.WorkArea.Y + 200),
                Size = new Size(300, 200),
                ShowInTaskbar = false,
            };
            form.Show();
            Application.DoEvents();

            result = WindowPlacer.Place(form.Handle, target);
            Application.DoEvents();
            frameAfter = WindowService.GetFrameBounds(form.Handle);
            form.Close();
        });

        Assert.True(result.Success, result.Error);
        Assert.True(result.Exact);
        Assert.Equal(target, frameAfter);
    }

    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null)
        {
            throw new InvalidOperationException("STA action failed", error);
        }
    }
}
