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
        Package = package;
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

        var presence = await _client.GetPresenceAsync(Package.WinGetId, cancellationToken).ConfigureAwait(false);
        if (presence.Presence == InstallPresence.Installed)
        {
            return PreconditionResult.AlreadyInDesiredState("Đã cài sẵn.");
        }

        var publisherProblem = await _client.CheckPublisherAsync(Package, cancellationToken).ConfigureAwait(false);
        return publisherProblem is null ? PreconditionResult.Met() : PreconditionResult.NotMet(publisherProblem);
    }

    public async Task<ExecuteResult> ExecuteAsync(TaskContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var result = await InstallAsync(context, Package.MachineWide, cancellationToken).ConfigureAwait(false);

        // QĐ-001 Q-04: toàn máy khi gói hỗ trợ, ngược lại để WinGet cài theo phạm vi mặc định của gói.
        if (Package.MachineWide && result.ExitCode == WinGetClient.NoApplicableInstaller)
        {
            if (UserContext.DifferentUserReason is { } otherUser)
            {
                return ExecuteResult.Failed("Gói không hỗ trợ cài cho toàn máy. " + otherUser, errorCode: result.ExitCodeHex);
            }

            context.Log.Warning("Gói không hỗ trợ cài cho toàn máy; cài theo phạm vi mặc định của gói.", Descriptor.Id);
            result = await InstallAsync(context, machineScope: false, cancellationToken).ConfigureAwait(false);
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

    private Task<ProcessResult> InstallAsync(TaskContext context, bool machineScope, CancellationToken cancellationToken)
    {
        context.Log.Info($"winget install --id {Package.WinGetId} --exact --source winget --silent" + (machineScope ? " --scope machine" : ""), Descriptor.Id);
        return _client.InstallAsync(Package.WinGetId, machineScope, cancellationToken);
    }

    public async Task<VerifyResult> VerifyAsync(TaskContext context, CancellationToken cancellationToken)
    {
        var presence = await _client.GetPresenceAsync(Package.WinGetId, cancellationToken).ConfigureAwait(false);
        return presence.Presence switch
        {
            InstallPresence.Installed => VerifyResult.Passed($"WinGet xác nhận đã cài {Package.WinGetId}."),
            InstallPresence.NotInstalled => VerifyResult.Attention("Cần chú ý — chưa xác minh ứng dụng sau cài: WinGet chưa thấy gói trên máy."),
            _ => VerifyResult.Inconclusive(presence.Detail ?? "Không đọc được danh sách ứng dụng."),
        };
    }

    public async Task<VerifyResult> RollbackAsync(TaskContext context, CancellationToken cancellationToken)
    {
        var result = await _client.UninstallAsync(Package.WinGetId, cancellationToken).ConfigureAwait(false);
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

        var presence = await _client.GetPresenceAsync(Package.WinGetId, cancellationToken).ConfigureAwait(false);
        return presence.Presence switch
        {
            InstallPresence.Installed => "Đã cài",
            InstallPresence.NotInstalled => "Chưa phát hiện",
            _ => "Chưa kiểm tra",
        };
    }
}
