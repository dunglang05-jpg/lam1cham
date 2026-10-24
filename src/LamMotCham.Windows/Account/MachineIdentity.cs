using System.Security.Cryptography;
using System.Text;
using LamMotCham.Core.Account;
using Microsoft.Win32;

namespace LamMotCham.Windows.Account;

/// <summary>
/// Mã máy gửi kèm khi đăng nhập/đăng ký để quản trị viên biết tài khoản dùng trên máy nào (máy chủ ghi vào nhật ký).
/// Tính lại mỗi lần chạy từ MachineGuid của Windows, băm SHA-256 để không gửi mã gốc; không lưu xuống đĩa.
/// </summary>
public static class MachineIdentity
{
    private const string Prefix = "lmc-";

    public static string Get()
    {
        var source = ReadMachineGuid() ?? Environment.MachineName;
        var id = FromSource(source);
        return AccountValidation.IsValidMachineId(id) ? id : Prefix + "khong-ro";
    }

    /// <summary>"lmc-" + 20 ký tự hex đầu của SHA-256 (chữ thường), hợp lệ theo RE_MACHINE của máy chủ.</summary>
    internal static string FromSource(string source)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("LamMotCham.Machine.v1|" + source.Trim().ToUpperInvariant()));
        return Prefix + Convert.ToHexStringLower(hash)[..20];
    }

    private static string? ReadMachineGuid()
    {
        try
        {
            using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = hive.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            return key?.GetValue("MachineGuid") is string guid && !string.IsNullOrWhiteSpace(guid) ? guid : null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }
}
