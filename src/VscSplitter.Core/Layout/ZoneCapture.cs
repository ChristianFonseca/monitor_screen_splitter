using VscSplitter.Core.Models;

namespace VscSplitter.Core.Layout;

/// <summary>Finds which zone each window currently occupies, so a hand-made arrangement can be captured.</summary>
public static class ZoneCapture
{
    /// <summary>
    /// Returns, for every window, the index of the zone it overlaps best (intersection over union),
    /// or -1. Each zone receives at most one window; the best matches win.
    /// </summary>
    public static int[] Assign(IReadOnlyList<PixelRect> zones, IReadOnlyList<PixelRect> windows, double minScore = 0.25)
    {
        var result = Enumerable.Repeat(-1, windows.Count).ToArray();
        var candidates = new List<(int Window, int Zone, double Score)>();
        for (var w = 0; w < windows.Count; w++)
        {
            for (var z = 0; z < zones.Count; z++)
            {
                var overlap = windows[w].Intersect(zones[z]).Area;
                if (overlap == 0)
                {
                    continue;
                }

                var union = windows[w].Area + zones[z].Area - overlap;
                var score = union > 0 ? overlap / (double)union : 0;
                if (score >= minScore)
                {
                    candidates.Add((w, z, score));
                }
            }
        }

        var zoneTaken = new bool[zones.Count];
        foreach (var (window, zone, _) in candidates.OrderByDescending(c => c.Score))
        {
            if (result[window] >= 0 || zoneTaken[zone])
            {
                continue;
            }

            result[window] = zone;
            zoneTaken[zone] = true;
        }

        return result;
    }
}
