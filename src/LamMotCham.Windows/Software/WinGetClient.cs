using System.Globalization;
using LamMotCham.Core.Processes;

namespace LamMotCham.Windows.Software;

public enum InstallPresence
{
    Installed,
    NotInstalled,
    Unknown,
}

public sealed record PresenceResult(InstallPresence Presence, string? Detail = null);

/// <summary>Ý nghĩa một mã thoát WinGet (lấy từ tài liệu returnCodes của winget-cli).</summary>
public sealed record WinGetCodeInfo(string Message, bool IsTransient, bool CountsAsInstalled, bool NeedsReboot);

/// <summary>
/// Gọi WinGet bằng danh sách tham số tách rời (không qua shell) và chỉ với package ID trong danh mục.
/// </summary>
public sealed partial class WinGetClient
{
    public const int NoApplicationsFound = unchecked((int)0x8A150014);

    /// <summary>Gói không có bộ cài phù hợp (thường vì không hỗ trợ phạm vi đã yêu cầu).</summary>
    public const int NoApplicableInstaller = unchecked((int)0x8A150010);

    private static readonly Dictionary<uint, WinGetCodeInfo> Codes = new()
    {
        [0x8A150008] = new("Không tải được bộ cài.", true, false, false),
        [0x8A150107] = new("Mất kết nối mạng trong khi cài.", true, false, false),
        [0x8A150102] = new("Đang có một trình cài đặt khác chạy trên máy.", true, false, false),
        [0x8A150101] = new("Ứng dụng đang mở. Đóng ứng dụng rồi thử lại.", false, false, false),
        [0x8A150103] = new("Có file đang được dùng. Đóng các ứng dụng liên quan rồi thử lại.", false, false, false),
        [0x8A150111] = new("Ứng dụng đang được một ứng dụng khác sử dụng.", false, false, false),
        [0x8A150104] = new("Máy thiếu thành phần phụ thuộc mà gói cần.", false, false, false),
        [0x8A150110] = new("Không cài được thành phần phụ thuộc của gói.", false, false, false),
        [0x8A150105] = new("Ổ đĩa không còn đủ dung lượng.", false, false, false),
        [0x8A150106] = new("Không đủ RAM để cài. Đóng bớt ứng dụng rồi thử lại.", false, false, false),
        [0x8A15010F] = new("Chính sách của tổ chức đang chặn việc cài đặt.", false, false, false),
        [0x8A15010C] = new("Trình cài đặt bị hủy (có thể do từ chối quyền quản trị).", false, false, false),
        [0x8A150113] = new("Gói không hỗ trợ cấu hình máy này.", false, false, false),
        [0x8A150108] = new("Trình cài đặt báo lỗi; cần liên hệ nhà phát hành.", false, false, false),
        [0x8A150112] = new("Tham số cài đặt không hợp lệ.", false, false, false),
        [0x8A150014] = new("Không tìm thấy gói trong nguồn WinGet.", false, false, false),
        [0x8A150010] = new("Gói không có bộ cài phù hợp với máy hoặc phạm vi cài này.", false, false, false),
        [0x8A150061] = new("Gói đã được cài sẵn.", false, true, false),
        [0x8A15010D] = new("Đã có một phiên bản khác của ứng dụng.", false, true, false),
        [0x8A15010E] = new("Đã có phiên bản mới hơn trên máy.", false, true, false),
        [0x8A150109] = new("Cần khởi động lại máy để hoàn tất cài đặt.", false, true, true),
        [0x8A15010A] = new("Cần khởi động lại máy rồi cài lại.", false, false, true),
        [0x8A15010B] = new("Trình cài đặt yêu cầu khởi động lại máy.", false, true, true),
    };

    private readonly Func<string?> _locator;

    public WinGetClient(Func<string?>? locator = null)
    {
        _locator = locator ?? Locate;
    }

    public static WinGetCodeInfo Describe(int exitCode) =>
        Codes.TryGetValue(unchecked((uint)exitCode), out var info)
            ? info
            : new WinGetCodeInfo("WinGet báo lỗi không xác định.", false, false, false);

    /// <summary>Tìm winget.exe ở vị trí chuẩn của App Installer; không tìm theo PATH để tránh chạy nhầm file.</summary>
    public static string? Locate()
    {
        var alias = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "WindowsApps", "winget.exe");
        if (File.Exists(alias))
        {
            return alias;
        }

        try
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps");
            return Directory.GetDirectories(root, "Microsoft.DesktopAppInstaller_*_x64__8wekyb3d8bbwe")
                .Select(d => Path.Combine(d, "winget.exe"))
                .Where(File.Exists)
                .OrderByDescending(p => p, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    public bool IsAvailable => _locator() is not null;

    public async Task<string?> GetVersionAsync(CancellationToken cancellationToken)
    {
        var exe = _locator();
        if (exe is null)
        {
            return null;
        }

        var result = await ProcessRunner.RunAsync(exe, ["--version"], cancellationToken, 4_000).ConfigureAwait(false);
        var text = result.StandardOutput.Trim();
        return result.ExitCode == 0 && text.StartsWith('v') ? text : null;
    }

    public static Version? ParseVersion(string? text) =>
        text is not null && Version.TryParse(text.TrimStart('v').Split('-')[0], out var v) ? v : null;

    public async Task<PresenceResult> GetPresenceAsync(string packageId, CancellationToken cancellationToken)
    {
        var exe = RequireExe();
        EnsureSafe(packageId);
        var result = await ProcessRunner.RunAsync(
            exe,
            ["list", "--id", packageId, "--exact", "--source", "winget", "--accept-source-agreements", "--disable-interactivity"],
            cancellationToken,
            16_000).ConfigureAwait(false);

        if (result.ExitCode == 0 && result.StandardOutput.Contains(packageId, StringComparison.OrdinalIgnoreCase))
        {
            return new PresenceResult(InstallPresence.Installed);
        }

        if (result.ExitCode == NoApplicationsFound || result.ExitCode == 0)
        {
            return new PresenceResult(InstallPresence.NotInstalled);
        }

        return new PresenceResult(InstallPresence.Unknown, $"winget list trả về {result.ExitCodeHex}");
    }

    /// <param name="packageId">Package ID trong danh mục.</param>
    /// <param name="machineScope">true: cài cho toàn máy (<c>--scope machine</c>, QĐ-001 Q-04); false: để WinGet chọn phạm vi mặc định.</param>
    /// <param name="cancellationToken">Ngừng chờ (không dừng trình cài đặt).</param>
    public Task<ProcessResult> InstallAsync(string packageId, bool machineScope, CancellationToken cancellationToken) =>
        InstallAsync(packageId, machineScope, locale: null, cancellationToken);

    /// <param name="packageId">Package ID trong danh mục đang dùng.</param>
    /// <param name="machineScope">true: cài cho toàn máy.</param>
    /// <param name="locale">Ngôn ngữ bộ cài ưu tiên (ví dụ vi-VN); null: để WinGet chọn.</param>
    /// <param name="cancellationToken">Ngừng chờ (không dừng trình cài đặt).</param>
    public Task<ProcessResult> InstallAsync(string packageId, bool machineScope, string? locale, CancellationToken cancellationToken)
    {
        var exe = RequireExe();
        EnsureSafe(packageId);
        return ProcessRunner.RunAsync(exe, InstallArguments(packageId, machineScope, locale), cancellationToken, killOnCancel: false);
    }

    /// <summary>Tham số cài đặt; tách riêng để kiểm tra được.</summary>
    internal static IReadOnlyList<string> InstallArguments(string packageId, bool machineScope) => InstallArguments(packageId, machineScope, null);

    internal static IReadOnlyList<string> InstallArguments(string packageId, bool machineScope, string? locale)
    {
        List<string> args =
        [
            "install", "--id", packageId, "--exact", "--source", "winget", "--silent",
            "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity",
        ];
        if (locale is not null && LocaleRegex().IsMatch(locale))
        {
            args.AddRange(["--locale", locale]);
        }

        if (machineScope)
        {
            args.AddRange(["--scope", "machine"]);
        }

        return args;
    }

    /// <summary>
    /// Đối chiếu nhà phát hành của gói trên nguồn WinGet với danh mục (chống package ID bị đổi chủ hoặc gõ nhầm).
    /// Trả về null nếu khớp, ngược lại là lý do không cài.
    /// </summary>
    public async Task<string?> CheckPublisherAsync(SoftwarePackage package, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(package);
        var exe = RequireExe();
        var id = package.WinGetId ?? throw new ArgumentException("Gói không có mã WinGet.", nameof(package));
        EnsureSafe(id);
        var result = await ProcessRunner.RunAsync(
            exe,
            ["show", "--id", id, "--exact", "--source", "winget", "--accept-source-agreements", "--disable-interactivity"],
            cancellationToken,
            32_000).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            return $"Không đọc được thông tin gói trên nguồn WinGet ({result.ExitCodeHex}: {Describe(result.ExitCode).Message}). Chưa cài.";
        }

        return PublisherMatches(result.StandardOutput, package.ExpectedPublisher)
            ? null
            : $"Nhà phát hành của {id} trên nguồn WinGet không khớp danh mục (cần: {package.ExpectedPublisher}). Không cài để tránh gói giả mạo.";
    }

    /// <summary>
    /// Có dòng "nhãn: giá trị" nào mà giá trị bắt đầu bằng tên nhà phát hành trong danh mục không.
    /// Không dựa vào chữ "Publisher" vì nhãn của WinGet đổi theo ngôn ngữ Windows.
    /// </summary>
    internal static bool PublisherMatches(string showOutput, string expectedPublisher)
    {
        if (string.IsNullOrWhiteSpace(showOutput) || string.IsNullOrWhiteSpace(expectedPublisher))
        {
            return false;
        }

        foreach (var line in showOutput.Split('\n'))
        {
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0 || colon > 40)
            {
                continue;
            }

            var value = line[(colon + 1)..].Trim();
            if (value.StartsWith(expectedPublisher.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public Task<ProcessResult> UninstallAsync(string packageId, CancellationToken cancellationToken)
    {
        var exe = RequireExe();
        EnsureSafe(packageId);
        return ProcessRunner.RunAsync(
            exe,
            ["uninstall", "--id", packageId, "--exact", "--source", "winget", "--silent", "--disable-interactivity", "--accept-source-agreements"],
            cancellationToken,
            killOnCancel: false);
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"^[a-z]{2,3}(-[A-Z]{2})?\z", System.Text.RegularExpressions.RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial System.Text.RegularExpressions.Regex LocaleRegex();

    private string RequireExe() =>
        _locator() ?? throw new InvalidOperationException("WinGet không khả dụng trên máy này.");

    private static void EnsureSafe(string packageId)
    {
        if (!SoftwareCatalog.IsSafeId(packageId) || SoftwareCatalog.Find(packageId) is null)
        {
            throw new ArgumentException(
                string.Create(CultureInfo.InvariantCulture, $"Package ID không nằm trong danh mục đang dùng: '{packageId}'."),
                nameof(packageId));
        }
    }
}
