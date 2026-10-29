using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace LamMotCham.Windows.Software;

/// <summary>Kết quả kiểm tra chữ ký số của một file.</summary>
/// <param name="IsValid">Windows (WinVerifyTrust) xác nhận chữ ký hợp lệ, file không bị sửa.</param>
/// <param name="Organization">Tổ chức (O) trong chứng chỉ ký, nếu có.</param>
/// <param name="CommonName">Tên chung (CN) trong chứng chỉ ký, nếu có.</param>
/// <param name="Detail">Mô tả cho log và thông báo lỗi.</param>
public sealed record SignatureCheck(bool IsValid, string? Organization, string? CommonName, string Detail)
{
    /// <summary>Chữ ký hợp lệ và hãng ký nằm trong <see cref="TrustedPublishers"/>.</summary>
    public bool IsTrusted => IsValid && (TrustedPublishers.IsTrusted(Organization) || TrustedPublishers.IsTrusted(CommonName));

    public string SignerText => Organization ?? CommonName ?? "không rõ";
}

/// <summary>
/// Kiểm tra chữ ký Authenticode bằng WinVerifyTrust (WINTRUST_ACTION_GENERIC_VERIFY_V2) rồi đọc tên hãng từ chứng chỉ ký.
/// Không kiểm tra thu hồi chứng chỉ qua mạng để không treo khi máy chưa có Internet ổn định.
/// </summary>
public static class Authenticode
{
    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    private const uint WtdUiNone = 2;
    private const uint WtdRevokeNone = 0;
    private const uint WtdChoiceFile = 1;
    private const uint WtdStateActionIgnore = 0;
    private const uint WtdCacheOnlyUrlRetrieval = 0x1000;

    public static SignatureCheck Check(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!OperatingSystem.IsWindows())
        {
            return new SignatureCheck(false, null, null, "Chỉ kiểm tra được chữ ký trên Windows.");
        }

        var status = Verify(path);
        if (status != 0)
        {
            return new SignatureCheck(false, null, null, status switch
            {
                unchecked((int)0x800B0100) => "File không có chữ ký số.",
                unchecked((int)0x80096010) => "Chữ ký số không khớp nội dung file (file đã bị sửa).",
                unchecked((int)0x800B0109) => "Chứng chỉ ký không thuộc tổ chức cấp chứng chỉ tin cậy.",
                unchecked((int)0x800B0101) => "Chứng chỉ ký đã hết hạn.",
                _ => $"Chữ ký số không hợp lệ (0x{unchecked((uint)status):X8}).",
            });
        }

        try
        {
#pragma warning disable SYSLIB0057 // Chỉ đọc chứng chỉ ký nhúng trong file; tính hợp lệ đã do WinVerifyTrust xác nhận ở trên.
            using var cert = X509Certificate.CreateFromSignedFile(path);
#pragma warning restore SYSLIB0057
            var subject = cert.Subject;
            var o = Rdn(subject, "O");
            var cn = Rdn(subject, "CN");
            return new SignatureCheck(true, o, cn, $"Có chữ ký số hợp lệ của {o ?? cn ?? "không rõ"}.");
        }
        catch (CryptographicException ex)
        {
            return new SignatureCheck(false, null, null, "Không đọc được chứng chỉ ký: " + ex.Message);
        }
    }

    /// <summary>Lấy một thành phần (O, CN) trong chuỗi Subject, có xử lý giá trị trong ngoặc kép.</summary>
    internal static string? Rdn(string subject, string key)
    {
        foreach (var part in SplitRdns(subject))
        {
            var eq = part.IndexOf('=', StringComparison.Ordinal);
            if (eq > 0 && string.Equals(part[..eq].Trim(), key, StringComparison.OrdinalIgnoreCase))
            {
                var value = part[(eq + 1)..].Trim();
                if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
                {
                    value = value[1..^1].Replace("\"\"", "\"", StringComparison.Ordinal);
                }

                return value.Length > 0 ? value : null;
            }
        }

        return null;
    }

    private static IEnumerable<string> SplitRdns(string subject)
    {
        var sb = new StringBuilder();
        var quoted = false;
        foreach (var c in subject)
        {
            if (c == '"')
            {
                quoted = !quoted;
            }

            if (c == ',' && !quoted)
            {
                yield return sb.ToString();
                sb.Clear();
                continue;
            }

            sb.Append(c);
        }

        if (sb.Length > 0)
        {
            yield return sb.ToString();
        }
    }

    private static int Verify(string path)
    {
        var filePath = Marshal.StringToHGlobalUni(path);
        var fileInfo = IntPtr.Zero;
        try
        {
            var info = new WinTrustFileInfo
            {
                CbStruct = (uint)Marshal.SizeOf<WinTrustFileInfo>(),
                PcwszFilePath = filePath,
            };
            fileInfo = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
            Marshal.StructureToPtr(info, fileInfo, false);

            var data = new WinTrustData
            {
                CbStruct = (uint)Marshal.SizeOf<WinTrustData>(),
                DwUiChoice = WtdUiNone,
                FdwRevocationChecks = WtdRevokeNone,
                DwUnionChoice = WtdChoiceFile,
                PFile = fileInfo,
                DwStateAction = WtdStateActionIgnore,
                DwProvFlags = WtdCacheOnlyUrlRetrieval,
            };
            var action = GenericVerifyV2;
            return WinVerifyTrust(IntPtr.Zero, ref action, ref data);
        }
        finally
        {
            if (fileInfo != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(fileInfo);
            }

            Marshal.FreeHGlobal(filePath);
        }
    }

    [DllImport("wintrust.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int WinVerifyTrust(IntPtr hwnd, ref Guid action, ref WinTrustData data);

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustFileInfo
    {
        public uint CbStruct;
        public IntPtr PcwszFilePath;
        public IntPtr HFile;
        public IntPtr PgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustData
    {
        public uint CbStruct;
        public IntPtr PPolicyCallbackData;
        public IntPtr PSipClientData;
        public uint DwUiChoice;
        public uint FdwRevocationChecks;
        public uint DwUnionChoice;
        public IntPtr PFile;
        public uint DwStateAction;
        public IntPtr HWvtStateData;
        public IntPtr PwszUrlReference;
        public uint DwProvFlags;
        public uint DwUiContext;
        public IntPtr PSignatureSettings;
    }
}
