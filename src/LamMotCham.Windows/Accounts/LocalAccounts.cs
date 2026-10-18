using System.Globalization;
using System.Runtime.InteropServices;
using LamMotCham.Windows.Infrastructure;
using LamMotCham.Windows.Native;

namespace LamMotCham.Windows.Accounts;

/// <summary>Một tài khoản người dùng trên máy. Không chứa mật khẩu hay thông tin bí mật.</summary>
/// <param name="IsLocal">true: tài khoản cục bộ (sửa được). false: tài khoản Microsoft/Azure AD — quản lý trên mạng.</param>
/// <param name="IsCurrent">Tài khoản đang chạy ứng dụng.</param>
public sealed record LocalAccount(string Name, string FullName, string Sid, bool IsAdmin, bool IsLocal, bool IsCurrent)
{
    public string KindText => IsAdmin ? "Quản trị viên" : "Người dùng chuẩn";

    public string SourceText => IsLocal ? "Tài khoản cục bộ" : "Tài khoản Microsoft";
}

/// <summary>Đọc và sửa tài khoản cục bộ bằng Windows API (netapi32). Mật khẩu không đi qua dòng lệnh, file hay log.</summary>
public static class LocalAccounts
{
    private const string ReadScript = """
        $ErrorActionPreference = 'Stop'
        $admins = @()
        try { $admins = @(Get-LocalGroupMember -SID 'S-1-5-32-544' -ErrorAction Stop | ForEach-Object { [string]$_.SID.Value }) } catch { }
        $me = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
        $users = @(Get-LocalUser | Where-Object { $_.Enabled } | ForEach-Object {
          [ordered]@{
            name = [string]$_.Name; fullName = [string]$_.FullName; sid = [string]$_.SID.Value
            source = [string]$_.PrincipalSource
            admin = ($admins -contains [string]$_.SID.Value)
            current = ([string]$_.SID.Value -eq $me)
          }
        })
        [ordered]@{ users = $users } | ConvertTo-Json -Depth 4 -Compress
        """;

    /// <summary>Tài khoản đang bật trên máy (ẩn tài khoản hệ thống bị tắt như Administrator, Guest).</summary>
    public static async Task<(IReadOnlyList<LocalAccount> Accounts, string? Error)> ReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var doc = await PowerShellRunner.RunJsonAsync(ReadScript, cancellationToken).ConfigureAwait(false);
            var list = new List<LocalAccount>();
            foreach (var u in doc.RootElement.GetProperty("users").EnumerateArray())
            {
                var source = u.GetProperty("source").GetString() ?? "";
                list.Add(new LocalAccount(
                    u.GetProperty("name").GetString() ?? "",
                    u.GetProperty("fullName").GetString() ?? "",
                    u.GetProperty("sid").GetString() ?? "",
                    u.GetProperty("admin").GetBoolean(),
                    string.Equals(source, "Local", StringComparison.OrdinalIgnoreCase),
                    u.GetProperty("current").GetBoolean()));
            }

            return (list, null);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Text.Json.JsonException or System.ComponentModel.Win32Exception or IOException
                                       or KeyNotFoundException)
        {
            return ([], "Không đọc được danh sách tài khoản: " + ex.Message);
        }
    }

    /// <summary>
    /// Tên đăng nhập hợp lệ theo Windows: 1–20 ký tự, không chứa " / \ [ ] : ; | = , + * ? &lt; &gt; @,
    /// không toàn dấu chấm/khoảng trắng, không có khoảng trắng đầu/cuối.
    /// </summary>
    public static bool IsValidUserName(string? name) =>
        name is { Length: >= 1 and <= 20 }
        && name.Trim().Length == name.Length
        && name.AsSpan().IndexOfAny(InvalidNameChars) < 0
        && !name.Any(char.IsControl)
        && !name.All(c => c is '.' or ' ');

    /// <summary>Tên hiển thị: tối đa 256 ký tự, không ký tự điều khiển.</summary>
    public static bool IsValidFullName(string? fullName) => fullName is { Length: <= 256 } && !fullName.Any(char.IsControl);

    private static readonly System.Buffers.SearchValues<char> InvalidNameChars = System.Buffers.SearchValues.Create("\"/\\[]:;|=,+*?<>@");

    internal static bool Exists(string name) => GetFullName(name, out _) == NativeMethods.NerrSuccess;

    /// <summary>Đọc tên hiển thị. Trả về mã lỗi netapi (0 = thành công).</summary>
    internal static uint GetFullName(string name, out string fullName)
    {
        fullName = "";
        var rc = NativeMethods.NetUserGetInfo(null, name, 10, out var buffer);
        try
        {
            if (rc == NativeMethods.NerrSuccess && buffer != IntPtr.Zero)
            {
                var info = Marshal.PtrToStructure<NativeMethods.UserInfo10>(buffer);
                fullName = Marshal.PtrToStringUni(info.FullName) ?? "";
            }
        }
        finally
        {
            if (buffer != IntPtr.Zero)
            {
                _ = NativeMethods.NetApiBufferFree(buffer);
            }
        }

        return rc;
    }

    internal static uint SetFullName(string name, string fullName)
    {
        var ptr = Marshal.StringToCoTaskMemUni(fullName);
        try
        {
            var info = new NativeMethods.UserInfo1011 { FullName = ptr };
            return NativeMethods.NetUserSetInfo(null, name, 1011, ref info, out _);
        }
        finally
        {
            Marshal.FreeCoTaskMem(ptr);
        }
    }

    internal static uint Rename(string name, string newName)
    {
        var ptr = Marshal.StringToCoTaskMemUni(newName);
        try
        {
            var info = new NativeMethods.UserInfo0 { Name = ptr };
            return NativeMethods.NetUserSetInfo(null, name, 0, ref info, out _);
        }
        finally
        {
            Marshal.FreeCoTaskMem(ptr);
        }
    }

    /// <summary>Đặt lại mật khẩu (quyền quản trị). Bộ nhớ chứa mật khẩu được xóa trắng ngay sau lời gọi.</summary>
    internal static uint SetPassword(string name, string password)
    {
        var ptr = Marshal.StringToCoTaskMemUni(password);
        try
        {
            var info = new NativeMethods.UserInfo1003 { Password = ptr };
            return NativeMethods.NetUserSetInfo(null, name, 1003, ref info, out _);
        }
        finally
        {
            Marshal.ZeroFreeCoTaskMemUnicode(ptr);
        }
    }

    /// <summary>Thử đăng nhập mạng để xác minh mật khẩu. Trả về mã lỗi Win32 (0 = đăng nhập được).</summary>
    internal static int TryLogon(string name, string password)
    {
        var ptr = Marshal.StringToCoTaskMemUni(password);
        try
        {
            if (NativeMethods.LogonUserW(name, ".", ptr, NativeMethods.Logon32LogonNetwork, NativeMethods.Logon32ProviderDefault, out var token))
            {
                _ = NativeMethods.CloseHandle(token);
                return 0;
            }

            return Marshal.GetLastPInvokeError();
        }
        finally
        {
            Marshal.ZeroFreeCoTaskMemUnicode(ptr);
        }
    }

    /// <summary>Thông báo lỗi tiếng Việt cho mã netapi/Win32. Không bao giờ chứa mật khẩu.</summary>
    internal static string Describe(uint code) => code switch
    {
        NativeMethods.ErrorAccessDenied => "Cần quyền quản trị để sửa tài khoản.",
        NativeMethods.NerrUserNotFound => "Không tìm thấy tài khoản.",
        NativeMethods.NerrUserExists => "Đã có tài khoản khác dùng tên này.",
        NativeMethods.NerrPasswordTooShort => "Mật khẩu không đạt chính sách của máy (độ dài, độ phức tạp hoặc trùng mật khẩu cũ).",
        _ => "Windows trả về mã lỗi " + code.ToString(CultureInfo.InvariantCulture) + ".",
    };
}
