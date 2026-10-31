using LamMotCham.Core.Orchestration;
using LamMotCham.Core.Tasks;
using Xunit;

namespace LamMotCham.Core.Tests;

/// <summary>PO 01/11/2026: "thanh tiến trình hoàn toàn không chạy" — mọi tác vụ phải báo đang ở bước nào.</summary>
public sealed class ProgressReportingTests
{
    private sealed class Capture : IProgress<TaskStatusUpdate>
    {
        private readonly Lock _gate = new();

        public List<TaskStatusUpdate> Updates { get; } = [];

        public void Report(TaskStatusUpdate value)
        {
            lock (_gate)
            {
                Updates.Add(value);
            }
        }
    }

    [Fact]
    public async Task Every_task_reports_its_phases_and_ends_with_the_final_state()
    {
        var capture = new Capture();
        var t = new FakeTask("a");

        var report = await Factory.Orchestrator(out _).RunAsync([t], capture);

        Assert.Equal(TaskState.Success, report.Of("a").State);
        var texts = capture.Updates.Where(u => u.State == TaskState.Running).Select(u => u.Message).ToList();
        Assert.Contains("Đang kiểm tra trạng thái hiện tại", texts);
        Assert.Contains("Đang thực hiện", texts);
        Assert.Contains("Đang xác minh kết quả", texts);
        Assert.All(capture.Updates.Where(u => u.Fraction is not null), u => Assert.InRange(u.Fraction!.Value, 0, 1));
        Assert.True(capture.Updates[^1].State.IsTerminal());
    }

    [Fact]
    public async Task Slow_task_without_own_progress_gets_a_moving_pulse()
    {
        var capture = new Capture();
        var t = new FakeTask("a")
        {
            Execute = async (_, ct) =>
            {
                await Task.Delay(2600, ct);
                return ExecuteResult.Completed();
            },
        };

        await Factory.Orchestrator(out _).RunAsync([t], capture);

        var pulses = capture.Updates.Where(u => u.Message == "Đang thực hiện").ToList();
        Assert.True(pulses.Count >= 2, $"Chỉ có {pulses.Count} lần báo.");
        Assert.True(pulses[^1].Fraction > pulses[0].Fraction);
        Assert.All(pulses, p => Assert.True(p.Fraction < 0.9));
    }

    [Fact]
    public async Task Late_reports_after_the_attempt_ends_are_dropped()
    {
        var capture = new Capture();
        TaskContext? kept = null;
        var t = new FakeTask("a");
        t.Execute = (_, _) => Task.FromResult(ExecuteResult.Completed());
        var wrapper = new ContextKeeper(t, c => kept = c);

        await Factory.Orchestrator(out _).RunAsync([wrapper], capture);
        var count = capture.Updates.Count;
        kept!.Report(0.5, "báo trễ");

        Assert.Equal(count, capture.Updates.Count);
    }

    private sealed class ContextKeeper(ITaskModule inner, Action<TaskContext> keep) : ITaskModule
    {
        public TaskDescriptor Descriptor => inner.Descriptor;

        public Task<PreconditionResult> CheckPreconditionsAsync(TaskContext context, CancellationToken cancellationToken)
        {
            keep(context);
            return inner.CheckPreconditionsAsync(context, cancellationToken);
        }

        public Task<ExecuteResult> ExecuteAsync(TaskContext context, CancellationToken cancellationToken) => inner.ExecuteAsync(context, cancellationToken);

        public Task<VerifyResult> VerifyAsync(TaskContext context, CancellationToken cancellationToken) => inner.VerifyAsync(context, cancellationToken);

        public Task<VerifyResult> RollbackAsync(TaskContext context, CancellationToken cancellationToken) => inner.RollbackAsync(context, cancellationToken);
    }
}
