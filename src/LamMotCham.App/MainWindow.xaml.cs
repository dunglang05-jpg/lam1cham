using System.Windows;

namespace LamMotCham.App;

/// <summary>Khung ứng dụng (mô-đun "shell"): chọn kích thước theo màn hình và thu gọn thanh bên khi cửa sổ hẹp.</summary>
public partial class MainWindow : Window
{
    /// <summary>Cửa sổ hẹp hơn mức này thì thanh bên chỉ còn icon.</summary>
    public const double CompactBelowWidth = 1200;

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
        var compact = width < CompactBelowWidth;
        if (compact != IsCompact)
        {
            IsCompact = compact;
            SidebarColumn.Width = new GridLength(compact ? 76 : 232);
            PageHost.Margin = compact ? new Thickness(18, 16, 18, 16) : new Thickness(28, 24, 28, 24);
        }
    }
}
