using LamMotCham.Core.Logging;
using LamMotCham.Core.Processes;
using LamMotCham.Core.Tasks;
using LamMotCham.Windows.Infrastructure;

namespace LamMotCham.Windows.Software;

/// <summary>
/// Cài một gói qua WinGet. Mã thoát 0 chưa đủ: chỉ báo SUCCESS khi <c>winget list</c> xác nhận gói đã có
/// (UI/UX Handoff mục 8). Không cưỡng chế dừng trình cài đặt khi người dùng hủy.
/// </summary>
public sealed class WinGetInstallTask : ITaskModule, IDescribesCurrentState
{
    /// <summary>Phiên bản WinGet tối thiểu đã thử với các tham số đang dùng.</summary>
    public static readonly Version MinimumWinGet = new(1, 6);

    private readonly WinGetClient _client;

    public WinGetInstallTask(SoftwarePackage package, WinGetClient client)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(client);
        if (!package.UsesWinGet)
        {
            throw new ArgumentException("Gói không có mã WinGet và nhà phát hành để cài bằng WinGet.", nameof(package));
        }

        Package = package;
        PackageId = package.WinGetId!;
        _client = client;
        Descriptor = new TaskDescriptor
        {
            Id = "sw." + package.Key,
            DisplayName = package.Name,
            Version = 1,
            Risk = RiskLevel.SoftwareChange,
            Privilege = package.MachineWide ? PrivilegeLevel.Administrator : PrivilegeLevel.User,
            Kind = TaskKind.Installer,
            Timeout = TimeSpan.FromMinutes(30),
            Retry = new RetryPolicy { MaxAttempts = 3, InitialDelay = TimeSpan.FromSeconds(10), BackoffFactor = 2, MaxDelay = TimeSpan.FromSeconds(60) },
            Rollback = RollbackSupport.Supported,
            RollbackNote = "Gỡ bằng winget uninstall.",
            AllowForcedStop = false,
        };
    }

    public SoftwarePackage Package { get; }

    /// <summary>Package ID WinGet (đã kiểm tra an toàn khi đọc danh mục).</summary>
    public string PackageId { get; }

    /// <summary>Ngôn ngữ ưu tiên khi cài (QĐ-006: dùng bản tiếng Việt nếu gói có).</summary>
    public const string PreferredLocale = "vi-VN";

    public TaskDescriptor Descriptor { get; }

    public async Task<PreconditionResult> CheckPreconditionsAsync(TaskContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var version = WinGetClient.ParseVersion(await _client.GetVersionAsync(cancellationToken).ConfigureAwait(false));
        if (version is null)
        {
            return PreconditionResult.NotSupported(
                "WinGet không khả dụng. Chưa cài gói này. Mở Microsoft Store, cập nhật App Installer rồi kiểm tra lại.");
        }

        if (version < MinimumWinGet)
        {
            return PreconditionResult.NotSupported($"WinGet v{version} quá cũ (cần từ v{MinimumWinGet}). Cập nhật App Installer từ Microsoft Store.");
        }

        // Gói cài theo người dùng mà ứng dụng chạy bằng tài khoản khác thì sẽ cài nhầm vào tài khoản đó.
        if (!Package.MachineWide && UserContext.DifferentUserReason is { } otherUser)
        {
            return PreconditionResult.NotMet(otherUser);
        }

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var presence = await _client.GetPresenceAsync(PackageId, cancellationToken).ConfigureAwait(false);
        if (presence.Presence == InstallPresence.Installed)
        {
            return PreconditionResult.AlreadyInDesiredState("Đã cài sẵn.");
        }

        var publisherProblem = await _client.CheckPublisherAsync(Package, cancellationToken).ConfigureAwait(false);
        context.Log.Info($"Kiểm tra trước khi cài qua WinGet (winget list + show) mất {watch.Elapsed.TotalSeconds:0} giây.", Descriptor.Id);
        return publisherProblem is null ? PreconditionResult.Met() : PreconditionResult.NotMet(publisherProblem);
    }

    public async Task<ExecuteResult> ExecuteAsync(TaskContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        // QĐ-001 Q-04: toàn máy khi gói hỗ trợ, ngược lại để WinGet cài theo phạm vi mặc định của gói.
        // QĐ-006: ưu tiên bản tiếng Việt; gói không có bản tiếng Việt thì cài bản mặc định.
        ProcessResult? result = null;
        foreach (var (machineScope, locale) in Attempts(Package.MachineWide))
        {
            if (!machineScope && Package.MachineWide && result is not null && UserContext.DifferentUserReason is { } otherUser)
            {
                return ExecuteResult.Failed("Gói không hỗ trợ cài cho toàn máy. " + otherUser, errorCode: result.ExitCodeHex);
            }

            if (result is not null)
            {
                context.Log.Warning(
                    $"Không có bộ cài phù hợp ({(locale is null ? "bỏ yêu cầu tiếng Việt" : "thử lại")}{(machineScope ? "" : ", cài theo phạm vi mặc định")}).",
                    Descriptor.Id);
            }

            result = await InstallAsync(context, machineScope, locale, cancellationToken).ConfigureAwait(false);
            if (result.ExitCode != WinGetClient.NoApplicableInstaller)
            {
                break;
            }
        }

        if (result is null)
        {
            return ExecuteResult.Failed("Không chạy được WinGet.");
        }

        if (result.ExitCode == 0)
        {
            return ExecuteResult.Completed("WinGet báo cài xong (mã thoát 0).");
        }

        var info = WinGetClient.Describe(result.ExitCode);
        context.Log.Warning($"winget thoát với {result.ExitCodeHex}: {info.Message}", Descriptor.Id);
        if (info.NeedsReboot)
        {
            return ExecuteResult.RebootRequired($"{info.Message} (mã {result.ExitCodeHex})");
        }

        return info.CountsAsInstalled
            ? ExecuteResult.Completed($"{info.Message} (mã {result.ExitCodeHex})")
            : ExecuteResult.Failed(info.Message, info.IsTransient, result.ExitCodeHex);
    }

    /// <summary>Thứ tự thử: (toàn máy, tiếng Việt) → (toàn máy) → (mặc định, tiếng Việt) → (mặc định).</summary>
    internal static IReadOnlyList<(bool MachineScope, string? Locale)> Attempts(bool machineWide) =>
        machineWide
            ? [(true, PreferredLocale), (true, null), (false, PreferredLocale), (false, null)]
            : [(false, PreferredLocale), (false, null)];

    private async Task<ProcessResult> InstallAsync(TaskContext context, bool machineScope, string? locale, CancellationToken cancellationToken)
    {
        context.Log.Info(
            $"winget install --id {PackageId} --exact --source winget --silent" + (locale is null ? "" : " --locale " + locale) + (machineScope ? " --scope machine" : ""),
            Descriptor.Id);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        ProcessResult result;
        using (InstallProgress.Pulse(context, 0.05, InstallProgress.BeforeVerify, TimeSpan.FromSeconds(60), "Đang tải và cài qua WinGet"))
        {
            result = await _client.InstallAsync(PackageId, machineScope, locale, cancellationToken).ConfigureAwait(false);
        }
        context.Log.Info($"winget install thoát với {result.ExitCodeHex} sau {watch.Elapsed.TotalSeconds:0} giây (gồm tải và cài).", Descriptor.Id);
        return result;
    }

    public async Task<VerifyResult> VerifyAsync(TaskContext context, CancellationToken cancellationToken)
    {
        var presence = await _client.GetPresenceAsync(PackageId, cancellationToken).ConfigureAwait(false);
        return presence.Presence switch
        {
            InstallPresence.Installed => VerifyResult.Passed($"WinGet xác nhận đã cài {PackageId}."),
            InstallPresence.NotInstalled => VerifyResult.Attention("Cần chú ý — chưa xác minh ứng dụng sau cài: WinGet chưa thấy gói trên máy."),
            _ => VerifyResult.Inconclusive(presence.Detail ?? "Không đọc được danh sách ứng dụng."),
        };
    }

    public async Task<VerifyResult> RollbackAsync(TaskContext context, CancellationToken cancellationToken)
    {
        var result = await _client.UninstallAsync(PackageId, cancellationToken).ConfigureAwait(false);
        return result.ExitCode == 0
            ? VerifyResult.Passed("Đã gỡ.")
            : VerifyResult.Failed($"Gỡ không thành công ({result.ExitCodeHex}).");
    }

    public async Task<string> DescribeCurrentStateAsync(CancellationToken cancellationToken)
    {
        if (!_client.IsAvailable)
        {
            return "Chưa kiểm tra";
        }

        var presence = await _client.GetPresenceAsync(PackageId, cancellationToken).ConfigureAwait(false);
        return presence.Presence switch
        {
            InstallPresence.Installed => "Đã cài",
            InstallPresence.NotInstalled => "Chưa phát hiện",
            _ => "Chưa kiểm tra",
        };
    }
}
