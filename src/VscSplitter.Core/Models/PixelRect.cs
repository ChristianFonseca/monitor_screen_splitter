using System.Text.Json.Serialization;

namespace VscSplitter.Core.Models;

/// <summary>Integer rectangle in physical screen pixels (virtual-desktop coordinates).</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    [JsonIgnore] public int Right => X + Width;

    [JsonIgnore] public int Bottom => Y + Height;

    [JsonIgnore] public long Area => (long)Math.Max(0, Width) * Math.Max(0, Height);

    [JsonIgnore] public bool IsEmpty => Width <= 0 || Height <= 0;

    public static PixelRect FromLtrb(int left, int top, int right, int bottom) =>
        new(left, top, right - left, bottom - top);

    public bool Contains(int x, int y) => x >= X && x < Right && y >= Y && y < Bottom;

    public PixelRect Intersect(PixelRect other)
    {
        var left = Math.Max(X, other.X);
        var top = Math.Max(Y, other.Y);
        var right = Math.Min(Right, other.Right);
        var bottom = Math.Min(Bottom, other.Bottom);
        return right > left && bottom > top ? FromLtrb(left, top, right, bottom) : default;
    }

    public PixelRect Deflate(int amount)
    {
        var w = Math.Max(1, Width - 2 * amount);
        var h = Math.Max(1, Height - 2 * amount);
        return new PixelRect(X + Math.Min(amount, (Width - 1) / 2), Y + Math.Min(amount, (Height - 1) / 2), w, h);
    }

    public override string ToString() => $"{Width}×{Height} @ ({X}, {Y})";
}
