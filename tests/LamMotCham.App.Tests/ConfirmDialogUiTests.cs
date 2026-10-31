using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using LamMotCham.App.Views.Dialogs;
using LamMotCham.Presentation.ViewModels;
using Xunit;

namespace LamMotCham.App.Tests;

/// <summary>
/// Kiểm thử giao diện thật của hộp xác nhận (ConfirmDialog.xaml + giao diện của ứng dụng):
/// tick ô đồng ý bằng UI Automation như người dùng bấm chuột, rồi xem nút xác nhận có sáng không.
/// Lỗi 1.1.1 trên laptop thật: đã tick nhưng nút "Xác nhận cài đặt" / "Bắt đầu chạy" vẫn mờ.
/// </summary>
public class ConfirmDialogUiTests
{
    private static volatile string _stage = "bắt đầu";

    [Fact]
    public void Tick_o_dong_y_thi_nut_xac_nhan_sang_tren_giao_dien_that() => RunSta(() =>
    {
        _stage = "tạo App";
        var app = new App();
        app.InitializeComponent();
        // App.xaml dùng OnMainWindowClose: đóng cửa sổ thử đầu tiên sẽ tắt luôn dispatcher, hộp thứ hai treo. Test tự tắt.
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        _stage = "đã nạp giao diện ứng dụng";

        // Icon dạng "pack://application:,,," trỏ vào trình chạy test chứ không phải LamMotCham.exe, nên thay bộ nạp icon
        // bằng bộ rỗng. Ô đồng ý, nút bấm, binding và giao diện hộp thoại giữ nguyên như ứng dụng.
        app.Resources["Icon"] = new NoIcon();
        app.Resources["LogoOrIcon"] = new NoIcon();
        try
        {
            // Hộp "Xác nhận cài đặt" (trang Phần mềm thiết yếu).
            CheckDialog(new ConfirmDialogViewModel(null!, "Xác nhận cài đặt", "download", "Xác nhận cài đặt", () => Task.CompletedTask)
            {
                Items = [new DialogItem("1. Google Chrome", null, null, null, "Google.Chrome")],
                Facts = [new DialogFact("Nguồn", "WinGet")],
                RequireAgreement = true,
                AgreementText = "Đồng ý để Làm 1 Chạm chấp nhận điều khoản của nguồn WinGet và của từng ứng dụng thay cho người dùng máy này.",
            }, "Xác nhận cài đặt");

            // Hộp "Xác nhận trước khi chạy" của Thiết lập đầy đủ (có cảnh báo BitLocker).
            CheckDialog(new ConfirmDialogViewModel(null!, "Xác nhận trước khi chạy", "shield", "Bắt đầu chạy", () => Task.CompletedTask)
            {
                Lead = "41 tác vụ sẽ chạy tuần tự.",
                Warning = "Có mục Tắt BitLocker.",
                WarningIsDanger = true,
                RequireAgreement = true,
                AgreementText = "Khách hàng đã đồng ý tắt mã hóa ổ đĩa (BitLocker).",
            }, "Bắt đầu chạy");
        }
        finally
        {
            _stage = "tắt App";
            app.Shutdown();
            _stage = "đã tắt App";
        }
    });

    private static void CheckDialog(ConfirmDialogViewModel vm, string primaryText)
    {
        _stage = "dựng hộp " + primaryText;
        var view = new ConfirmDialog { DataContext = vm };
        try
        {
            // Dựng và bố cục trong bộ nhớ (không mở cửa sổ): template, binding và lệnh hoạt động như trên màn hình.
            view.Measure(new Size(640, 760));
            view.Arrange(new Rect(0, 0, 640, 760));
            view.UpdateLayout();
            _stage = "bố cục xong " + primaryText;
            _stage = "kiểm tra " + primaryText;
            var checkBox = Descendants<CheckBox>(view).Single();
            var confirm = Descendants<Button>(view).Single(b => Equals(b.Content, primaryText));
            var back = Descendants<Button>(view).Single(b => Equals(b.Content, "Quay lại"));

            Assert.Equal(Visibility.Visible, checkBox.Visibility);
            Assert.True(back.IsEnabled, "Nút Quay lại luôn bấm được.");
            Assert.False(confirm.IsEnabled, "Chưa tick thì nút xác nhận phải mờ.");

            var toggle = (IToggleProvider)new CheckBoxAutomationPeer(checkBox).GetPattern(PatternInterface.Toggle);
            toggle.Toggle();
            Assert.Equal(ToggleState.On, toggle.ToggleState);
            Assert.True(vm.Agreed, "Tick trên giao diện phải cập nhật Agreed.");
            Assert.True(confirm.IsEnabled, "Đã tick thì nút xác nhận phải sáng (lỗi 1.1.1).");
            Assert.True(new ButtonAutomationPeer(confirm).IsEnabled(), "UI Automation cũng phải thấy nút bấm được.");

            toggle.Toggle();
            Assert.False(vm.Agreed);
            Assert.False(confirm.IsEnabled, "Bỏ tick thì nút xác nhận mờ lại.");

            toggle.Toggle();
            Assert.True(confirm.IsEnabled, "Tick lại thì nút sáng lại.");
        }
        finally
        {
            _stage = "xong " + primaryText;
        }
    }

    private sealed class NoIcon : System.Windows.Data.IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => null;

        public object? ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => null;
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t)
            {
                yield return t;
            }

            foreach (var d in Descendants<T>(child))
            {
                yield return d;
            }
        }
    }

    private static void RunSta(Action body)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                body();
            }
#pragma warning disable CA1031 // Chuyển mọi lỗi trên luồng giao diện về luồng kiểm thử để báo đúng.
            catch (Exception ex) when (ex is not OutOfMemoryException)
#pragma warning restore CA1031
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(60)))
        {
            throw new Xunit.Sdk.XunitException("Kiểm thử giao diện không xong sau 60 giây (treo) ở bước: " + _stage);
        }
        if (error is not null)
        {
            throw new Xunit.Sdk.XunitException("Kiểm thử giao diện thất bại: " + error);
        }
    }
}
