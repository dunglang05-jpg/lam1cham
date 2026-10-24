namespace LamMotCham.Core.Account;

/// <summary>Vai trò của tài khoản trong phần mềm, theo máy chủ trả về ("admin" hoặc "khach").</summary>
public enum AccountRole
{
    Guest,
    Admin,
}

/// <summary>Kết quả một lần gọi máy chủ tài khoản.</summary>
public enum AccountResultKind
{
    /// <summary>Đăng nhập hoặc kiểm tra phiên thành công.</summary>
    Success,

    /// <summary>Đăng ký thành công, tài khoản đang chờ quản trị viên duyệt.</summary>
    Registered,

    /// <summary>Sai tên đăng nhập hoặc mật khẩu.</summary>
    BadCredentials,

    /// <summary>Tài khoản chưa được kích hoạt.</summary>
    Pending,

    /// <summary>Tài khoản hoặc máy đang bị khóa tạm thời / đã bị khóa.</summary>
    Locked,

    /// <summary>Tên đăng nhập đã tồn tại.</summary>
    Exists,

    /// <summary>Dữ liệu gửi lên không hợp lệ.</summary>
    Invalid,

    /// <summary>Gửi quá nhiều lần, cần chờ.</summary>
    RateLimited,

    /// <summary>Phiên đã hết hạn hoặc tài khoản không còn hoạt động.</summary>
    SessionExpired,

    /// <summary>Máy chủ từ chối thao tác vì không đủ quyền.</summary>
    Forbidden,

    /// <summary>Máy chủ báo lỗi hoặc trả về dữ liệu không đọc được.</summary>
    ServerError,

    /// <summary>Không kết nối được máy chủ (mất mạng, hết thời gian chờ).</summary>
    Network,

    /// <summary>Bản build chưa có khóa truy cập máy chủ.</summary>
    NotConfigured,
}

/// <summary>
/// Phiên đăng nhập đã được máy chủ cấp. Chỉ giữ trong bộ nhớ của phiên chạy hiện tại: không lưu xuống đĩa,
/// thoát phần mềm là mất, lần mở sau phải đăng nhập lại. Mã phiên không bao giờ ghi vào log.
/// </summary>
/// <param name="Username">Tên đăng nhập (chữ thường như máy chủ lưu).</param>
/// <param name="DisplayName">Họ tên hiển thị.</param>
/// <param name="Role">Vai trò.</param>
/// <param name="Token">Mã phiên 64 ký tự hex do máy chủ cấp.</param>
/// <param name="SignedInAt">Thời điểm đăng nhập.</param>
public sealed record AccountSession(string Username, string DisplayName, AccountRole Role, string Token, DateTimeOffset SignedInAt)
{
    public string RoleText => Role == AccountRole.Admin ? "Quản trị viên" : "Nhân viên";

    /// <summary>Chữ cái đầu của tên để hiện trong ô tròn đại diện.</summary>
    public string Initial
    {
        get
        {
            var name = string.IsNullOrWhiteSpace(DisplayName) ? Username : DisplayName;
            var last = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "?";
            return last[..1].ToUpperInvariant();
        }
    }

    /// <summary>Không in mã phiên khi record bị ghi ra chuỗi (log, debug).</summary>
    public override string ToString() => $"{Username} ({RoleText})";
}

/// <summary>Kết quả trả về cho giao diện: loại kết quả, thông báo tiếng Việt và phiên (nếu đăng nhập thành công).</summary>
public sealed record AccountResult(AccountResultKind Kind, string Message, AccountSession? Session = null)
{
    public bool IsSuccess => Kind is AccountResultKind.Success or AccountResultKind.Registered;

    /// <summary>Lỗi do mạng hoặc máy chủ, không phải do tài khoản.</summary>
    public bool IsConnectivityProblem => Kind is AccountResultKind.Network or AccountResultKind.ServerError;
}

