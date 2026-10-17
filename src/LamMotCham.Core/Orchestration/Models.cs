using LamMotCham.Core.Tasks;

namespace LamMotCham.Core.Orchestration;

/// <summary>Sự kiện thay đổi trạng thái gửi lên UI. UI chịu trách nhiệm gom cập nhật.</summary>
public sealed record TaskStatusUpdate(string TaskId, TaskState State, string? Message, int Attempt);

public sealed record TaskOutcome
{
    public required string TaskId { get; init; }

    public required string DisplayName { get; init; }

    public required TaskKind Kind { get; init; }

    public required RiskLevel Risk { get; init; }

    public required TaskState State { get; init; }

    public string? Reason { get; init; }

    public int Attempts { get; init; }

    public DateTimeOffset? StartedAt { get; init; }

    public DateTimeOffset? FinishedAt { get; init; }

    public TimeSpan? Duration => StartedAt is { } s && FinishedAt is { } f ? f - s : null;
}

public enum RunHaltReason
{
    None,

    /// <summary>Một task cần khởi động lại; các task sau giữ trạng thái chờ.</summary>
    RebootRequired,

    /// <summary>Một task không phản hồi yêu cầu hủy; dừng hàng đợi để tránh chạy chồng.</summary>
    UnresponsiveTask,

    CancelledByUser,
}

public sealed record RunReport
{
    public required DateTimeOffset StartedAt { get; init; }

    public required DateTimeOffset FinishedAt { get; init; }

    public required IReadOnlyList<TaskOutcome> Tasks { get; init; }

    public RunHaltReason HaltReason { get; init; }

    public int Count(TaskState state) => Tasks.Count(t => t.State == state);

    /// <summary>Lượt chạy chỉ được coi là hoàn tất khi mọi task đều SUCCESS.</summary>
    public bool AllSucceeded => Tasks.Count > 0 && Tasks.All(t => t.State == TaskState.Success);
}

public sealed record OrchestratorOptions
{
    /// <summary>Chu kỳ kiểm tra lại tài nguyên khi đang tạm dừng.</summary>
    public TimeSpan ResourcePollInterval { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>Thời gian chờ tài nguyên tối đa trước khi đánh dấu task cần chú ý.</summary>
    public TimeSpan MaxResourceWait { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>Thời gian cho task tự dừng sau khi bị hủy/timeout trước khi coi là không phản hồi.</summary>
    public TimeSpan CancellationGracePeriod { get; init; } = TimeSpan.FromSeconds(5);
}

public sealed class QueueValidationException(IReadOnlyList<string> errors)
    : Exception("Hàng đợi task không hợp lệ: " + string.Join(" ", errors))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}

/// <summary>Trừu tượng hóa việc chờ để test retry/backoff không phải chờ thật.</summary>
public interface IDelayScheduler
{
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

public sealed class SystemDelayScheduler(TimeProvider? timeProvider = null) : IDelayScheduler
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, _time, cancellationToken);
}
