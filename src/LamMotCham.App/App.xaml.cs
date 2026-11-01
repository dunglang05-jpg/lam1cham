using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using LamMotCham.App.Views;
using LamMotCham.Core.Logging;
using LamMotCham.Presentation.Services;
using LamMotCham.Presentation.ViewModels;

namespace LamMotCham.App;

[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "Application không dùng IDisposable; mutex và MainViewModel được giải phóng trong OnExit.")]
public partial class App : Application, IUiPlatform
{
    private const string SignOutArgument = "--after-signout";

    private Mutex? _singleInstance;

    /// <summary>Mở lại sau khi bấm Đăng xuất: không tự vào qua Wi-Fi nữa, để đăng nhập tài khoản khác được.</summary>
    private bool _afterSignOut;
    private AppServices? _services;
    private MainViewModel? _main;
    private bool _showingError;
    private string? _lastErrorMessage;
    private DateTimeOffset _lastErrorAt;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _afterSignOut = e.Args.Contains(SignOutArgument);

        // Chỉ một bản chạy tại một thời điểm để không có hai hàng đợi cài đặt chạy chồng.
        _singleInstance = new Mutex(true, @"Local\LamMotCham.SingleInstance", out var created);
        if (!created)
        {
            var waitForPrevious = e.Args.Contains(SignOutArgument);
            bool acquired;
            try
            {
                acquired = waitForPrevious && _singleInstance.WaitOne(TimeSpan.FromSeconds(10));
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }

            if (!acquired)
            {
                MessageBox.Show("Làm 1 Chạm đang chạy. Hãy dùng cửa sổ đang mở.", "Làm 1 Chạm", MessageBoxButton.OK, MessageBoxImage.Information);
                _singleInstance.Dispose();
                _singleInstance = null;
                Shutdown();
                return;
            }
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            _services?.Log.Error("Lỗi nền không được xử lý: " + args.Exception.GetBaseException().Message);
            args.SetObserved();
        };

        _services = AppServices.Create();

        // Mô-đun "account": đăng nhập trước khi vào ứng dụng. Phiên chỉ nằm trong bộ nhớ của lần chạy này.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        ShowSignIn();
    }

    private void ShowSignIn()
    {
        var services = _services!;
        var vm = new LoginViewModel(services.Account, services.Version, () => services.LoadSoftwareCatalogAsync());
        var window = new LoginWindow { DataContext = vm };
        var signedIn = false;
        vm.SignedIn += (_, _) =>
        {
            signedIn = true;
            ShowMain();
            window.Close();
        };
        window.Closed += (_, _) =>
        {
            if (!signedIn)
            {
                Shutdown();
            }
        };
        MainWindow = window;
        window.Show();

        // Wi-Fi miễn đăng nhập (Admin › Cài đặt): đang nối Wi-Fi của cửa hàng thì vào thẳng.
        // Vừa đăng xuất thì không tự vào lại (người dùng muốn đổi tài khoản).
        if (_afterSignOut)
        {
            return;
        }

        window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, async () => await vm.TryWifiSignInAsync(() =>
        {
            var ssids = LamMotCham.Windows.Network.WifiInfo.ConnectedSsids();
            if (ssids.Count == 0 && LamMotCham.Windows.Network.WifiInfo.LastProblem is { } why)
            {
                services.Log.Info("Không dùng được Wi-Fi miễn đăng nhập: " + why);
            }

            return ssids;
        }));
    }

    private void ShowMain()
    {
        _main = new MainViewModel(_services!, this);
        var window = new MainWindow { DataContext = _main };
        MainWindow = window;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        window.Show();
        _main.StartTicker();
        window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, async () => await _main.Machine.StartupAsync());
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Thoát phần mềm là xóa phiên đăng nhập (AccountService.Dispose); không có gì lưu lại trên đĩa.
        if (_main is not null)
        {
            _main.Dispose();
        }
        else
        {
            _services?.Dispose();
        }
        if (_singleInstance is not null)
        {
            try
            {
                _singleInstance.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Mutex không thuộc luồng này.
            }

            _singleInstance.Dispose();
        }

        base.OnExit(e);
    }

    void IUiPlatform.Shutdown() => Shutdown();

    /// <summary>Đăng xuất: mở lại phần mềm ở màn hình đăng nhập (trạng thái sạch, giữ nguyên quyền quản trị nếu đang có).</summary>
    void IUiPlatform.RestartForSignIn()
    {
        _services?.Account.SignOut();
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!)
            {
                UseShellExecute = false,
                ArgumentList = { SignOutArgument },
            })?.Dispose();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            _services?.Log.Error("Không mở lại được phần mềm sau khi đăng xuất: " + ex.Message);
        }

        Shutdown();
    }

    void IUiPlatform.SetClipboard(string text)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch (COMException)
        {
            // Clipboard đang bị ứng dụng khác giữ.
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        _services?.Log.Error("Lỗi giao diện không được xử lý: " + e.Exception);

        // Lỗi dựng giao diện lặp lại ở mỗi lượt vẽ: chỉ báo một lần, không mở hộp thoại lồng nhau.
        var now = DateTimeOffset.Now;
        var same = string.Equals(_lastErrorMessage, e.Exception.Message, StringComparison.Ordinal) && now - _lastErrorAt < TimeSpan.FromSeconds(15);
        _lastErrorMessage = e.Exception.Message;
        _lastErrorAt = now;
        if (_showingError || same)
        {
            return;
        }

        // Trang không dựng được: quay về Tổng quan để ứng dụng vẫn dùng được.
        var pageFailed = e.Exception is XamlParseException || e.Exception.InnerException is XamlParseException;
        var leftPage = false;
        if (pageFailed && _main is not null && _main.CurrentPage != Page.Overview)
        {
            _main.CurrentPage = Page.Overview;
            leftPage = true;
        }

        _showingError = true;
        try
        {
            MessageBox.Show(
                "Có lỗi không mong đợi: " + e.Exception.Message + Environment.NewLine + Environment.NewLine +
                (leftPage ? "Trang vừa mở không hiển thị được nên ứng dụng đã quay về Tổng quan. " : "Thao tác vừa rồi có thể chưa hoàn tất. ") +
                "Chi tiết đã được ghi vào log.",
                "Làm 1 Chạm", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _showingError = false;
        }
    }
}
