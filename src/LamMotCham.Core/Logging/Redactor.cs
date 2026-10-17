using System.Text.RegularExpressions;

namespace LamMotCham.Core.Logging;

/// <summary>
/// Loại bỏ dữ liệu nhạy cảm trước khi ghi log hoặc báo cáo (tài liệu 05, mục 5–6).
/// Đây là lớp bảo vệ cuối cùng; task vẫn không được chủ động log bí mật.
/// </summary>
public static partial class Redactor
{
    public const string Placeholder = "[ĐÃ ẨN]";

    public static string Redact(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        var text = input;
        text = JwtRegex().Replace(text, Placeholder);
        text = BearerRegex().Replace(text, "Bearer " + Placeholder);
        text = BitLockerKeyRegex().Replace(text, Placeholder);
        text = ProductKeyRegex().Replace(text, Placeholder);
        text = KeyValueSecretRegex().Replace(text, m => m.Groups["key"].Value + m.Groups["sep"].Value + Placeholder);
        text = UserProfilePathRegex().Replace(text, m => m.Groups["prefix"].Value + "[USER]");
        return text;
    }

    // Khóa khôi phục BitLocker: 8 nhóm 6 chữ số.
    [GeneratedRegex(@"\b\d{6}(?:-\d{6}){7}\b", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex BitLockerKeyRegex();

    // Khóa sản phẩm dạng XXXXX-XXXXX-XXXXX-XXXXX-XXXXX.
    [GeneratedRegex(@"\b[A-Z0-9]{5}(?:-[A-Z0-9]{5}){4}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex ProductKeyRegex();

    [GeneratedRegex(@"\beyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex JwtRegex();

    [GeneratedRegex(@"\bbearer\s+[A-Za-z0-9\-._~+/]+=*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex BearerRegex();

    [GeneratedRegex(
        @"(?<key>\b(?:password|passwd|pwd|secret|token|api[_-]?key|access[_-]?key|client[_-]?secret|authorization|mat[_-]?khau)\b)(?<sep>\s*[:=]\s*)(?:""[^""]*""|'[^']*'|\S+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 200)]
    private static partial Regex KeyValueSecretRegex();

    // Ẩn tên tài khoản Windows trong đường dẫn hồ sơ người dùng.
    [GeneratedRegex(@"(?<prefix>\b[A-Za-z]:\\Users\\)[^\\\s""']+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex UserProfilePathRegex();
}
