using System.Windows;
using System.Windows.Media;

namespace LamMotCham.App;

/// <summary>Khung ứng dụng (mô-đun "shell"): chọn kích thước theo màn hình và thu gọn thanh bên khi cửa sổ hẹp.</summary>
public partial class MainWindow : Window
{
    /// <summary>Cửa sổ hẹp hơn mức này thì thanh bên chỉ còn icon.</summary>
    public const double CompactBelowWidth = 1200;

    /// <summary>Dưới chiều cao này (DIP) dùng lề trên/dưới nhỏ.</summary>
    public const double ShortBelowHeight = 820;

    public static readonly DependencyProperty IsCompactProperty = DependencyProperty.Register(
        nameof(IsCompact), typeof(bool), typeof(MainWindow), new PropertyMetadata(false));

    private const double DesignWidth = 1366;
    private const double DesignHeight = 768;
    private const double MinScale = 0.85;

    private double _scale = 1.0;

    private const double PreferredWidth = 1440;
    private const double PreferredHeight = 900;

    public MainWindow()
    {
        InitializeComponent();
        FitToScreen();
        SizeChanged += (_, _) => UpdateCompact();
        UpdateCompact();
    }

    /// <summary>true khi cửa sổ hẹp: thanh bên thu gọn, lề nhỏ hơn.</summary>
    public bool IsCompact
    {
        get => (bool)GetValue(IsCompactProperty);
        private set => SetValue(IsCompactProperty, value);
    }

    /// <summary>Không để cửa sổ vượt quá vùng làm việc (trừ taskbar); màn hình nhỏ thì mở toàn màn hình.</summary>
    private void FitToScreen()
    {
        var area = SystemParameters.WorkArea;
        MinWidth = Math.Min(MinWidth, area.Width);
        MinHeight = Math.Min(MinHeight, area.Height);
        if (area.Width < PreferredWidth + 20 || area.Height < PreferredHeight + 20)
        {
            Width = Math.Min(PreferredWidth, area.Width);
            Height = Math.Min(PreferredHeight, area.Height);
            WindowState = WindowState.Maximized;
        }
        else
        {
            Width = PreferredWidth;
            Height = PreferredHeight;
        }
    }

    private void UpdateCompact()
    {
        var outerWidth = ActualWidth > 0 ? ActualWidth : (WindowState == WindowState.Maximized ? SystemParameters.WorkArea.Width : Width);
        var outerHeight = ActualHeight > 0 ? ActualHeight : (WindowState == WindowState.Maximized ? SystemParameters.WorkArea.Height : Height);

        // Vùng nội dung (trừ khung cửa sổ và thanh tiêu đề Windows).
        var width = Math.Max(1, outerWidth - 16);
        var height = Math.Max(1, outerHeight - 39);

        // Co giãn: nhỏ hơn khung thiết kế 1366×768 thì thu nhỏ toàn bộ giao diện (tối thiểu 85%) để vừa một màn hình.
        var scale = Math.Clamp(Math.Min(width / DesignWidth, height / DesignHeight), MinScale, 1.0);
        if (Math.Abs(scale - _scale) > 0.005)
        {
            _scale = scale;
            Root.LayoutTransform = scale < 0.995 ? new ScaleTransform(scale, scale) : Transform.Identity;
            TextOptions.SetTextFormattingMode(this, scale < 0.995 ? TextFormattingMode.Ideal : TextFormattingMode.Display);
        }

        var effectiveWidth = width / _scale;
        var effectiveHeight = height / _scale;
        var compact = effectiveWidth < CompactBelowWidth;
        if (compact != IsCompact)
        {
            IsCompact = compact;
            SidebarColumn.Width = new GridLength(compact ? 64 : 232);
        }

        // Màn hình thấp: thanh trên thấp hơn, ẩn chân trang, lề nhỏ.
        var shortScreen = effectiveHeight < ShortBelowHeight;
        TopBarRow.Height = new GridLength(shortScreen ? 52 : 58);
        Footer.Visibility = shortScreen ? Visibility.Collapsed : Visibility.Visible;
        PageHost.Margin = (compact, shortScreen) switch
        {
            (true, _) => new Thickness(14),
            (false, true) => new Thickness(18, 14, 18, 14),
            _ => new Thickness(20),
        };
    }
}
