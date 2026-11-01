using System.Text.RegularExpressions;

namespace LamMotCham.Windows.Software;

/// <summary>Cách cài khi tải bằng link (định dạng ứng dụng của Web Admin).</summary>
public enum InstallKind
{
    /// <summary>File cài .exe, chạy với tham số cài im lặng.</summary>
    Exe,

    /// <summary>Gói .msi, cài bằng msiexec /qn.</summary>
    Msi,

    /// <summary>File .zip giải nén vào Program Files và tạo lối tắt.</summary>
    Zip,

    /// <summary>Không cài: tạo lối tắt trang web (ứng dụng) hoặc mở link tải trên trình duyệt (phiên bản).</summary>
    Web,
}

/// <summary>Một phiên bản để chọn (ví dụ Office Home 2024 tiếng Việt). Kind = Web nghĩa là mở link để tải tay.</summary>
public sealed record PackageVariant(string Label, InstallKind Kind, Uri Url, string SilentArgs);

/// <summary>
/// Một ứng dụng trong danh mục. Danh mục lấy từ Web Admin sau khi đăng nhập (QĐ-006); khi không lấy được thì dùng
/// danh mục có sẵn trong phần mềm (<see cref="SoftwareCatalog.All"/>). Chỉ ứng dụng có trong danh mục đang dùng mới được cài.
/// </summary>
public sealed record SoftwarePackage
{
    /// <summary>Khóa ổn định, dùng làm ID task ("sw." + Key) và tên logo.</summary>
    public required string Key { get; init; }

    public required string Name { get; init; }

    public string Description { get; init; } = "";

    public string Category { get; init; } = SoftwareCatalog.OtherCategory;

    /// <summary>Package ID trên nguồn "winget"; null nếu không cài bằng WinGet.</summary>
    public string? WinGetId { get; init; }

    /// <summary>Nhà phát hành của gói trên WinGet (phải khớp mới cài). Rỗng thì không cài bằng WinGet.</summary>
    public string ExpectedPublisher { get; init; } = "";

    /// <summary>Chọn sẵn ("tick sẵn" trên Admin).</summary>
    public bool Recommended { get; init; }

    public string IconKey { get; init; } = "grid";

    /// <summary>Cài cho toàn máy (WinGet <c>--scope machine</c>); false: theo người dùng.</summary>
    public bool MachineWide { get; init; }

    /// <summary>Chữ đại diện khi không có logo (1–2 ký tự).</summary>
    public string Initial { get; init; } = "";

    /// <summary>Màu nền chữ đại diện, dạng #RRGGBB.</summary>
    public string Color { get; init; } = "#3B5BDB";

    public InstallKind Kind { get; init; } = InstallKind.Exe;

    public Uri? DirectUrl { get; init; }

    public Uri? PageUrl { get; init; }

    /// <summary>Mẫu tìm link mới nhất trên <see cref="PageUrl"/>.</summary>
    public string? PageRegex { get; init; }

    public string SilentArgs { get; init; } = "";

    public Uri? Website { get; init; }

    public string? ZipFolder { get; init; }

    public string? ZipExe { get; init; }

    public IReadOnlyList<PackageVariant> Variants { get; init; } = [];

    /// <summary>Tên trong "Installed apps" để xác minh đã cài.</summary>
    public IReadOnlyList<string> DetectNames { get; init; } = [];

    /// <summary>Đường dẫn file (có biến môi trường) để xác minh đã cài.</summary>
    public IReadOnlyList<string> DetectPaths { get; init; } = [];

    /// <summary>null: cài được. Khác null: hiện mờ trên laptop kèm lý do.</summary>
    public string? UnavailableReason { get; init; }

    public bool IsAvailable => UnavailableReason is null;

    /// <summary>Cài được bằng WinGet (có mã và nhà phát hành để đối chiếu).</summary>
    public bool UsesWinGet => WinGetId is not null && ExpectedPublisher.Length > 0;

    /// <summary>Có link tải (trực tiếp hoặc dò trên trang).</summary>
    public bool HasLink => DirectUrl is not null || (PageUrl is not null && PageRegex is not null);

    public bool HasVariants => Variants.Count > 0;

    /// <summary>Nguồn hiển thị/ghi báo cáo: mã WinGet hoặc tên miền link tải.</summary>
    public string SourceText =>
        UsesWinGet ? WinGetId! :
        HasVariants ? "Chọn phiên bản" :
        Kind == InstallKind.Web ? Website?.Host ?? "" :
        (DirectUrl ?? PageUrl)?.Host ?? "";
}

/// <summary>
/// Danh mục phần mềm. <see cref="All"/> là danh mục đóng gói sẵn (dùng khi chưa lấy được danh sách từ Web Admin);
/// <see cref="Current"/> là danh mục đang dùng trong lần chạy này (allowlist cho WinGet và tải link).
/// Package ID và nhà phát hành đã đối chiếu với kho microsoft/winget-pkgs ngày 30/10/2026.
/// </summary>
public static partial class SoftwareCatalog
{
    /// <summary>Phiên bản dữ liệu của danh mục (mô-đun dữ liệu "catalog", QĐ-002). Đổi khi thêm/bớt gói, logo hoặc hãng tin cậy.</summary>
    public const string DataVersion = "2026.11.02.1";

    public const string OtherCategory = "Khác";

    private static volatile IReadOnlyList<SoftwarePackage>? _current;

    public static IReadOnlyList<SoftwarePackage> All { get; } =
    [
        Builtin("chrome", "Google Chrome", "Trình duyệt web", "Trình duyệt", "Google.Chrome", "Google LLC", true, "globe", true),
        Builtin("brave", "Brave", "Trình duyệt chặn quảng cáo", "Trình duyệt", "Brave.Brave", "Brave Software Inc", false, "globe", true),
        Builtin("unikey", "UniKey", "Bộ gõ tiếng Việt", "Tiện ích", "UniKey.UniKey", "unikey.org", true, "keyboard", false),
        Builtin("7zip", "7-Zip", "Nén và giải nén", "Tiện ích", "7zip.7zip", "Igor Pavlov", true, "archive", true),
        Builtin("acrobat", "Adobe Acrobat Reader", "Đọc và ký file PDF", "Văn phòng", "Adobe.Acrobat.Reader.64-bit", "Adobe", true, "doc", true),
        Builtin("sumatra", "SumatraPDF", "Trình đọc PDF nhẹ", "Văn phòng", "SumatraPDF.SumatraPDF", "Krzysztof Kowalczyk", false, "doc", false),
        Builtin("vlc", "VLC media player", "Phát video và nhạc", "Media", "VideoLAN.VLC", "VideoLAN", true, "play", true),
        Builtin("m365", "Microsoft 365 Apps", "Word, Excel, PowerPoint — cần bản quyền", "Văn phòng", "Microsoft.Office", "Microsoft Corporation", false, "grid", true),
        Builtin("zalo", "Zalo", "Nhắn tin, gọi video", "Liên lạc", "VNGCorp.Zalo", "VNG Corp.", false, "chat", false),
        Builtin("zoom", "Zoom Workplace", "Họp trực tuyến", "Liên lạc", "Zoom.Zoom", "Zoom", false, "video", true),
    ];

    /// <summary>Danh mục đang dùng: danh sách từ Web Admin nếu đã lấy được, ngược lại là <see cref="All"/>.</summary>
    public static IReadOnlyList<SoftwarePackage> Current => _current ?? All;

    /// <summary>true khi đang dùng danh sách lấy từ Web Admin.</summary>
    public static bool IsFromServer => _current is not null;

    /// <summary>Nhóm hiển thị, theo thứ tự xuất hiện trong danh mục đang dùng.</summary>
    public static IReadOnlyList<string> Categories =>
        [.. Current.Select(p => p.Category).Where(c => c.Length > 0).Distinct(StringComparer.Ordinal)];

    /// <summary>Dùng danh mục lấy từ Web Admin (đã kiểm tra). null: quay về danh mục có sẵn.</summary>
    public static void Use(IReadOnlyList<SoftwarePackage>? packages) => _current = packages is { Count: > 0 } ? packages : null;

    /// <summary>Package ID chỉ gồm ký tự an toàn; không bao giờ chứa khoảng trắng hay ký tự shell.</summary>
    public static bool IsSafeId(string? id) => id is not null && SafeIdRegex().IsMatch(id);

    /// <summary>
    /// Logo thật đã đóng gói trong ứng dụng (App/Assets/Logos/{key}.png), lấy từ kho mã nguồn chính thức của nhà phát hành.
    /// Gói chưa có logo trả về null và giao diện dùng chữ đại diện hoặc icon theo danh mục.
    /// </summary>
    private static readonly HashSet<string> BundledLogos = new(StringComparer.Ordinal)
    {
        "chrome", "brave", "acrobat", "7zip", "vlc", "m365", "zalo", "zoom",
    };

    public static string? LogoKeyFor(string packageKey)
    {
        if (BundledLogos.Contains(packageKey))
        {
            return packageKey;
        }

        // Hai ứng dụng cùng logo (ví dụ "Microsoft 365" và "Microsoft Office") có khóa "m365" và "m365-2".
        var dash = packageKey.LastIndexOf('-');
        return dash > 0 && packageKey[(dash + 1)..].All(char.IsAsciiDigit) && BundledLogos.Contains(packageKey[..dash]) ? packageKey[..dash] : null;
    }

    /// <summary>Gói WinGet trong danh mục đang dùng (allowlist trước khi gọi winget).</summary>
    public static SoftwarePackage? Find(string winGetId) =>
        Current.FirstOrDefault(p => p.UsesWinGet && string.Equals(p.WinGetId, winGetId, StringComparison.Ordinal));

    private static SoftwarePackage Builtin(
        string key, string name, string description, string category, string winGetId, string publisher, bool recommended, string icon, bool machineWide) =>
        new()
        {
            Key = key,
            Name = name,
            Description = description,
            Category = category,
            WinGetId = winGetId,
            ExpectedPublisher = publisher,
            Recommended = recommended,
            IconKey = icon,
            MachineWide = machineWide,
            Initial = name[..1],
        };

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9.\-_+]{1,127}\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex SafeIdRegex();
}
