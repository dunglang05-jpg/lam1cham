using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
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
    [Fact]
    public void Tick_o_dong_y_thi_nut_xac_nhan_sang_tren_giao_dien_that() => RunSta(() =>
    {
        // Ảnh/icon "pack://application:,,," phải lấy từ assembly của ứng dụng, không phải trình chạy test.
        Application.ResourceAssembly = typeof(App).Assembly;
        var app = new App();
        app.InitializeComponent();
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
            app.Shutdown();
        }
    });

    private static void CheckDialog(ConfirmDialogViewModel vm, string primaryText)
    {
        var view = new ConfirmDialog { DataContext = vm };
        var window = new Window
        {
            Content = view,
            Width = 640,
            Height = 760,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            ShowActivated = false,
            Left = -20000,
            Top = -20000,
        };
        window.Show();
        try
        {
            Pump();
            var checkBox = Descendants<CheckBox>(view).Single();
            var confirm = Descendants<Button>(view).Single(b => Equals(b.Content, primaryText));
            var back = Descendants<Button>(view).Single(b => Equals(b.Content, "Quay lại"));

            Assert.True(checkBox.IsVisible, "Ô đồng ý phải hiện.");
            Assert.True(back.IsEnabled, "Nút Quay lại luôn bấm được.");
            Assert.False(confirm.IsEnabled, "Chưa tick thì nút xác nhận phải mờ.");

            var toggle = (IToggleProvider)new CheckBoxAutomationPeer(checkBox).GetPattern(PatternInterface.Toggle);
            toggle.Toggle();
            Pump();
            Assert.Equal(ToggleState.On, toggle.ToggleState);
            Assert.True(vm.Agreed, "Tick trên giao diện phải cập nhật Agreed.");
            Assert.True(confirm.IsEnabled, "Đã tick thì nút xác nhận phải sáng (lỗi 1.1.1).");
            Assert.True(new ButtonAutomationPeer(confirm).IsEnabled(), "UI Automation cũng phải thấy nút bấm được.");

            toggle.Toggle();
            Pump();
            Assert.False(vm.Agreed);
            Assert.False(confirm.IsEnabled, "Bỏ tick thì nút xác nhận mờ lại.");

            toggle.Toggle();
            Pump();
            Assert.True(confirm.IsEnabled, "Tick lại thì nút sáng lại.");
        }
        finally
        {
            window.Close();
        }
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
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
        thread.Start();
        thread.Join();
        if (error is not null)
        {
            throw new Xunit.Sdk.XunitException("Kiểm thử giao diện thất bại: " + error);
        }
    }
}
