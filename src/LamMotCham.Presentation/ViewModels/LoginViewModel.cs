using LamMotCham.Core.Account;
using LamMotCham.Presentation.Mvvm;
using LamMotCham.Presentation.Services;

namespace LamMotCham.Presentation.ViewModels;

/// <summary>
/// Mô-đun "account": màn hình đăng nhập / đăng ký trước khi vào ứng dụng.
/// Mật khẩu chuyển thẳng từ PasswordBox qua SetPassword, không qua binding, không ghi log, xóa ngay sau khi gửi.
/// </summary>
public sealed class LoginViewModel : ObservableObject
{
    private readonly AccountService _account;
    private readonly Func<Task>? _afterSignIn;
    private bool _isRegisterMode;
    private string _username = "";
    private string _fullName = "";
    private string _password = "";
    private string _passwordConfirm = "";
    private bool _isBusy;
    private string _problem = "";
    private string _notice = "";
    private string _success = "";

    /// <param name="account">Dịch vụ tài khoản.</param>
    /// <param name="appVersion">Phiên bản hiện trên màn hình.</param>
    /// <param name="afterSignIn">Việc chạy ngay sau khi đăng nhập, trước khi vào ứng dụng (lấy danh sách phần mềm — QĐ-006).</param>
    public LoginViewModel(AccountService account, string appVersion, Func<Task>? afterSignIn = null)
    {
        ArgumentNullException.ThrowIfNull(account);
        _account = account;
        _afterSignIn = afterSignIn;
        AppVersion = "v" + appVersion;
        SubmitCommand = new AsyncCommand(SubmitAsync, () => !IsBusy && IsConfigured);
        if (!IsConfigured)
        {
            _problem = "Bản phần mềm này chưa được cấu hình máy chủ đăng nhập. Vui lòng tải bản chính thức hoặc liên hệ người phát hành.";
        }
    }

    /// <summary>Đăng nhập thành công; phiên đã nằm trong AccountService.Current.</summary>
    public event EventHandler<AccountSession>? SignedIn;

    /// <summary>Yêu cầu giao diện xóa nội dung các ô mật khẩu.</summary>
    public event EventHandler? PasswordsCleared;

    public AsyncCommand SubmitCommand { get; }

    public string AppVersion { get; }

    public bool IsConfigured => _account.IsConfigured;

    public string ServerText => _account.Endpoint.Host;

    public string MachineText => _account.MachineId;

    public bool IsRegisterMode
    {
        get => _isRegisterMode;
        set
        {
            if (Set(ref _isRegisterMode, value))
            {
                OnPropertyChanged(nameof(IsSignInMode));
                OnPropertyChanged(nameof(Title));
                OnPropertyChanged(nameof(Subtitle));
                OnPropertyChanged(nameof(PrimaryText));
                Problem = IsConfigured ? "" : Problem;
                Notice = "";
                Success = "";
                ClearPasswords();
            }
        }
    }

    public bool IsSignInMode
    {
        get => !_isRegisterMode;
        set => IsRegisterMode = !value;
    }

    public string Title => _isRegisterMode ? "Tạo tài khoản" : "Đăng nhập";

    public string Subtitle => _isRegisterMode
        ? "Tài khoản mới cần quản trị viên duyệt trước khi dùng được."
        : "Dùng tài khoản do quản trị viên cấp hoặc đã được duyệt.";

    public string PrimaryText => _isBusy
        ? (_isRegisterMode ? "Đang gửi đăng ký…" : "Đang đăng nhập…")
        : (_isRegisterMode ? "Đăng ký" : "Đăng nhập");

    public string Username
    {
        get => _username;
        set => Set(ref _username, value ?? "");
    }

    public string FullName
    {
        get => _fullName;
        set => Set(ref _fullName, value ?? "");
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (Set(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(PrimaryText));
                OnPropertyChanged(nameof(IsIdle));
                Commands.InvalidateAll();
            }
        }
    }

    /// <summary>Cho phép đổi chế độ / sửa ô nhập khi không đang gửi.</summary>
    public bool IsIdle => !_isBusy;

    /// <summary>Lỗi cần sửa (nền đỏ).</summary>
    public string Problem
    {
        get => _problem;
        private set => Set(ref _problem, value);
    }

    /// <summary>Thông tin cần chú ý, ví dụ tài khoản đang chờ duyệt (nền vàng).</summary>
    public string Notice
    {
        get => _notice;
        private set => Set(ref _notice, value);
    }

    /// <summary>Thông báo thành công, ví dụ đã đăng ký (nền xanh).</summary>
    public string Success
    {
        get => _success;
        private set => Set(ref _success, value);
    }

    public void SetPassword(string value) => _password = value ?? "";

    public void SetPasswordConfirm(string value) => _passwordConfirm = value ?? "";

    private async Task SubmitAsync()
    {
        Problem = "";
        Notice = "";
        Success = "";

        var problem = Validate();
        if (problem is not null)
        {
            Problem = problem;
            return;
        }

        IsBusy = true;
        try
        {
            if (_isRegisterMode)
            {
                await RegisterAsync();
            }
            else
            {
                await SignInAsync();
            }
        }
#pragma warning disable CA1031 // Lỗi không mong đợi phải hiện trên màn hình đăng nhập thay vì làm sập ứng dụng.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            Problem = "Có lỗi không mong đợi: " + ex.Message + " Vui lòng thử lại.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private string? Validate()
    {
        if (_isRegisterMode)
        {
            return AccountValidation.CheckName(_fullName)
                ?? AccountValidation.CheckUsername(_username)
                ?? AccountValidation.CheckNewPassword(_password)
                ?? (string.Equals(_password, _passwordConfirm, StringComparison.Ordinal) ? null : "Hai lần nhập mật khẩu chưa giống nhau.");
        }

        return AccountValidation.CheckUsername(_username) ?? AccountValidation.CheckLoginPassword(_password);
    }

    /// <summary>
    /// Mở phần mềm khi đang nối Wi-Fi nằm trong danh sách "Wi-Fi miễn đăng nhập" trên Admin thì vào thẳng (PO 02/11/2026).
    /// Không đọc được tên Wi-Fi hoặc Wi-Fi không có trong danh sách: giữ màn hình đăng nhập như thường, không báo lỗi.
    /// </summary>
    /// <param name="readSsids">Đọc tên các Wi-Fi đang kết nối (chạy nền).</param>
    public async Task TryWifiSignInAsync(Func<IReadOnlyList<string>> readSsids)
    {
        ArgumentNullException.ThrowIfNull(readSsids);
        if (!IsConfigured || IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var ssids = await Task.Run(readSsids);
            if (ssids.Count == 0)
            {
                return;
            }

            Notice = "Đang kiểm tra Wi-Fi " + string.Join(", ", ssids) + "…";
            var result = await _account.TrySignInByWifiAsync(ssids);
            Notice = "";
            if (result?.Session is not { } session)
            {
                return;
            }

            if (_afterSignIn is not null)
            {
                Notice = "Đang tải danh sách phần mềm…";
                await _afterSignIn();
                Notice = "";
            }

            SignedIn?.Invoke(this, session);
        }
#pragma warning disable CA1031 // Vào qua Wi-Fi chỉ là lối tắt: lỗi bất kỳ thì vẫn đăng nhập bằng tài khoản như thường.
        catch (Exception)
#pragma warning restore CA1031
        {
            Notice = "";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SignInAsync()
    {
        var result = await _account.SignInAsync(_username, _password);
        if (result.Kind == AccountResultKind.Success && result.Session is not null)
        {
            ClearPasswords();
            if (_afterSignIn is not null)
            {
                Notice = "Đang tải danh sách phần mềm…";
                await _afterSignIn();
                Notice = "";
            }

            SignedIn?.Invoke(this, result.Session);
            return;
        }

        if (result.Kind == AccountResultKind.Pending)
        {
            Notice = result.Message;
        }
        else
        {
            Problem = result.Message;
        }

        if (result.Kind is AccountResultKind.BadCredentials or AccountResultKind.Locked)
        {
            ClearPasswords();
        }
    }

    private async Task RegisterAsync()
    {
        var result = await _account.RegisterAsync(_fullName, _username, _password);
        if (!result.IsSuccess)
        {
            Problem = result.Message;
            return;
        }

        // Về màn hình đăng nhập, giữ tên đăng nhập để người dùng đăng nhập ngay sau khi được duyệt.
        var username = _username.Trim();
        IsRegisterMode = false;
        Username = username;
        FullName = "";
        Success = result.Message + " Khi quản trị viên duyệt xong, đăng nhập bằng tài khoản vừa tạo.";
    }

    private void ClearPasswords()
    {
        _password = "";
        _passwordConfirm = "";
        PasswordsCleared?.Invoke(this, EventArgs.Empty);
    }
}
