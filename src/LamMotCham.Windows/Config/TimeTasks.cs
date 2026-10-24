using System.Globalization;
using LamMotCham.Core.Logging;
using LamMotCham.Core.Network;
using LamMotCham.Core.Processes;
using LamMotCham.Core.Tasks;
using LamMotCham.Windows.Infrastructure;

namespace LamMotCham.Windows.Config;

/// <summary>Đặt múi giờ Việt Nam (SE Asia Standard Time, UTC+07:00).</summary>
public sealed class TimeZoneTask(RollbackStore store) : ITaskModule, IDescribesCurrentState
{
    public const string VietnamTimeZoneId = "SE Asia Standard Time";

    public TaskDescriptor Descriptor { get; } = new()
    {
        Id = "win.timezone",
        DisplayName = "Múi giờ Việt Nam (UTC+07:00)",
        Version = 1,
        Risk = RiskLevel.Reversible,
        Privilege = PrivilegeLevel.User,
        Kind = TaskKind.Configuration,
        Timeout = TimeSpan.FromSeconds(30),
        Rollback = RollbackSupport.Supported,
    };

    public Task<PreconditionResult> CheckPreconditionsAsync(TaskContext context, CancellationToken cancellationToken)
    {
        TimeZoneInfo.ClearCachedData();
        return Task.FromResult(TimeZoneInfo.Local.Id == VietnamTimeZoneId
            ? PreconditionResult.AlreadyInDesiredState("Múi giờ đã là UTC+07:00.")
            : PreconditionResult.Met());
    }

    public async Task<ExecuteResult> ExecuteAsync(TaskContext context, CancellationToken cancellationToken)
    {
        TimeZoneInfo.ClearCachedData();
        store.SaveIfAbsent(Descriptor.Id, new Dictionary<string, string?> { ["id"] = TimeZoneInfo.Local.Id });
        var result = await ProcessRunner.RunAsync(WindowsEnvironment.SystemTool("tzutil.exe"), ["/s", VietnamTimeZoneId], cancellationToken).ConfigureAwait(false);
        return result.ExitCode == 0
            ? ExecuteResult.Completed()
            : ExecuteResult.Failed("tzutil không đổi được múi giờ.", errorCode: result.ExitCodeHex);
    }

    public Task<VerifyResult> VerifyAsync(TaskContext context, CancellationToken cancellationToken)
    {
        TimeZoneInfo.ClearCachedData();
        var id = TimeZoneInfo.Local.Id;
        return Task.FromResult(id == VietnamTimeZoneId
            ? VerifyResult.Passed("Đã đặt SE Asia Standard Time và đọc lại khớp.")
            : VerifyResult.Failed($"Múi giờ hiện tại vẫn là {id}."));
    }

    public async Task<VerifyResult> RollbackAsync(TaskContext context, CancellationToken cancellationToken)
    {
        var saved = store.Load(Descriptor.Id);
        if (saved?.GetValueOrDefault("id") is not { Length: > 0 } old)
        {
            return VerifyResult.Inconclusive("Không có múi giờ cũ đã lưu.");
        }

        var result = await ProcessRunner.RunAsync(WindowsEnvironment.SystemTool("tzutil.exe"), ["/s", old], cancellationToken).ConfigureAwait(false);
        TimeZoneInfo.ClearCachedData();
        if (result.ExitCode != 0 || !string.Equals(TimeZoneInfo.Local.Id, old, StringComparison.Ordinal))
        {
            // Giữ múi giờ cũ đã lưu để có thể hoàn tác lại.
            return VerifyResult.Failed($"Không khôi phục được múi giờ cũ ({old}); hiện tại vẫn là {TimeZoneInfo.Local.Id}.");
        }

        store.Clear(Descriptor.Id);
        return VerifyResult.Passed($"Đã khôi phục {old} và đọc lại khớp.");
    }

    public Task<string> DescribeCurrentStateAsync(CancellationToken cancellationToken)
    {
        TimeZoneInfo.ClearCachedData();
        return Task.FromResult("Hiện tại: " + TimeZoneInfo.Local.DisplayName);
    }
}

/// <summary>
/// Yêu cầu Windows đồng bộ giờ rồi đo lại độ lệch bằng SNTP. Không đổi máy chủ thời gian đã cấu hình.
/// </summary>
public sealed class TimeSyncTask : ITaskModule, IDescribesCurrentState
{
    private static readonly string[] Servers = ["time.windows.com", "pool.ntp.org"];
    private static readonly TimeSpan Tolerance = TimeSpan.FromSeconds(2);

    public TaskDescriptor Descriptor { get; } = new()
    {
        Id = "win.timesync",
        DisplayName = "Đồng bộ thời gian",
        Version = 1,
        Risk = RiskLevel.Reversible,
        Privilege = PrivilegeLevel.Administrator,
        Kind = TaskKind.Configuration,
        Timeout = TimeSpan.FromMinutes(2),
        Retry = new RetryPolicy { MaxAttempts = 2, InitialDelay = TimeSpan.FromSeconds(5) },
        Rollback = RollbackSupport.NotPossible,
        RollbackNote = "Đồng bộ giờ không cần hoàn tác; không thay đổi cấu hình máy chủ thời gian.",
    };

    public async Task<PreconditionResult> CheckPreconditionsAsync(TaskContext context, CancellationToken cancellationToken)
    {
        var offset = await MeasureAsync(cancellationToken).ConfigureAwait(false);
        if (offset is null)
        {
            return PreconditionResult.NotMet("Không kết nối được máy chủ thời gian. Kiểm tra Internet rồi chạy lại.");
        }

        return offset.Value.Duration() <= Tolerance
            ? PreconditionResult.AlreadyInDesiredState($"Đồng hồ chỉ lệch {Format(offset.Value)}.")
            : PreconditionResult.Met();
    }

    public async Task<ExecuteResult> ExecuteAsync(TaskContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        // sc start trả về 1056 nếu dịch vụ đã chạy: không phải lỗi.
        var start = await ProcessRunner.RunAsync(WindowsEnvironment.SystemTool("sc.exe"), ["start", "w32time"], cancellationToken).ConfigureAwait(false);
        if (start.ExitCode is not (0 or 1056))
        {
            context.Log.Warning($"Không khởi động được dịch vụ Windows Time ({start.ExitCode}).", Descriptor.Id);
        }

        var sync = await ProcessRunner.RunAsync(WindowsEnvironment.SystemTool("w32tm.exe"), ["/resync", "/force"], cancellationToken).ConfigureAwait(false);
        return sync.ExitCode == 0
            ? ExecuteResult.Completed()
            : ExecuteResult.Failed("Windows Time chưa đồng bộ được.", isTransient: true, errorCode: sync.ExitCodeHex);
    }

    public async Task<VerifyResult> VerifyAsync(TaskContext context, CancellationToken cancellationToken)
    {
        var offset = await MeasureAsync(cancellationToken).ConfigureAwait(false);
        if (offset is null)
        {
            return VerifyResult.Inconclusive("Không đo lại được độ lệch vì không kết nối được máy chủ thời gian.");
        }

        return offset.Value.Duration() <= Tolerance
            ? VerifyResult.Passed($"Độ lệch sau đồng bộ: {Format(offset.Value)}.")
            : VerifyResult.Failed($"Đồng hồ vẫn lệch {Format(offset.Value)}.");
    }

    public Task<VerifyResult> RollbackAsync(TaskContext context, CancellationToken cancellationToken) =>
        Task.FromResult(VerifyResult.Passed("Không cần hoàn tác."));

    public async Task<string> DescribeCurrentStateAsync(CancellationToken cancellationToken)
    {
        var offset = await MeasureAsync(cancellationToken).ConfigureAwait(false);
        return offset is null ? "Hiện tại: chưa đo được (không có mạng?)" : "Hiện tại: lệch " + Format(offset.Value);
    }

    private static async Task<TimeSpan?> MeasureAsync(CancellationToken cancellationToken)
    {
        foreach (var server in Servers)
        {
            var offset = await SntpClient.TryGetClockOffsetAsync(server, TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);
            if (offset is not null)
            {
                return offset;
            }
        }

        return null;
    }

    private static string Format(TimeSpan offset)
    {
        var d = offset.Duration();
        return d.TotalMinutes >= 1
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)d.TotalMinutes} phút {d.Seconds} giây")
            : string.Create(CultureInfo.GetCultureInfo("vi-VN"), $"{d.TotalSeconds:0.0} giây");
    }
}
