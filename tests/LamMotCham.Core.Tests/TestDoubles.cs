using LamMotCham.Core.Logging;
using LamMotCham.Core.Orchestration;
using LamMotCham.Core.Resources;
using LamMotCham.Core.Tasks;

namespace LamMotCham.Core.Tests;

/// <summary>Task giả lập có thể cấu hình từng bước, ghi lại số lần gọi.</summary>
internal sealed class FakeTask : ITaskModule
{
    private int _executeCalls;

    public FakeTask(string id, TaskKind kind = TaskKind.Configuration, params string[] dependsOn)
    {
        Descriptor = new TaskDescriptor
        {
            Id = id,
            DisplayName = "Task " + id,
            Version = 1,
            Risk = kind == TaskKind.Check ? RiskLevel.ReadOnly : RiskLevel.Reversible,
            Privilege = PrivilegeLevel.User,
            Kind = kind,
            Timeout = TimeSpan.FromSeconds(10),
            DependsOn = dependsOn,
            Rollback = kind == TaskKind.Check ? RollbackSupport.NotApplicable : RollbackSupport.Supported,
        };
    }

    public TaskDescriptor Descriptor { get; set; }

    public Func<CancellationToken, Task<PreconditionResult>> Precondition { get; set; } =
        _ => Task.FromResult(PreconditionResult.Met());

    public Func<int, CancellationToken, Task<ExecuteResult>> Execute { get; set; } =
        (_, _) => Task.FromResult(ExecuteResult.Completed());

    public Func<CancellationToken, Task<VerifyResult>> Verify { get; set; } =
        _ => Task.FromResult(VerifyResult.Passed());

    public int ExecuteCalls => Volatile.Read(ref _executeCalls);

    public CancellationToken LastToken { get; private set; }

    public Task<PreconditionResult> CheckPreconditionsAsync(TaskContext context, CancellationToken cancellationToken) =>
        Precondition(cancellationToken);

    public Task<ExecuteResult> ExecuteAsync(TaskContext context, CancellationToken cancellationToken)
    {
        LastToken = cancellationToken;
        var n = Interlocked.Increment(ref _executeCalls);
        return Execute(n, cancellationToken);
    }

    public Task<VerifyResult> VerifyAsync(TaskContext context, CancellationToken cancellationToken) =>
        Verify(cancellationToken);

    public Task<VerifyResult> RollbackAsync(TaskContext context, CancellationToken cancellationToken) =>
        Task.FromResult(VerifyResult.Passed());
}

/// <summary>Bỏ qua thời gian chờ và ghi lại các khoảng chờ được yêu cầu.</summary>
internal sealed class RecordingDelay : IDelayScheduler
{
    private readonly List<TimeSpan> _delays = [];
    private readonly Lock _gate = new();

    public IReadOnlyList<TimeSpan> Delays
    {
        get
        {
            lock (_gate)
            {
                return [.. _delays];
            }
        }
    }

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _delays.Add(delay);
        }

        return Task.CompletedTask;
    }
}

internal sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

internal sealed class QueueMonitor(params double?[] values) : IResourceMonitor
{
    private readonly Queue<double?> _values = new(values);
    private double? _last = values.Length > 0 ? values[^1] : null;

    public ResourceSnapshot Read()
    {
        if (_values.Count > 0)
        {
            _last = _values.Dequeue();
        }

        return new ResourceSnapshot(_last);
    }
}

internal sealed class ScriptedGovernor(params bool[] decisions) : IResourceGovernor
{
    private readonly Queue<bool> _decisions = new(decisions);

    public ResourceDecision Evaluate()
    {
        var allow = _decisions.Count == 0 || _decisions.Dequeue();
        return allow ? ResourceDecision.Allow() : ResourceDecision.Deny("RAM thấp (giả lập).");
    }
}

internal static class Factory
{
    public static TaskOrchestrator Orchestrator(
        out InMemoryRunLogger log,
        IResourceGovernor? governor = null,
        RecordingDelay? delay = null,
        OrchestratorOptions? options = null)
    {
        log = new InMemoryRunLogger();
        return new TaskOrchestrator(
            log,
            governor,
            delay ?? new RecordingDelay(),
            options ?? new OrchestratorOptions { CancellationGracePeriod = TimeSpan.FromMilliseconds(300) });
    }

    public static TaskOutcome Of(this RunReport report, string id) => report.Tasks.Single(t => t.TaskId == id);
}
