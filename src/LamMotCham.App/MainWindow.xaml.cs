using System.Windows;

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
        var width = ActualWidth > 0 ? ActualWidth : (WindowState == WindowState.Maximized ? SystemParameters.WorkArea.Width : Width);
        var height = ActualHeight > 0 ? ActualHeight : (WindowState == WindowState.Maximized ? SystemParameters.WorkArea.Height : Height);
        var compact = width < CompactBelowWidth;
        if (compact != IsCompact)
        {
            IsCompact = compact;
            SidebarColumn.Width = new GridLength(compact ? 64 : 232);
        }

        // Màn hình thấp (laptop 1366×768 hoặc 1920×1080 phóng 150%): thanh trên thấp hơn, ẩn chân trang, lề nhỏ.
        var shortScreen = height < ShortBelowHeight;
        TopBarRow.Height = new GridLength(shortScreen ? 58 : 66);
        Footer.Visibility = shortScreen ? Visibility.Collapsed : Visibility.Visible;
        PageHost.Margin = (compact, shortScreen) switch
        {
            (true, _) => new Thickness(16),
            (false, true) => new Thickness(22, 16, 22, 16),
            _ => new Thickness(25),
        };
    }
}
