using VscSplitter.Core.Models;

namespace VscSplitter.Core.Layout;

/// <summary>Pairs connected monitors with the layouts saved in a profile.</summary>
public static class MonitorMatcher
{
    /// <summary>
    /// Returns the saved layout for each monitor (same order), or null when none fits.
    /// Matching order: stable device id, then identical position + resolution, then GDI device name.
    /// </summary>
    public static IReadOnlyList<MonitorLayout?> Match(IReadOnlyList<MonitorInfo> monitors, IReadOnlyList<MonitorLayout> layouts)
    {
        var result = new MonitorLayout?[monitors.Count];
        var used = new HashSet<MonitorLayout>(ReferenceEqualityComparer.Instance);

        void Pass(Func<MonitorInfo, MonitorLayout, bool> predicate)
        {
            for (var i = 0; i < monitors.Count; i++)
            {
                if (result[i] is not null)
                {
                    continue;
                }

                foreach (var layout in layouts)
                {
                    if (used.Contains(layout) || !predicate(monitors[i], layout))
                    {
                        continue;
                    }

                    result[i] = layout;
                    used.Add(layout);
                    break;
                }
            }
        }

        Pass((m, l) => l.MonitorId.Length > 0 && string.Equals(m.Id, l.MonitorId, StringComparison.OrdinalIgnoreCase));
        Pass((m, l) => m.Bounds == l.Bounds);
        Pass((m, l) => l.DeviceName.Length > 0 && string.Equals(m.DeviceName, l.DeviceName, StringComparison.OrdinalIgnoreCase));
        return result;
    }
}
