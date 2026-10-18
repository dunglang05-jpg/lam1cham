using System.Windows;
using System.Windows.Controls;

namespace LamMotCham.App.Controls;

/// <summary>
/// Hai khối cạnh nhau (khối chính co giãn, khối phụ rộng <see cref="SideWidth"/>); khi không đủ chỗ
/// (hẹp hơn <see cref="StackBelow"/>) thì xếp khối phụ xuống dưới. <see cref="SideWidth"/> = 0: hai khối rộng bằng nhau.
/// </summary>
public sealed class SplitPanel : Panel
{
    public static readonly DependencyProperty SideWidthProperty = DependencyProperty.Register(
        nameof(SideWidth), typeof(double), typeof(SplitPanel), new FrameworkPropertyMetadata(320.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>Tỷ lệ bề rộng cột phụ (0–0.9). Khác 0 thì dùng thay cho SideWidth, ví dụ 0.4 = cột chính 60%, cột phụ 40%.</summary>
    public static readonly DependencyProperty SideRatioProperty = DependencyProperty.Register(
        nameof(SideRatio), typeof(double), typeof(SplitPanel), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double SideRatio
    {
        get => (double)GetValue(SideRatioProperty);
        set => SetValue(SideRatioProperty, value);
    }

    public static readonly DependencyProperty GapProperty = DependencyProperty.Register(
        nameof(Gap), typeof(double), typeof(SplitPanel), new FrameworkPropertyMetadata(18.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty StackBelowProperty = DependencyProperty.Register(
        nameof(StackBelow), typeof(double), typeof(SplitPanel), new FrameworkPropertyMetadata(860.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double SideWidth
    {
        get => (double)GetValue(SideWidthProperty);
        set => SetValue(SideWidthProperty, value);
    }

    public double Gap
    {
        get => (double)GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    public double StackBelow
    {
        get => (double)GetValue(StackBelowProperty);
        set => SetValue(StackBelowProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? StackBelow : availableSize.Width;
        var (main, side) = Pair();
        if (main is null)
        {
            return default;
        }

        if (side is null)
        {
            main.Measure(new Size(width, availableSize.Height));
            return new Size(width, main.DesiredSize.Height);
        }

        if (IsStacked(width))
        {
            main.Measure(new Size(width, double.PositiveInfinity));
            side.Measure(new Size(width, double.PositiveInfinity));
            return new Size(width, main.DesiredSize.Height + Gap + side.DesiredSize.Height);
        }

        var (mainWidth, sideWidth) = Widths(width);
        main.Measure(new Size(mainWidth, availableSize.Height));
        side.Measure(new Size(sideWidth, availableSize.Height));
        return new Size(width, Math.Max(main.DesiredSize.Height, side.DesiredSize.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var (main, side) = Pair();
        if (main is null)
        {
            return finalSize;
        }

        if (side is null)
        {
            main.Arrange(new Rect(0, 0, finalSize.Width, finalSize.Height));
            return finalSize;
        }

        if (IsStacked(finalSize.Width))
        {
            main.Arrange(new Rect(0, 0, finalSize.Width, main.DesiredSize.Height));
            side.Arrange(new Rect(0, main.DesiredSize.Height + Gap, finalSize.Width, side.DesiredSize.Height));
            return finalSize;
        }

        var (mainWidth, sideWidth) = Widths(finalSize.Width);
        main.Arrange(new Rect(0, 0, mainWidth, finalSize.Height));
        side.Arrange(new Rect(mainWidth + Gap, 0, sideWidth, finalSize.Height));
        return finalSize;
    }

    private bool IsStacked(double width) => width < StackBelow;

    private (double Main, double Side) Widths(double width)
    {
        var usable = Math.Max(0, width - Gap);
        if (SideRatio > 0)
        {
            var ratioSide = usable * Math.Min(SideRatio, 0.9);
            return (usable - ratioSide, ratioSide);
        }

        if (SideWidth <= 0)
        {
            return (usable / 2, usable / 2);
        }

        var side = Math.Min(SideWidth, usable / 2);
        return (usable - side, side);
    }

    private (UIElement? Main, UIElement? Side) Pair()
    {
        var visible = InternalChildren.Cast<UIElement>().Where(c => c.Visibility != Visibility.Collapsed).Take(2).ToList();
        return (visible.ElementAtOrDefault(0), visible.ElementAtOrDefault(1));
    }
}

/// <summary>
/// Lưới thẻ tự chọn số cột: mỗi cột rộng ít nhất <see cref="MinItemWidth"/>, tối đa <see cref="MaxColumns"/> cột.
/// </summary>
public sealed class AdaptiveGrid : Panel
{
    public static readonly DependencyProperty MinItemWidthProperty = DependencyProperty.Register(
        nameof(MinItemWidth), typeof(double), typeof(AdaptiveGrid), new FrameworkPropertyMetadata(200.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty MaxColumnsProperty = DependencyProperty.Register(
        nameof(MaxColumns), typeof(int), typeof(AdaptiveGrid), new FrameworkPropertyMetadata(4, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty GapProperty = DependencyProperty.Register(
        nameof(Gap), typeof(double), typeof(AdaptiveGrid), new FrameworkPropertyMetadata(14.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>Khoảng cách giữa các hàng; âm = dùng <see cref="Gap"/>.</summary>
    public static readonly DependencyProperty RowGapProperty = DependencyProperty.Register(
        nameof(RowGap), typeof(double), typeof(AdaptiveGrid), new FrameworkPropertyMetadata(-1.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double MinItemWidth
    {
        get => (double)GetValue(MinItemWidthProperty);
        set => SetValue(MinItemWidthProperty, value);
    }

    public int MaxColumns
    {
        get => (int)GetValue(MaxColumnsProperty);
        set => SetValue(MaxColumnsProperty, value);
    }

    public double Gap
    {
        get => (double)GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    public double RowGap
    {
        get => (double)GetValue(RowGapProperty);
        set => SetValue(RowGapProperty, value);
    }

    private double VerticalGap => RowGap >= 0 ? RowGap : Gap;

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? MinItemWidth * MaxColumns : availableSize.Width;
        var (columns, itemWidth) = Layout(width);
        var height = 0.0;
        var rowHeight = 0.0;
        var items = Visible();
        for (var i = 0; i < items.Count; i++)
        {
            items[i].Measure(new Size(itemWidth, double.PositiveInfinity));
            rowHeight = Math.Max(rowHeight, items[i].DesiredSize.Height);
            if (i % columns == columns - 1 || i == items.Count - 1)
            {
                height += rowHeight + (height > 0 ? VerticalGap : 0);
                rowHeight = 0;
            }
        }

        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var (columns, itemWidth) = Layout(finalSize.Width);
        var items = Visible();
        var y = 0.0;
        for (var start = 0; start < items.Count; start += columns)
        {
            var row = items.Skip(start).Take(columns).ToList();
            var rowHeight = row.Max(c => c.DesiredSize.Height);
            for (var j = 0; j < row.Count; j++)
            {
                row[j].Arrange(new Rect(j * (itemWidth + Gap), y, itemWidth, rowHeight));
            }

            y += rowHeight + VerticalGap;
        }

        return finalSize;
    }

    private (int Columns, double ItemWidth) Layout(double width)
    {
        var columns = (int)Math.Floor((width + Gap) / (MinItemWidth + Gap));
        columns = Math.Clamp(columns, 1, Math.Max(1, MaxColumns));
        var itemWidth = Math.Max(0, (width - (Gap * (columns - 1))) / columns);
        return (columns, itemWidth);
    }

    private List<UIElement> Visible() =>
        [.. InternalChildren.Cast<UIElement>().Where(c => c.Visibility != Visibility.Collapsed)];
}
