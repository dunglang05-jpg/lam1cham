using System.Globalization;
using System.Text.RegularExpressions;

namespace LamMotCham.Windows.Disk;

/// <summary>Thông tin đọc được về ổ cần chia (chỉ đọc, từ PowerShell Storage).</summary>
public sealed record DiskSplitProbe
{
    public required char DriveLetter { get; init; }

    /// <summary>Kích thước phân vùng hiện tại (byte).</summary>
    public required long PartitionSize { get; init; }

    /// <summary>Kích thước nhỏ nhất Windows cho phép thu về (Get-PartitionSupportedSize.SizeMin).</summary>
    public required long SupportedMin { get; init; }

    public required long FreeBytes { get; init; }

    public required string FileSystem { get; init; }

    public required int DiskNumber { get; init; }

    /// <summary>GPT, MBR hoặc RAW.</summary>
    public required string PartitionStyle { get; init; }

    public required int PartitionCount { get; init; }

    /// <summary>Win32_EncryptableVolume.GetConversionStatus: 0 chưa mã hóa, 1 đã mã hóa, 2–5 đang mã hóa/giải mã/tạm dừng; null không đọc được.</summary>
    public int? ConversionStatus { get; init; }

    /// <summary>Ký tự ổ còn trống đầu tiên (D–Z), null nếu hết.</summary>
    public char? FreeLetter { get; init; }
}

/// <summary>Yêu cầu chia ổ do kỹ thuật viên nhập.</summary>
public sealed record DiskSplitRequest(char SourceLetter, double NewSizeGb, string Label);

/// <summary>Kết quả xem trước: con số cụ thể, lỗi chặn và cảnh báo.</summary>
public sealed record DiskSplitPlan
{
    public required DiskSplitRequest Request { get; init; }

    public required DiskSplitProbe Probe { get; init; }

    public required long NewPartitionBytes { get; init; }

    /// <summary>Kích thước ổ nguồn sau khi thu nhỏ (byte).</summary>
    public required long TargetSourceSize { get; init; }

    /// <summary>Dung lượng tối đa có thể tách ra (đã chừa trống cho ổ nguồn).</summary>
    public required long MaxNewBytes { get; init; }

    public required char NewLetter { get; init; }

    public required IReadOnlyList<string> Errors { get; init; }

    public required IReadOnlyList<string> Warnings { get; init; }

    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// Quy tắc an toàn khi chia ổ: chỉ thu nhỏ ổ NTFS có sẵn rồi tạo ổ mới từ vùng trống vừa tách;
/// không xóa hay định dạng phân vùng cũ. Ổ nguồn phải còn trống ít nhất 30 GB (hoặc 15%) sau khi chia.
/// </summary>
public static partial class DiskSplitPlanner
{
    public const long GiB = 1L << 30;

    public const double MinNewGb = 10;

    /// <summary>Ổ nguồn phải còn trống ít nhất chừng này sau khi chia.</summary>
    public const long MinSourceFreeAfter = 30 * GiB;

    /// <summary>Chừa thêm so với giới hạn của Windows để thu nhỏ không sát mép.</summary>
    public const long SafetyMargin = 2 * GiB;

    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    /// <summary>Nhãn ổ mới: 1–32 ký tự chữ không dấu, số, khoảng trắng, gạch ngang, gạch dưới.</summary>
    public static bool IsValidLabel(string? label) => label is not null && LabelRegex().IsMatch(label);

    public static bool IsValidLetter(char letter) => letter is >= 'C' and <= 'Z';

    public static DiskSplitPlan Plan(DiskSplitProbe probe, DiskSplitRequest request)
    {
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(request);

        var errors = new List<string>();
        var warnings = new List<string>();
        var letter = probe.DriveLetter;

        var newBytes = double.IsFinite(request.NewSizeGb) && request.NewSizeGb > 0
            ? (long)(request.NewSizeGb * GiB) / (1L << 20) * (1L << 20)
            : 0;
        var maxNew = MaxNewBytes(probe);

        if (!string.Equals(probe.FileSystem, "NTFS", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"Chỉ chia được ổ NTFS (ổ {letter}: là {Display(probe.FileSystem)}).");
        }

        if (string.Equals(probe.PartitionStyle, "RAW", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Ổ đĩa vật lý chưa được khởi tạo bảng phân vùng.");
        }
        else if (string.Equals(probe.PartitionStyle, "MBR", StringComparison.OrdinalIgnoreCase) && probe.PartitionCount >= 4)
        {
            errors.Add("Ổ đĩa kiểu MBR đã có 4 phân vùng, không tạo thêm phân vùng chính được.");
        }

        switch (probe.ConversionStatus)
        {
            case >= 2 and <= 5:
                errors.Add($"BitLocker đang mã hóa hoặc giải mã ổ {letter}:. Chờ xong rồi chia ổ.");
                break;
            case 1:
                warnings.Add($"Ổ {letter}: đang được BitLocker mã hóa. Ổ mới sẽ không được mã hóa.");
                break;
            case null:
                warnings.Add("Không đọc được trạng thái BitLocker.");
                break;
        }

        if (probe.FreeLetter is not { } newLetter)
        {
            errors.Add("Không còn ký tự ổ trống (D–Z) để gán cho ổ mới.");
            newLetter = '?';
        }

        if (!IsValidLabel(request.Label))
        {
            errors.Add("Tên ổ mới: 1–32 ký tự chữ không dấu, số, khoảng trắng, dấu gạch.");
        }

        if (maxNew < (long)(MinNewGb * GiB))
        {
            errors.Add($"Ổ {letter}: không đủ chỗ trống để tách (cần chừa ít nhất {Gb(MinSourceFreeAfter)} trống và Windows chỉ cho thu nhỏ đến {Gb(probe.SupportedMin)}).");
        }
        else if (newBytes < (long)(MinNewGb * GiB))
        {
            errors.Add($"Ổ mới phải từ {MinNewGb:0} GB.");
        }
        else if (newBytes > maxNew)
        {
            errors.Add($"Chỉ tách được tối đa {Gb(maxNew)} để ổ {letter}: vẫn còn đủ chỗ trống.");
        }

        return new DiskSplitPlan
        {
            Request = request,
            Probe = probe,
            NewPartitionBytes = newBytes,
            TargetSourceSize = probe.PartitionSize - newBytes,
            MaxNewBytes = Math.Max(0, maxNew),
            NewLetter = newLetter,
            Errors = errors,
            Warnings = warnings,
        };
    }

    /// <summary>Tối đa tách được: không vượt giới hạn thu nhỏ của Windows, và ổ nguồn còn trống đủ.</summary>
    public static long MaxNewBytes(DiskSplitProbe probe)
    {
        ArgumentNullException.ThrowIfNull(probe);
        var byShrink = probe.PartitionSize - probe.SupportedMin - SafetyMargin;
        var reserve = Math.Max(MinSourceFreeAfter, (long)(probe.PartitionSize * 0.15));
        var byFree = probe.FreeBytes - reserve;
        var max = Math.Min(byShrink, byFree);
        return max <= 0 ? 0 : max / GiB * GiB;
    }

    public static string Gb(long bytes) => (bytes / (double)GiB).ToString("0.#", Vi) + " GB";

    private static string Display(string? fs) => string.IsNullOrWhiteSpace(fs) ? "không rõ" : fs;

    [GeneratedRegex(@"^[A-Za-z0-9 _\-]{1,32}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex LabelRegex();
}
