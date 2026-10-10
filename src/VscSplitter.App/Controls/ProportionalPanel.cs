using System.Windows;
using System.Windows.Controls;

namespace VscSplitter.App.Controls;

/// <summary>
/// Arranges children by rectangles expressed as fractions (0..1) of the panel. Optionally keeps an aspect
/// ratio (letterboxed and centred), which is how monitors and zones are drawn to scale.
/// A zero width (or height) in <see cref="RectProperty"/> centres the child on X (or Y) at its desired size.
/// </summary>
public sealed class ProportionalPanel : Panel
{
    public static readonly DependencyProperty RectProperty = DependencyProperty.RegisterAttached(
        "Rect",
        typeof(Rect),
        typeof(ProportionalPanel),
        new FrameworkPropertyMetadata(new Rect(0, 0, 1, 1), FrameworkPropertyMetadataOptions.AffectsParentArrange));

    public static readonly DependencyProperty AspectRatioProperty = DependencyProperty.Register(
        nameof(AspectRatio),
        typeof(double),
        typeof(ProportionalPanel),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    /// <summary>Width / height to preserve; 0 stretches to fill.</summary>
    public double AspectRatio
    {
        get => (double)GetValue(AspectRatioProperty);
        set => SetValue(AspectRatioProperty, value);
    }

    public static Rect GetRect(DependencyObject element) => (Rect)element.GetValue(RectProperty);

    public static void SetRect(DependencyObject element, Rect value) => element.SetValue(RectProperty, value);

    protected override Size MeasureOverride(Size availableSize)
    {
        var size = new Size(
            double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height);
        var box = GetContentBox(size);
        foreach (UIElement child in InternalChildren)
        {
            var r = GetRect(child);
            child.Measure(new Size(
                r.Width > 0 ? r.Width * box.Width : double.PositiveInfinity,
                r.Height > 0 ? r.Height * box.Height : double.PositiveInfinity));
        }

        return size;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var box = GetContentBox(finalSize);
        foreach (UIElement child in InternalChildren)
        {
            var r = GetRect(child);
            var width = r.Width > 0 ? r.Width * box.Width : child.DesiredSize.Width;
            var height = r.Height > 0 ? r.Height * box.Height : child.DesiredSize.Height;
            var x = box.X + r.X * box.Width - (r.Width > 0 ? 0 : width / 2);
            var y = box.Y + r.Y * box.Height - (r.Height > 0 ? 0 : height / 2);
            child.Arrange(new Rect(x, y, Math.Max(0, width), Math.Max(0, height)));
        }

        return finalSize;
    }

    private Rect GetContentBox(Size size)
    {
        var aspect = AspectRatio;
        if (aspect <= 0 || double.IsNaN(aspect) || size.Width <= 0 || size.Height <= 0)
        {
            return new Rect(0, 0, size.Width, size.Height);
        }

        var width = size.Width;
        var height = width / aspect;
        if (height > size.Height)
        {
            height = size.Height;
            width = height * aspect;
        }

        return new Rect((size.Width - width) / 2, (size.Height - height) / 2, width, height);
    }
}
