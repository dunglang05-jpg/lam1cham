using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace LamMotCham.App.Controls;

/// <summary>
/// Vòng tiến độ (thiết kế canvas: .ring) — vẽ vòng nền và cung theo phần trăm, chữ ở giữa.
/// Dùng chung (Tổng quan, Dọn dẹp, Bàn giao). Chỉ hiển thị số liệu do ViewModel cung cấp.
/// </summary>
public sealed class RingProgress : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(RingProgress), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(RingProgress), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RingBrushProperty = DependencyProperty.Register(
        nameof(RingBrush), typeof(Brush), typeof(RingProgress),
        new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromRgb(0x0E, 0xB6, 0x7D)), FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Brush TrackBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xEA, 0xF1, 0xFA)));
    private static readonly Brush InkBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x17, 0x29, 0x44)));
    private static readonly Typeface Face = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    public RingProgress()
    {
        Width = 66;
        Height = 66;
    }

    /// <summary>0–100.</summary>
    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public Brush RingBrush
    {
        get => (Brush)GetValue(RingBrushProperty);
        set => SetValue(RingBrushProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        const double thickness = 7;
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= thickness * 2)
        {
            return;
        }

        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var radius = (size - thickness) / 2;
        drawingContext.DrawEllipse(null, new Pen(TrackBrush, thickness), center, radius, radius);

        var value = Math.Clamp(double.IsNaN(Value) ? 0 : Value, 0, 100);
        if (value >= 99.95)
        {
            drawingContext.DrawEllipse(null, new Pen(RingBrush, thickness), center, radius, radius);
        }
        else if (value > 0)
        {
            var angle = value / 100 * 2 * Math.PI;
            var start = new Point(center.X, center.Y - radius);
            var end = new Point(center.X + radius * Math.Sin(angle), center.Y - radius * Math.Cos(angle));
            var figure = new PathFigure { StartPoint = start, IsClosed = false };
            figure.Segments.Add(new ArcSegment(end, new Size(radius, radius), 0, value > 50, SweepDirection.Clockwise, true));
            var geometry = new PathGeometry([figure]);
            drawingContext.DrawGeometry(null, new Pen(RingBrush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, geometry);
        }

        if (!string.IsNullOrEmpty(Text))
        {
            var text = new FormattedText(Text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Face, size * 0.24, InkBrush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            drawingContext.DrawText(text, new Point(center.X - text.Width / 2, center.Y - text.Height / 2));
        }
    }

    private static Brush Freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}
