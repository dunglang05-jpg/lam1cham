using System.Diagnostics;
using LamMotCham.Core.Logging;
using LamMotCham.Core.Tasks;

namespace LamMotCham.Windows.Software;

/// <summary>Cách cài đã chọn cho lần chạy này.</summary>
public enum InstallRoute
{
    None,
    WinGet,
    Link,
    WebShortcut,
}

/// <summary>
/// Task cài một ứng dụng trong danh mục đang dùng (QĐ-006). Chọn cách cài lúc kiểm tra điều kiện:
/// ưu tiên link tải khi Admin có link và cách nhận biết (nhanh hơn WinGet 2–3 lần, tải trước được — PO duyệt 01/11/2026),
/// lỗi trước khi chạy trình cài (tải, chữ ký) thì chuyển sang WinGet; không có link thì WinGet nếu gói có mã + nhà phát hành;
/// link luôn bắt buộc chữ ký số hãng tin cậy;
/// ứng dụng loại "Trang web" thì tạo lối tắt. Ứng dụng hiện mờ (thiếu cách cài) báo "Không hỗ trợ" kèm lý do.
/// Chỉ báo thành công sau khi xác minh; không có cách xác minh thì báo cần chú ý.
/// </summary>
public sealed class AppInstallTask : ITaskModule, IDescribesCurrentState
{
    private readonly WinGetClient _winGet;
    private readonly WinGetInstallTask? _winGetTask;
    private readonly Stopwatch _watch = new();

    public AppInstallTask(SoftwarePackage package, WinGetClient winGet)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(winGet);
        Package = package;
        _winGet = winGet;
        _winGetTask = package.UsesWinGet ? new WinGetInstallTask(package, winGet) : null;
        Direct = package.Kind != InstallKind.Web && (package.HasLink || package.HasVariants) ? new DirectInstallTask(package) : null;
        var reversible = package.UsesWinGet || package.Kind == InstallKind.Web;
        Descriptor = new TaskDescriptor
        {
            Id = "sw." + package.Key,
            DisplayName = package.Name,
            Version = 2,
            Risk = RiskLevel.SoftwareChange,
            Privilege = package.MachineWide || !package.UsesWinGet ? PrivilegeLevel.Administrator : PrivilegeLevel.User,
            Kind = TaskKind.Installer,
            Timeout = TimeSpan.FromMinutes(60),
            Retry = new RetryPolicy { MaxAttempts = 3, InitialDelay = TimeSpan.FromSeconds(10), BackoffFactor = 2, MaxDelay = TimeSpan.FromSeconds(60) },
            Rollback = reversible ? RollbackSupport.Supported : RollbackSupport.NotPossible,
            RollbackNote = reversible
                ? (package.Kind == InstallKind.Web ? "Xóa lối tắt trên màn hình nền." : "Gỡ bằng winget uninstall.")
                : "Cài bằng link: gỡ trong Cài đặt Windows › Ứng dụng.",
            AllowForcedStop = false,
        };
    }

    public SoftwarePackage Package { get; }

    /// <summary>Cách cài bằng link (có khi ứng dụng có link tải hoặc phiên bản để chọn).</summary>
    public DirectInstallTask? Direct { get; }

    public TaskDescriptor Descriptor { get; }

    public InstallRoute Route { get; private set; }

    /// <summary>
    /// Cài bằng link trước WinGet: có link cài tự động (không phải "Mở link") và Admin khai báo cách nhận biết,
    /// để kiểm tra "đã cài sẵn" và xác minh sau cài bằng registry thay cho các lệnh winget chậm.
    /// </summary>
    public bool PrefersLink => Package.IsAvailable && Package.Kind != InstallKind.Web && Direct is { IsAutomatic: true } && InstallDetector.CanDetect(Package);

    /// <summary>Lần chạy gần nhất phát hiện ứng dụng đã có sẵn (không cài lại).</summary>
    public bool WasAlreadyInstalled { get; private set; }

    /// <summary>Số liệu lần cài gần nhất cho báo cáo "Lịch sử cài".</summary>
    public InstallMetrics? LastMetrics { get; private set; }

    /// <summary>Nguồn để hiển thị: mã WinGet, tên miền link tải hoặc trang web.</summary>
    public string SourceText => Package.SourceText;

    public async Task<PreconditionResult> CheckPreconditionsAsync(TaskContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        Route = InstallRoute.None;
        WasAlreadyInstalled = false;
        LastMetrics = null;
        context.Report(0, "Đang kiểm tra máy…");

        if (!Package.IsAvailable)
        {
            return PreconditionResult.NotSupported(Package.UnavailableReason!);
        }

        if (Package.Kind == InstallKind.Web)
        {
            Route = InstallRoute.WebShortcut;
            return WebShortcutTask.Exists(Package) ? Already("Đã có lối tắt trên màn hình nền.", InstallRoute.WebShortcut) : PreconditionResult.Met();
        }

        if (PrefersLink)
        {
            if (InstallDetector.FindEvidence(Package) is { } found)
            {
                return Already($"Đã cài sẵn ({found}).", InstallRoute.Link);
            }

            Route = InstallRoute.Link;
            return PreconditionResult.Met();
        }

        if (_winGetTask is not null)
        {
            var viaWinGet = await _winGetTask.CheckPreconditionsAsync(context, cancellationToken).ConfigureAwait(false);
            switch (viaWinGet.Outcome)
            {
                case PreconditionOutcome.Met:
                    Route = InstallRoute.WinGet;
                    return viaWinGet;
                case PreconditionOutcome.AlreadyInDesiredState:
                    return Already(viaWinGet.Reason ?? "Đã cài sẵn.", InstallRoute.WinGet);
                case PreconditionOutcome.NotMet:
                    // Nhà phát hành không khớp hoặc chạy nhầm tài khoản: không chuyển sang link.
                    return viaWinGet;
                default:
                    if (Direct is not { CanInstall: true })
                    {
                        return viaWinGet;
                    }

                    context.Log.Warning((viaWinGet.Reason ?? "WinGet không dùng được.") + " Chuyển sang cài bằng link tải.", Descriptor.Id);
                    break;
            }
        }

        if (Direct is not { CanInstall: true })
        {
            return PreconditionResult.NotSupported("Chưa có cách cài tự động cho ứng dụng này.");
        }

        if (InstallDetector.FindEvidence(Package) is { } evidence)
        {
            return Already($"Đã cài sẵn ({evidence}).", InstallRoute.Link);
        }

        Route = InstallRoute.Link;
        return PreconditionResult.Met();
    }

    public async Task<ExecuteResult> ExecuteAsync(TaskContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        _watch.Restart();
        switch (Route)
        {
            case InstallRoute.WinGet:
            {
                var result = await _winGetTask!.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
                LastMetrics = new InstallMetrics { Source = Package.WinGetId!, Install = _watch.Elapsed };
                return result;
            }

            case InstallRoute.Link:
            {
                var result = await Direct!.ExecuteAsync(context, Descriptor.Id, cancellationToken).ConfigureAwait(false);
                LastMetrics = Direct.LastMetrics;
                if (result.Outcome != ExecuteOutcome.Failed || Direct.InstallerStarted || _winGetTask is null || !_winGet.IsAvailable)
                {
                    return result;
                }

                // Lỗi trước khi chạy trình cài (dò link, tải, chữ ký): chưa thay đổi gì trên máy nên chuyển sang WinGet.
                return await FallBackToWinGetAsync(context, result, cancellationToken).ConfigureAwait(false);
            }

            case InstallRoute.WebShortcut:
                await WebShortcutTask.CreateAsync(Package, cancellationToken).ConfigureAwait(false);
                LastMetrics = new InstallMetrics { Source = Package.Website?.Host ?? "" };
                return ExecuteResult.Completed("Đã tạo lối tắt trên màn hình nền.");

            default:
                return ExecuteResult.Failed("Chưa chọn được cách cài.");
        }
    }

    private async Task<ExecuteResult> FallBackToWinGetAsync(TaskContext context, ExecuteResult linkFailure, CancellationToken cancellationToken)
    {
        context.Log.Warning($"Cài bằng link chưa được: {linkFailure.Reason} Chuyển sang WinGet.", Descriptor.Id);
        var pre = await _winGetTask!.CheckPreconditionsAsync(context, cancellationToken).ConfigureAwait(false);
        switch (pre.Outcome)
        {
            case PreconditionOutcome.Met:
                break;
            case PreconditionOutcome.AlreadyInDesiredState:
                Route = InstallRoute.WinGet;
                WasAlreadyInstalled = true;
                return ExecuteResult.Completed("WinGet thấy ứng dụng đã có trên máy.");
            default:
                return ExecuteResult.Failed($"{linkFailure.Reason} WinGet cũng không dùng được: {pre.Reason}", linkFailure.IsTransient, linkFailure.ErrorCode);
        }

        Route = InstallRoute.WinGet;
        _watch.Restart();
        var result = await _winGetTask.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
        LastMetrics = new InstallMetrics { Source = Package.WinGetId!, Install = _watch.Elapsed };
        return result;
    }

    public Task<VerifyResult> VerifyAsync(TaskContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Report(InstallProgress.BeforeVerify, "Đang xác minh đã cài…");
        return VerifyRouteAsync(context, cancellationToken);
    }

    private Task<VerifyResult> VerifyRouteAsync(TaskContext context, CancellationToken cancellationToken) => Route switch
    {
        InstallRoute.WinGet => _winGetTask!.VerifyAsync(context, cancellationToken),
        InstallRoute.Link => Direct!.VerifyAsync(cancellationToken),
        InstallRoute.WebShortcut => Task.FromResult(WebShortcutTask.Exists(Package)
            ? VerifyResult.Passed("Lối tắt đã có trên màn hình nền.")
            : VerifyResult.Failed("Không thấy lối tắt vừa tạo.")),
        _ => Task.FromResult(VerifyResult.Inconclusive("Chưa chạy.")),
    };

    public async Task<VerifyResult> RollbackAsync(TaskContext context, CancellationToken cancellationToken)
    {
        if (Package.Kind == InstallKind.Web)
        {
            return WebShortcutTask.Remove(Package) ? VerifyResult.Passed("Đã xóa lối tắt.") : VerifyResult.Failed("Không xóa được lối tắt.");
        }

        if (_winGetTask is not null && _winGet.IsAvailable)
        {
            return await _winGetTask.RollbackAsync(context, cancellationToken).ConfigureAwait(false);
        }

        return VerifyResult.NotSupported("Gỡ trong Cài đặt Windows › Ứng dụng.");
    }

    public async Task<string> DescribeCurrentStateAsync(CancellationToken cancellationToken)
    {
        if (!Package.IsAvailable)
        {
            return "Chưa kiểm tra";
        }

        if (Package.Kind == InstallKind.Web)
        {
            return WebShortcutTask.Exists(Package) ? "Đã cài" : "Chưa phát hiện";
        }

        // Nhận biết bằng registry nhanh hơn nhiều so với một lệnh winget list cho mỗi ứng dụng.
        if (PrefersLink)
        {
            return InstallDetector.FindEvidence(Package) is not null ? "Đã cài" : "Chưa phát hiện";
        }

        if (_winGetTask is not null && _winGet.IsAvailable)
        {
            return await _winGetTask.DescribeCurrentStateAsync(cancellationToken).ConfigureAwait(false);
        }

        return InstallDetector.CanDetect(Package)
            ? (InstallDetector.FindEvidence(Package) is not null ? "Đã cài" : "Chưa phát hiện")
            : "Chưa kiểm tra";
    }

    /// <param name="route">Cách xác minh lại: ứng dụng đã có sẵn vẫn phải qua Verify (lỗi 1.1.4: Route để trống nên
    /// Verify báo "Không xác minh được. Chưa chạy." — K-Lite đã cài sẵn hiện "Cần kiểm tra").</param>
    private PreconditionResult Already(string reason, InstallRoute route)
    {
        Route = route;
        WasAlreadyInstalled = true;
        return PreconditionResult.AlreadyInDesiredState(reason);
    }
}
