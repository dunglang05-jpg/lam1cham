using System.Text.RegularExpressions;

namespace LamMotCham.Core.Account;

/// <summary>
/// Kiểm tra dữ liệu trước khi gửi, cùng quy tắc với máy chủ (api.php / lib.php: RE_NAME, RE_USER, RE_PASS, RE_MACHINE)
/// để báo lỗi ngay trên máy thay vì chờ máy chủ trả về.
/// </summary>
public static partial class AccountValidation
{
    public const int NameMinLength = 2;
    public const int NameMaxLength = 50;
    public const int PasswordMinLength = 6;
    public const int PasswordMaxLength = 64;

    /// <summary>Gộp khoảng trắng thừa trong họ tên như máy chủ làm trước khi kiểm tra.</summary>
    public static string NormalizeName(string? name) => SpacesRegex().Replace(name ?? "", " ").Trim();

    /// <summary>null nếu hợp lệ, ngược lại là thông báo lỗi.</summary>
    public static string? CheckName(string? name)
    {
        var n = NormalizeName(name);
        if (n.Length == 0)
        {
            return "Nhập họ tên.";
        }

        if (n.Length < NameMinLength || n.Length > NameMaxLength)
        {
            return $"Họ tên dài từ {NameMinLength} đến {NameMaxLength} ký tự.";
        }

        return NameRegex().IsMatch(n) ? null : "Họ tên chỉ gồm chữ cái và khoảng trắng.";
    }

    public static string? CheckUsername(string? username)
    {
        var u = (username ?? "").Trim();
        if (u.Length == 0)
        {
            return "Nhập tên đăng nhập hoặc mã kho.";
        }

        return UserRegex().IsMatch(u)
            ? null
            : "Tên đăng nhập dài 3–30 ký tự, bắt đầu bằng chữ hoặc số, chỉ gồm chữ không dấu, số và . _ -";
    }

    /// <summary>Mật khẩu khi đăng ký: 6–64 ký tự, không có khoảng trắng.</summary>
    public static string? CheckNewPassword(string? password)
    {
        var p = password ?? "";
        if (p.Length == 0)
        {
            return "Nhập mật khẩu.";
        }

        return PasswordRegex().IsMatch(p)
            ? null
            : $"Mật khẩu dài {PasswordMinLength}–{PasswordMaxLength} ký tự và không có khoảng trắng.";
    }

    /// <summary>Mật khẩu khi đăng nhập: chỉ kiểm tra độ dài như máy chủ, để tài khoản cũ vẫn đăng nhập được.</summary>
    public static string? CheckLoginPassword(string? password)
    {
        var p = password ?? "";
        if (p.Length == 0)
        {
            return "Nhập mật khẩu.";
        }

        return p.Length > PasswordMaxLength ? $"Mật khẩu dài tối đa {PasswordMaxLength} ký tự." : null;
    }

    /// <summary>Mã máy hợp lệ theo máy chủ; mã không hợp lệ bị máy chủ đổi thành "khong-ro".</summary>
    public static bool IsValidMachineId(string? machineId) => machineId is not null && MachineRegex().IsMatch(machineId);

    /// <summary>Mã phiên máy chủ cấp: 64 ký tự hex thường.</summary>
    public static bool IsValidToken(string? token) => token is not null && TokenRegex().IsMatch(token);

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpacesRegex();

    [GeneratedRegex(@"^\p{L}+(?: \p{L}+)*\z")]
    private static partial Regex NameRegex();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{2,29}\z")]
    private static partial Regex UserRegex();

    [GeneratedRegex(@"^\S{6,64}\z")]
    private static partial Regex PasswordRegex();

    [GeneratedRegex(@"^[A-Za-z0-9._-]{1,40}\z")]
    private static partial Regex MachineRegex();

    [GeneratedRegex(@"^[0-9a-f]{64}\z")]
    private static partial Regex TokenRegex();
}
