using System.Text.RegularExpressions;

namespace LamMotCham.Windows.Software;

/// <summary>Một gói trong danh mục được duyệt. Chỉ gói có trong danh mục mới được cài.</summary>
public sealed record SoftwarePackage(
    string Key,
    string Name,
    string Description,
    string Category,
    string WinGetId,
    string ExpectedPublisher,
    bool Recommended,
    string IconKey,
    bool MachineWide);

/// <summary>
/// Danh mục phần mềm đóng gói trong ứng dụng (allowlist). Package ID và publisher đã được đối chiếu với
/// kho microsoft/winget-pkgs ngày 18/10/2026. Bản thử nghiệm — PO cần duyệt trước khi phát hành (Q-02).
/// </summary>
public static partial class SoftwareCatalog
{
    /// <summary>Phiên bản dữ liệu của danh mục (mô-đun dữ liệu "catalog", QĐ-002). Đổi khi thêm/bớt gói hoặc logo.</summary>
    public const string DataVersion = "2026.10.18";

    public static IReadOnlyList<SoftwarePackage> All { get; } =
    [
        new("chrome", "Google Chrome", "Trình duyệt web", "Trình duyệt", "Google.Chrome", "Google LLC", true, "globe", true),
        new("brave", "Brave", "Trình duyệt chặn quảng cáo", "Trình duyệt", "Brave.Brave", "Brave Software Inc", false, "globe", true),
        new("unikey", "UniKey", "Bộ gõ tiếng Việt", "Tiện ích", "UniKey.UniKey", "unikey.org", true, "keyboard", false),
        new("7zip", "7-Zip", "Nén và giải nén", "Tiện ích", "7zip.7zip", "Igor Pavlov", true, "archive", true),
        new("acrobat", "Adobe Acrobat Reader", "Đọc và ký file PDF", "Văn phòng", "Adobe.Acrobat.Reader.64-bit", "Adobe", true, "doc", true),
        new("sumatra", "SumatraPDF", "Trình đọc PDF nhẹ", "Văn phòng", "SumatraPDF.SumatraPDF", "Krzysztof Kowalczyk", false, "doc", false),
        new("vlc", "VLC media player", "Phát video và nhạc", "Media", "VideoLAN.VLC", "VideoLAN", true, "play", true),
        new("m365", "Microsoft 365 Apps", "Word, Excel, PowerPoint — cần bản quyền", "Văn phòng", "Microsoft.Office", "Microsoft Corporation", false, "grid", true),
        new("zalo", "Zalo", "Nhắn tin, gọi video", "Liên lạc", "VNGCorp.Zalo", "VNG Corp.", false, "chat", false),
        new("zoom", "Zoom Workplace", "Họp trực tuyến", "Liên lạc", "Zoom.Zoom", "Zoom", false, "video", true),
    ];

    public static IReadOnlyList<string> Categories { get; } = ["Trình duyệt", "Văn phòng", "Tiện ích", "Media", "Liên lạc"];

    /// <summary>Package ID chỉ gồm ký tự an toàn; không bao giờ chứa khoảng trắng hay ký tự shell.</summary>
    public static bool IsSafeId(string? id) => id is not null && SafeIdRegex().IsMatch(id);

    /// <summary>
    /// Logo thật đã đóng gói trong ứng dụng (App/Assets/Logos/{key}.png), lấy từ kho mã nguồn chính thức của nhà phát hành.
    /// Gói chưa có logo trả về null và giao diện dùng icon theo danh mục.
    /// </summary>
    private static readonly HashSet<string> BundledLogos = new(StringComparer.Ordinal)
    {
        "chrome", "brave", "acrobat", "7zip", "vlc", "m365", "zalo", "zoom",
    };

    public static string? LogoKeyFor(string packageKey) => BundledLogos.Contains(packageKey) ? packageKey : null;

    public static SoftwarePackage? Find(string winGetId) =>
        All.FirstOrDefault(p => string.Equals(p.WinGetId, winGetId, StringComparison.Ordinal));

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9.\-_+]{1,127}\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex SafeIdRegex();
}
