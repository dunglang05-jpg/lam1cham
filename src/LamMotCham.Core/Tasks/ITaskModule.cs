using LamMotCham.Core.Logging;

namespace LamMotCham.Core.Tasks;

/// <summary>Ngữ cảnh truyền cho task trong một lần chạy.</summary>
public sealed record TaskContext(IRunLogger Log, int Attempt);

/// <summary>
/// Hợp đồng một task độc lập. Orchestrator gọi theo thứ tự:
/// CheckPreconditions → Execute (nếu cần) → Verify. Chỉ Verify đạt mới cho ra SUCCESS.
/// </summary>
/// <remarks>
/// Task phải tôn trọng <see cref="CancellationToken"/>, không chặn UI thread, không ghép input vào shell,
/// và không tự khởi động lại máy.
/// </remarks>
public interface ITaskModule
{
    TaskDescriptor Descriptor { get; }

    Task<PreconditionResult> CheckPreconditionsAsync(TaskContext context, CancellationToken cancellationToken);

    Task<ExecuteResult> ExecuteAsync(TaskContext context, CancellationToken cancellationToken);

    Task<VerifyResult> VerifyAsync(TaskContext context, CancellationToken cancellationToken);

    /// <summary>
    /// Khôi phục trạng thái trước khi Execute. Orchestrator không tự gọi trong MVP (xem Q-17);
    /// chỉ chạy khi người dùng chủ động yêu cầu hoàn tác.
    /// </summary>
    Task<VerifyResult> RollbackAsync(TaskContext context, CancellationToken cancellationToken);
}

/// <summary>Lớp cơ sở cho task chỉ đọc: không có Execute thay đổi hệ thống và không có rollback.</summary>
public abstract class ReadOnlyCheckTask : ITaskModule
{
    public abstract TaskDescriptor Descriptor { get; }

    public virtual Task<PreconditionResult> CheckPreconditionsAsync(TaskContext context, CancellationToken cancellationToken) =>
        Task.FromResult(PreconditionResult.Met());

    /// <summary>Task chỉ đọc không thay đổi gì; việc đọc thực hiện trong Verify.</summary>
    public Task<ExecuteResult> ExecuteAsync(TaskContext context, CancellationToken cancellationToken) =>
        Task.FromResult(ExecuteResult.Completed());

    public abstract Task<VerifyResult> VerifyAsync(TaskContext context, CancellationToken cancellationToken);

    public Task<VerifyResult> RollbackAsync(TaskContext context, CancellationToken cancellationToken) =>
        Task.FromResult(VerifyResult.Passed("Task chỉ đọc, không có gì để hoàn tác."));
}
