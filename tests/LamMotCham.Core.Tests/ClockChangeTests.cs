using LamMotCham.Core.Logging;
using LamMotCham.Core.Orchestration;
using LamMotCham.Core.Tasks;
using Xunit;

namespace LamMotCham.Core.Tests;

/// <summary>
/// 02/11/2026, máy mới của PO: giờ máy sai 15 tiếng, tác vụ Đồng bộ thời gian chỉnh lại giữa lượt chạy
/// nên báo cáo hiện thời gian chạy "-53992 giây". Thời gian chạy phải đo bằng đồng hồ đơn điệu.
/// </summary>
public sealed class ClockChangeTests
{
    [Fact]
    public async Task Duration_ignores_wall_clock_changes_during_a_task()
    {
        var time = new ManualTimeProvider(DateTimeOffset.Parse("2026-11-03T03:30:00+07:00", System.Globalization.CultureInfo.InvariantCulture));
        var sync = new FakeTask("win.timesync")
        {
            Execute = (_, _) =>
            {
                time.Advance(TimeSpan.FromHours(-15));
                return Task.FromResult(ExecuteResult.Completed());
            },
        };
        var orchestrator = new TaskOrchestrator(new InMemoryRunLogger(), delayScheduler: new RecordingDelay(), timeProvider: time);

        var report = await orchestrator.RunAsync([sync], confirmedByUser: true, progress: null, CancellationToken.None);
        var outcome = report.Of("win.timesync");

        Assert.Equal(TaskState.Success, outcome.State);
        Assert.True(outcome.FinishedAt < outcome.StartedAt);          // giờ hệ thống đã lùi
        Assert.NotNull(outcome.Duration);
        Assert.True(outcome.Duration >= TimeSpan.Zero);
        Assert.True(outcome.Duration < TimeSpan.FromMinutes(1));
    }
}
