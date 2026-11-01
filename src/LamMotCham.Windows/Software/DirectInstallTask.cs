using System.Diagnostics;
using System.IO.Compression;
using LamMotCham.Core.Logging;
using LamMotCham.Core.Processes;
using LamMotCham.Core.Tasks;
using LamMotCham.Windows.Infrastructure;

namespace LamMotCham.Windows.Software;

/// <summary>Số liệu một lần cài, gửi lên mục "Lịch sử cài" của Web Admin.</summary>
public sealed record InstallMetrics
{
    public string Source { get; init; } = "";

    public long Bytes { get; init; }

    public TimeSpan Download { get; init; }

    /// <summary>Thời gian thật sự phải chờ tải (file tải trước trong lúc cài app khác thì gần 0).</summary>
    public TimeSpan Wait { get; init; }

    public TimeSpan Install { get; init; }

    public int ExitCode { get; init; }

    public bool AlreadyInstalled { get; init; }
}

/// <summary>
/// Cài bằng link của Web Admin (QĐ-006): tải qua https, bắt buộc chữ ký số hợp lệ của hãng trong
/// <see cref="TrustedPublishers"/>, chạy trình cài với tham số đã kiểm tra (không qua shell), rồi xác minh bằng dữ liệu nhận biết.
/// Phiên bản loại "Mở link" chỉ mở trang tải trên trình duyệt để kỹ thuật viên cài tay.
/// </summary>
public sealed class DirectInstallTask
{
    private const int RebootInitiated = 1641;
    private const int RebootRequired = 3010;
    private const int AnotherInstallRunning = 1618;
    private const int UserCancelled = 1602;

    public DirectInstallTask(SoftwarePackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        Package = package;
        SelectedVariant = package.Variants.Count > 0 ? package.Variants[0] : null;
    }

    public SoftwarePackage Package { get; }

    /// <summary>Phiên bản người dùng chọn (ứng dụng có nhiều phiên bản). Mặc định là phiên bản đầu tiên trên Admin.</summary>
    public PackageVariant? SelectedVariant { get; set; }

    public InstallMetrics? LastMetrics { get; private set; }

    /// <summary>Có cách cài bằng link không (link trực tiếp, dò link hoặc phiên bản).</summary>
    public bool CanInstall => Package.HasVariants ? SelectedVariant is not null : Package.HasLink;

    /// <summary>Cài tự động được bằng link (không phải phiên bản "Mở link" để cài tay).</summary>
    public bool IsAutomatic => CanInstall && EffectiveKind != InstallKind.Web;

    /// <summary>Lần chạy gần nhất đã khởi chạy trình cài (sau bước tải và kiểm tra chữ ký).</summary>
    public bool InstallerStarted { get; private set; }

    internal InstallKind EffectiveKind => SelectedVariant?.Kind ?? Package.Kind;

    /// <summary>Link tải cho lần cài này: link của phiên bản đã chọn, hoặc link trực tiếp/dò link mới nhất.</summary>
    internal async Task<(Uri? Url, string Note)> ResolveAsync(CancellationToken cancellationToken) =>
        SelectedVariant is { } variant
            ? (variant.Url, $"Phiên bản \"{variant.Label}\".")
            : await LinkDownloader.ResolveAsync(Package, cancellationToken).ConfigureAwait(false);

    public async Task<ExecuteResult> ExecuteAsync(TaskContext context, string taskId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        LastMetrics = null;
        InstallerStarted = false;

        if (SelectedVariant is { Kind: InstallKind.Web } link)
        {
            OpenInBrowser(link.Url);
            context.Log.Info($"Mở link tải phiên bản \"{link.Label}\" ({link.Url.Host}) trên trình duyệt.", taskId);
            LastMetrics = new InstallMetrics { Source = link.Url.Host };
            return ExecuteResult.Completed($"Đã mở trang tải \"{link.Label}\" trên trình duyệt.");
        }

        DownloadedFile file;
        TimeSpan waited;
        var prefetched = LinkPrefetcher.Current is { } prefetcher
            ? await prefetcher.TakeAsync(this, cancellationToken, InstallProgress.Download(context)).ConfigureAwait(false)
            : null;
        if (prefetched is not null)
        {
            file = prefetched.File;
            waited = prefetched.Waited;
            context.Log.Info($"{prefetched.Note} Dùng file đã tải trước từ {file.Source.Host} (phải chờ thêm {waited.TotalSeconds:0} giây).", taskId);
        }
        else
        {
            var (url, note) = await ResolveAsync(cancellationToken).ConfigureAwait(false);
            if (url is null)
            {
                return ExecuteResult.Failed(note, isTransient: true);
            }

            context.Log.Info($"{note} Tải từ {url.Host}.", taskId);
            try
            {
                context.Report(ProgressSteps.Execute, "Đang tải");
                file = await LinkDownloader.DownloadAsync(url, EffectiveKind, cancellationToken, InstallProgress.Download(context)).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                return ExecuteResult.Failed($"Không tải được file cài từ {url.Host}: {ex.Message}", isTransient: IsTransientDownloadError(ex));
            }

            waited = file.Elapsed;
        }

        using (file)
        {
            context.Log.Info($"Đã tải {file.Bytes / 1048576.0:0.0} MB trong {file.Elapsed.TotalSeconds:0} giây.", taskId);
            var metrics = new InstallMetrics { Source = file.Source.Host, Bytes = file.Bytes, Download = file.Elapsed, Wait = waited };
            LastMetrics = metrics;

            if (EffectiveKind == InstallKind.Zip)
            {
                return await InstallZipAsync(file, metrics, context, taskId).ConfigureAwait(false);
            }

            context.Report(InstallProgress.DownloadShare, "Đang kiểm tra chữ ký số");
            var signature = Authenticode.Check(file.Path);
            context.Log.Info($"Chữ ký số: {signature.Detail}", taskId);
            if (!signature.IsTrusted)
            {
                return ExecuteResult.Failed(signature.IsValid
                    ? $"File cài ký bởi \"{signature.SignerText}\", không nằm trong danh sách hãng tin cậy của Làm 1 Chạm. Không cài."
                    : $"{signature.Detail} Không cài file không có chữ ký số hợp lệ.");
            }

            var args = SplitArgs(SelectedVariant?.SilentArgs ?? Package.SilentArgs);
            var watch = Stopwatch.StartNew();
            InstallerStarted = true;
            ProcessResult result;
            using (InstallProgress.Pulse(context, InstallProgress.DownloadShare, InstallProgress.BeforeVerify, TimeSpan.FromSeconds(30), "Đang chạy trình cài"))
            {
                if (EffectiveKind == InstallKind.Msi)
                {
                    List<string> msiArgs = ["/i", file.Path, "/qn", "/norestart", .. args];
                    result = await ProcessRunner.RunAsync(WindowsEnvironment.SystemTool("msiexec.exe"), msiArgs, CancellationToken.None, killOnCancel: false).ConfigureAwait(false);
                }
                else
                {
                    result = await ProcessRunner.RunAsync(file.Path, args, CancellationToken.None, killOnCancel: false).ConfigureAwait(false);
                }
            }

            LastMetrics = metrics with { Install = watch.Elapsed, ExitCode = result.ExitCode };
            context.Log.Info($"Trình cài đặt kết thúc với mã {result.ExitCode} sau {watch.Elapsed.TotalSeconds:0} giây.", taskId);
            return Classify(result.ExitCode);
        }
    }

    /// <summary>Thời gian chờ tối đa để ứng dụng xuất hiện sau khi trình cài thoát (một số bộ cài tiếp tục chạy nền).</summary>
    public TimeSpan DetectWait { get; init; } = TimeSpan.FromSeconds(90);

    public async Task<VerifyResult> VerifyAsync(CancellationToken cancellationToken)
    {
        if (SelectedVariant is { Kind: InstallKind.Web } link)
        {
            return InstallDetector.FindEvidence(Package) is { } found
                ? VerifyResult.Passed($"Đã thấy {found} trên máy.")
                : VerifyResult.Attention($"Đã mở trang tải \"{link.Label}\". Tải và cài tay, rồi bấm \"Kiểm tra trạng thái cài\".");
        }

        if (!InstallDetector.CanDetect(Package))
        {
            return VerifyResult.Attention("Cần chú ý — chưa xác minh ứng dụng sau cài: Admin chưa khai báo cách nhận biết (tên hoặc đường dẫn).");
        }

        // Trình cài có thể thoát ngay rồi tiếp tục cài nền: chờ ứng dụng xuất hiện trong một khoảng ngắn.
        var deadline = DateTimeOffset.UtcNow + DetectWait;
        while (true)
        {
            if (InstallDetector.FindEvidence(Package) is { } evidence)
            {
                return VerifyResult.Passed($"Đã thấy {evidence} trên máy.");
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                return VerifyResult.Attention("Cần chú ý — chưa xác minh ứng dụng sau cài: chưa thấy ứng dụng trong danh sách đã cài. Trình cài có thể vẫn đang chạy nền.");
            }

            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Lỗi tải đáng thử lại: mất mạng, hết giờ, máy chủ lỗi 5xx, 408, 429. Link sai (404, 403…) thử lại vô ích
    /// (kiểm thử 01/11/2026: link 404 bị thử 3 lần, chờ 30 giây).
    /// </summary>
    internal static bool IsTransientDownloadError(Exception ex) => ex switch
    {
        HttpRequestException { StatusCode: { } code } => (int)code >= 500 || code is System.Net.HttpStatusCode.RequestTimeout or System.Net.HttpStatusCode.TooManyRequests,
        HttpRequestException => true,
        TaskCanceledException => true,
        _ => false,
    };

    /// <summary>Trình cài báo đã hủy (1602): chưa cài gì lên máy, nên được phép thử cách cài dự phòng.</summary>
    internal static bool InstallerCancelled(ExecuteResult result) =>
        result.ErrorCode == UserCancelled.ToString(System.Globalization.CultureInfo.InvariantCulture);

    internal static ExecuteResult Classify(int exitCode) => exitCode switch
    {
        0 => ExecuteResult.Completed("Trình cài đặt báo xong (mã thoát 0)."),
        RebootRequired or RebootInitiated => ExecuteResult.RebootRequired($"Trình cài đặt yêu cầu khởi động lại máy (mã {exitCode})."),
        AnotherInstallRunning => ExecuteResult.Failed("Đang có một trình cài đặt khác chạy trên máy (mã 1618).", isTransient: true, errorCode: "1618"),
        UserCancelled => ExecuteResult.Failed("Trình cài đặt bị hủy (mã 1602).", errorCode: "1602"),
        _ => ExecuteResult.Failed($"Trình cài đặt báo lỗi (mã {exitCode}).", errorCode: exitCode.ToString(System.Globalization.CultureInfo.InvariantCulture)),
    };

    /// <summary>Tách tham số theo khoảng trắng. Ký tự đã được giới hạn (chữ, số, / - _ = . và khoảng trắng) nên không cần xử lý ngoặc.</summary>
    internal static IReadOnlyList<string> SplitArgs(string args) =>
        RemoteCatalog.SilentArgsRegex().IsMatch(args ?? "")
            ? (args ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [];

    private async Task<ExecuteResult> InstallZipAsync(DownloadedFile file, InstallMetrics metrics, TaskContext context, string taskId)
    {
        if (Package.ZipFolder is not { } folder || Package.ZipExe is not { } exe)
        {
            return ExecuteResult.Failed("Gói ZIP thiếu thư mục cài hoặc file chạy.");
        }

        var watch = Stopwatch.StartNew();
        InstallerStarted = true;
        var staging = Path.Combine(file.Directory, "giai-nen");
        try
        {
            using (InstallProgress.Pulse(context, InstallProgress.DownloadShare, InstallProgress.BeforeVerify, TimeSpan.FromSeconds(10), "Đang giải nén"))
            {
                await Task.Run(() => ZipFile.ExtractToDirectory(file.Path, staging, overwriteFiles: false)).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return ExecuteResult.Failed("Không giải nén được file ZIP: " + ex.Message);
        }

        var exePath = Directory.EnumerateFiles(staging, exe, SearchOption.AllDirectories).FirstOrDefault();
        if (exePath is null)
        {
            return ExecuteResult.Failed($"Không thấy {exe} trong file ZIP.");
        }

        var signature = Authenticode.Check(exePath);
        context.Log.Info($"Chữ ký số của {exe}: {signature.Detail}", taskId);
        if (!signature.IsTrusted)
        {
            return ExecuteResult.Failed(signature.IsValid
                ? $"{exe} ký bởi \"{signature.SignerText}\", không nằm trong danh sách hãng tin cậy của Làm 1 Chạm. Không cài."
                : $"{exe}: {signature.Detail} Không cài file không có chữ ký số hợp lệ.");
        }

        var source = Path.GetDirectoryName(exePath)!;
        var target = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), folder);
        CopyDirectory(source, target);
        CreateStartMenuShortcut(Path.Combine(target, Path.GetFileName(exePath)));
        LastMetrics = metrics with { Install = watch.Elapsed };
        return ExecuteResult.Completed($"Đã chép vào {target} và tạo lối tắt trong menu Start.");
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, dir)));
        }

        foreach (var f in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(f, Path.Combine(target, Path.GetRelativePath(source, f)), overwrite: true);
        }
    }

    private void CreateStartMenuShortcut(string exePath)
    {
        var programs = Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);
        var link = Path.Combine(programs, WebShortcutTask.SafeFileName(Package.Name) + ".lnk");
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null)
        {
            return;
        }

        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            dynamic shortcut = shell.CreateShortcut(link);
            shortcut.TargetPath = exePath;
            shortcut.WorkingDirectory = Path.GetDirectoryName(exePath);
            shortcut.Save();
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
        }
    }

    /// <summary>Mở link bằng Explorer (quyền người dùng), để trình duyệt không chạy kèm quyền quản trị (QĐ-005).</summary>
    internal static void OpenInBrowser(Uri url) =>
        Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"))
        {
            UseShellExecute = false,
            ArgumentList = { url.AbsoluteUri },
        })?.Dispose();
}
