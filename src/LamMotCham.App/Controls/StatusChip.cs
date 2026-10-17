using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LamMotCham.App.Controls;

/// <summary>
/// Nhãn trạng thái: luôn có icon + chữ, không dùng màu đơn độc (UI/UX Handoff mục 6, 14).
/// </summary>
public sealed class StatusChip : Border
{
    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State), typeof(string), typeof(StatusChip), new PropertyMetadata("pending", (d, _) => ((StatusChip)d).Update()));

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(StatusChip), new PropertyMetadata(null, (d, _) => ((StatusChip)d).Update()));

    private static readonly Brush GrayBg = Frozen("#EEF1F5");
    private static readonly Brush GrayFg = Frozen("#3F4A5C");
    private static readonly Brush BlueBg = Frozen("#EAF1FF");
    private static readonly Brush BlueFg = Frozen("#1D55CC");
    private static readonly Brush GreenBg = Frozen("#E8F5EC");
    private static readonly Brush GreenFg = Frozen("#146C34");
    private static readonly Brush RedBg = Frozen("#FDECEC");
    private static readonly Brush RedFg = Frozen("#A61F1F");
    private static readonly Brush AmberBg = Frozen("#FDF4E3");
    private static readonly Brush AmberFg = Frozen("#7A4F0F");
    private static readonly Brush InfoBg = Frozen("#E6F1F8");
    private static readonly Brush InfoFg = Frozen("#0B5A8A");

    private static readonly Dictionary<string, (string Glyph, Brush Bg, Brush Fg, string Label)> States = new(StringComparer.Ordinal)
    {
        ["pending"] = ("", GrayBg, GrayFg, "Chờ"),
        ["running"] = ("", BlueBg, BlueFg, "Đang chạy"),
        ["success"] = ("", GreenBg, GreenFg, "Thành công"),
        ["failed"] = ("", RedBg, RedFg, "Thất bại"),
        ["skipped"] = ("", GrayBg, GrayFg, "Đã bỏ qua"),
        ["attention"] = ("", AmberBg, AmberFg, "Cần chú ý"),
        ["cancelled"] = ("", GrayBg, GrayFg, "Đã hủy"),
        ["reboot"] = ("", AmberBg, AmberFg, "Chờ khởi động lại"),
        ["nottested"] = ("", GrayBg, GrayFg, "Chưa kiểm thử"),
        ["notsupported"] = ("", GrayBg, GrayFg, "Không hỗ trợ"),
        ["pass"] = ("", GreenBg, GreenFg, "Đạt"),
        ["unchecked"] = ("", GrayBg, GrayFg, "Chưa kiểm tra"),
        ["manual"] = ("", GrayBg, GrayFg, "Cần kiểm tra tay"),
        ["na"] = ("", GrayBg, GrayFg, "Không áp dụng"),
        ["installed"] = ("", GreenBg, GreenFg, "Đã cài"),
        ["notfound"] = ("", GrayBg, GrayFg, "Chưa phát hiện"),
        ["safe"] = ("", GreenBg, GreenFg, "An toàn"),
        ["review"] = ("", AmberBg, AmberFg, "Xem kỹ trước"),
        ["low"] = ("", InfoBg, InfoFg, "Tác động thấp"),
        ["medium"] = ("", AmberBg, AmberFg, "Tác động vừa"),
        ["readonly"] = ("", GrayBg, GrayFg, "Chỉ đọc"),
    };

    private readonly TextBlock _glyph = new()
    {
        FontSize = 12,
        Margin = new Thickness(0, 0, 5, 0),
        VerticalAlignment = VerticalAlignment.Center,
    };

    private readonly TextBlock _label = new()
    {
        FontSize = 12,
        FontWeight = FontWeights.SemiBold,
        VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    public StatusChip()
    {
        Height = 24;
        CornerRadius = new CornerRadius(12);
        Padding = new Thickness(9, 0, 10, 0);
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Center;
        _glyph.SetResourceReference(TextBlock.FontFamilyProperty, "GlyphFont");
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(_glyph);
        panel.Children.Add(_label);
        Child = panel;
        Update();
    }

    public string State
    {
        get => (string)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public string? Text
    {
        get => (string?)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    private void Update()
    {
        var s = States.TryGetValue(State ?? "pending", out var found) ? found : States["pending"];
        Background = s.Bg;
        _glyph.Foreground = s.Fg;
        _label.Foreground = s.Fg;
        _glyph.Text = s.Glyph;
        _label.Text = string.IsNullOrEmpty(Text) ? s.Label : Text;
        System.Windows.Automation.AutomationProperties.SetName(this, _label.Text);
    }

    private static SolidColorBrush Frozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
