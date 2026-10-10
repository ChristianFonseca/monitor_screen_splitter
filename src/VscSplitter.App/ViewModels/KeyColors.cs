using System.Windows.Media;

namespace VscSplitter.App.ViewModels;

/// <summary>Stable colour per workspace name, so a window looks the same in the list and in its zone.</summary>
public static class KeyColors
{
    private static readonly Color[] Palette =
    [
        Color.FromRgb(0x60, 0xA5, 0xFA), Color.FromRgb(0xF4, 0x72, 0xB6), Color.FromRgb(0x34, 0xD3, 0x99),
        Color.FromRgb(0xFB, 0xBF, 0x24), Color.FromRgb(0xA7, 0x8B, 0xFA), Color.FromRgb(0xF8, 0x71, 0x71),
        Color.FromRgb(0x22, 0xD3, 0xEE), Color.FromRgb(0xA3, 0xE6, 0x35), Color.FromRgb(0xFB, 0x92, 0x3C),
        Color.FromRgb(0x81, 0x8C, 0xF8), Color.FromRgb(0x2D, 0xD4, 0xBF), Color.FromRgb(0xE8, 0x79, 0xF9),
        Color.FromRgb(0xFD, 0xA4, 0xAF), Color.FromRgb(0x93, 0xC5, 0xFD), Color.FromRgb(0xFC, 0xD3, 0x4D),
        Color.FromRgb(0x6E, 0xE7, 0xB7),
    ];

    public static Color For(string key)
    {
        // FNV-1a: string.GetHashCode() is randomised per process, this is stable across runs.
        var hash = 2166136261u;
        foreach (var ch in key.ToLowerInvariant())
        {
            hash ^= ch;
            hash *= 16777619u;
        }

        return Palette[hash % (uint)Palette.Length];
    }

    public static SolidColorBrush Brush(Color color, byte alpha)
    {
        var brush = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }
}
