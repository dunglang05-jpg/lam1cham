using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using LamMotCham.Windows.Native;

namespace LamMotCham.Windows.Infrastructure;

/// <summary>
/// Phát hiện ứng dụng đang chạy bằng tài khoản khác với người đang đăng nhập máy (ví dụ máy dùng tài khoản thường,
/// kỹ thuật viên nâng quyền bằng tài khoản quản trị khác). Khi đó thiết lập theo người dùng (HKCU, gói cài cho người dùng)
/// sẽ áp vào tài khoản quản trị chứ không phải tài khoản của khách, nên các task đó phải dừng thay vì báo thành công.
/// </summary>
public static class UserContext
{
    private static readonly Lazy<string?> Cached = new(() => Describe(ProcessUser(), SessionUser()));

    /// <summary>Thay thế kết quả phát hiện trong test.</summary>
    internal static Func<string?>? OverrideForTests { get; set; }

    /// <summary>Lý do không được áp thiết lập theo người dùng; null nếu ứng dụng chạy đúng tài khoản đang đăng nhập.</summary>
    public static string? DifferentUserReason => OverrideForTests is { } o ? o() : Cached.Value;

    /// <summary>So sánh tài khoản chạy ứng dụng với tài khoản của phiên Windows. Không rõ một trong hai thì coi như khớp.</summary>
    internal static string? Describe(string? processUser, string? sessionUser)
    {
        if (string.IsNullOrWhiteSpace(processUser) || string.IsNullOrWhiteSpace(sessionUser) ||
            string.Equals(processUser, sessionUser, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return $"Phần mềm đang chạy bằng tài khoản {processUser}, không phải tài khoản đang đăng nhập máy ({sessionUser}). " +
               $"Mục này áp theo người dùng nên sẽ đổi nhầm tài khoản {processUser}; đã bỏ qua. " +
               $"Mở phần mềm trong tài khoản {sessionUser} (không nâng quyền bằng tài khoản khác) để áp dụng.";
    }

    private static string? ProcessUser()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return identity.Name;
        }
        catch (System.Security.SecurityException)
        {
            return null;
        }
    }

    private static string? SessionUser()
    {
        int sessionId;
        using (var self = Process.GetCurrentProcess())
        {
            sessionId = self.SessionId;
        }

        var user = Query(sessionId, NativeMethods.WtsUserName);
        if (string.IsNullOrEmpty(user))
        {
            return null;
        }

        var domain = Query(sessionId, NativeMethods.WtsDomainName);
        return string.IsNullOrEmpty(domain) ? user : domain + "\\" + user;
    }

    private static string? Query(int sessionId, int infoClass)
    {
        if (!NativeMethods.WTSQuerySessionInformationW(IntPtr.Zero, sessionId, infoClass, out var buffer, out _) || buffer == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStringUni(buffer);
        }
        finally
        {
            NativeMethods.WTSFreeMemory(buffer);
        }
    }
}
