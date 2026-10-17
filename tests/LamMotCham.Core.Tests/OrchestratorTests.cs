using LamMotCham.Core.Orchestration;
using LamMotCham.Core.Tasks;
using Xunit;

namespace LamMotCham.Core.Tests;

public sealed class OrchestratorTests
{
    [Fact]
    public async Task Success_only_after_verify_passes()
    {
        var t = new FakeTask("a");
        var report = await Factory.Orchestrator(out _).RunAsync([t]);

        Assert.Equal(TaskState.Success, report.Of("a").State);
        Assert.Equal(1, t.ExecuteCalls);
        Assert.True(report.AllSucceeded);
    }

    [Fact]
    public async Task Execute_ok_but_verify_failed_is_failed()
    {
        var t = new FakeTask("a") { Verify = _ => Task.FromResult(VerifyResult.Failed("giá trị không khớp")) };
        var report = await Factory.Orchestrator(out _).RunAsync([t]);

        Assert.Equal(TaskState.Failed, report.Of("a").State);
        Assert.Contains("giá trị không khớp", report.Of("a").Reason);
    }

    [Fact]
    public async Task Inconclusive_verify_is_needs_attention_never_success()
    {
        var t = new FakeTask("a") { Verify = _ => Task.FromResult(VerifyResult.Inconclusive("không đọc được")) };
        var report = await Factory.Orchestrator(out _).RunAsync([t]);

        Assert.Equal(TaskState.NeedsAttention, report.Of("a").State);
    }

    [Fact]
    public async Task Verify_throwing_is_needs_attention()
    {
        var t = new FakeTask("a") { Verify = _ => throw new InvalidOperationException("WMI lỗi") };
        var report = await Factory.Orchestrator(out _).RunAsync([t]);

        Assert.Equal(TaskState.NeedsAttention, report.Of("a").State);
    }

    [Fact]
    public async Task Check_verify_outcomes_map_to_standard_states()
    {
        var phys = new FakeTask("p", TaskKind.Check) { Verify = _ => Task.FromResult(VerifyResult.RequiresPhysicalCheck("cần nghe thử")) };
        var na = new FakeTask("n", TaskKind.Check) { Verify = _ => Task.FromResult(VerifyResult.NotSupported("không có pin")) };
        var att = new FakeTask("w", TaskKind.Check) { Verify = _ => Task.FromResult(VerifyResult.Attention("driver lỗi")) };
        var report = await Factory.Orchestrator(out _).RunAsync([phys, na, att]);

        Assert.Equal(TaskState.NotTested, report.Of("p").State);
        Assert.Equal(TaskState.NotSupported, report.Of("n").State);
        Assert.Equal(TaskState.NeedsAttention, report.Of("w").State);
    }

    [Fact]
    public async Task Already_in_desired_state_skips_execute_but_still_verifies()
    {
        var verified = false;
        var t = new FakeTask("a")
        {
            Precondition = _ => Task.FromResult(PreconditionResult.AlreadyInDesiredState("đã bật")),
            Verify = _ => { verified = true; return Task.FromResult(VerifyResult.Passed()); },
        };
        var report = await Factory.Orchestrator(out _).RunAsync([t]);

        Assert.Equal(TaskState.Success, report.Of("a").State);
        Assert.Equal(0, t.ExecuteCalls);
        Assert.True(verified);
        Assert.Contains("không thay đổi", report.Of("a").Reason);
    }

    [Fact]
    public async Task Precondition_not_met_and_not_supported_do_not_execute()
    {
        var a = new FakeTask("a") { Precondition = _ => Task.FromResult(PreconditionResult.NotMet("thiếu mạng")) };
        var b = new FakeTask("b") { Precondition = _ => Task.FromResult(PreconditionResult.NotSupported("không có WinGet")) };
        var report = await Factory.Orchestrator(out _).RunAsync([a, b]);

        Assert.Equal(TaskState.Skipped, report.Of("a").State);
        Assert.Equal(TaskState.NotSupported, report.Of("b").State);
        Assert.Equal(0, a.ExecuteCalls);
        Assert.Equal(0, b.ExecuteCalls);
    }

    [Fact]
    public async Task Dependent_of_failed_task_is_skipped_and_independent_continues()
    {
        var a = new FakeTask("a") { Execute = (_, _) => Task.FromResult(ExecuteResult.Failed("lỗi")) };
        var b = new FakeTask("b", TaskKind.Configuration, "a");
        var c = new FakeTask("c");
        var report = await Factory.Orchestrator(out _).RunAsync([a, b, c]);

        Assert.Equal(TaskState.Failed, report.Of("a").State);
        Assert.Equal(TaskState.Skipped, report.Of("b").State);
        Assert.Contains("Task a", report.Of("b").Reason);
        Assert.Equal(0, b.ExecuteCalls);
        Assert.Equal(TaskState.Success, report.Of("c").State);
    }

    [Fact]
    public async Task Dependencies_run_first_even_if_listed_later()
    {
        var order = new List<string>();
        var b = new FakeTask("b", TaskKind.Configuration, "a") { Execute = (_, _) => { order.Add("b"); return Task.FromResult(ExecuteResult.Completed()); } };
        var a = new FakeTask("a") { Execute = (_, _) => { order.Add("a"); return Task.FromResult(ExecuteResult.Completed()); } };
        await Factory.Orchestrator(out _).RunAsync([b, a]);

        Assert.Equal(new[] { "a", "b" }, order.ToArray());
    }

    [Fact]
    public async Task Exception_in_execute_fails_task_without_stopping_queue()
    {
        var a = new FakeTask("a") { Execute = (_, _) => throw new IOException("đĩa bận") };
        var b = new FakeTask("b");
        var report = await Factory.Orchestrator(out _).RunAsync([a, b]);

        Assert.Equal(TaskState.Failed, report.Of("a").State);
        Assert.Contains("IOException", report.Of("a").Reason);
        Assert.Equal(TaskState.Success, report.Of("b").State);
    }

    [Fact]
    public async Task Transient_failure_retries_with_increasing_backoff()
    {
        var delay = new RecordingDelay();
        var a = new FakeTask("a")
        {
            Execute = (n, _) => Task.FromResult(n < 3 ? ExecuteResult.Failed("mạng chập chờn", isTransient: true) : ExecuteResult.Completed()),
        };
        a.Descriptor = a.Descriptor with { Retry = new RetryPolicy { MaxAttempts = 3, InitialDelay = TimeSpan.FromSeconds(2), BackoffFactor = 2 } };
        var report = await Factory.Orchestrator(out _, delay: delay).RunAsync([a]);

        Assert.Equal(TaskState.Success, report.Of("a").State);
        Assert.Equal(3, report.Of("a").Attempts);
        Assert.Equal(new[] { TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4) }, delay.Delays.ToArray());
    }

    [Fact]
    public async Task Non_transient_failure_is_never_retried()
    {
        var a = new FakeTask("a") { Execute = (_, _) => Task.FromResult(ExecuteResult.Failed("bị chính sách chặn")) };
        a.Descriptor = a.Descriptor with { Retry = new RetryPolicy { MaxAttempts = 3 } };
        var report = await Factory.Orchestrator(out _).RunAsync([a]);

        Assert.Equal(TaskState.Failed, report.Of("a").State);
        Assert.Equal(1, a.ExecuteCalls);
    }

    [Fact]
    public async Task Retries_are_bounded()
    {
        var a = new FakeTask("a") { Execute = (_, _) => Task.FromResult(ExecuteResult.Failed("tải lỗi", true, "0x8A150008")) };
        a.Descriptor = a.Descriptor with { Retry = new RetryPolicy { MaxAttempts = 3 } };
        var report = await Factory.Orchestrator(out _).RunAsync([a]);

        Assert.Equal(TaskState.Failed, report.Of("a").State);
        Assert.Equal(3, a.ExecuteCalls);
        Assert.Contains("0x8A150008", report.Of("a").Reason);
        Assert.Contains("đã thử 3 lần", report.Of("a").Reason);
    }

    [Fact]
    public async Task Timeout_fails_task_and_cancels_its_token()
    {
        var a = new FakeTask("a") { Execute = async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return ExecuteResult.Completed(); } };
        a.Descriptor = a.Descriptor with { Timeout = TimeSpan.FromMilliseconds(100) };
        var b = new FakeTask("b");
        var report = await Factory.Orchestrator(out _).RunAsync([a, b]);

        Assert.Equal(TaskState.Failed, report.Of("a").State);
        Assert.Contains("Quá thời gian", report.Of("a").Reason);
        Assert.Equal(TaskState.Success, report.Of("b").State);
    }

    [Fact]
    public async Task User_cancel_before_start_cancels_everything()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var a = new FakeTask("a");
        var report = await Factory.Orchestrator(out _).RunAsync([a], null, cts.Token);

        Assert.Equal(TaskState.Cancelled, report.Of("a").State);
        Assert.Equal(0, a.ExecuteCalls);
        Assert.Equal(RunHaltReason.CancelledByUser, report.HaltReason);
    }

    [Fact]
    public async Task User_cancel_during_cooperative_task_cancels_it_and_the_rest()
    {
        using var cts = new CancellationTokenSource();
        var a = new FakeTask("a")
        {
            Execute = async (_, ct) => { await cts.CancelAsync(); await Task.Delay(Timeout.Infinite, ct); return ExecuteResult.Completed(); },
        };
        var b = new FakeTask("b");
        var report = await Factory.Orchestrator(out _).RunAsync([a, b], null, cts.Token);

        Assert.Equal(TaskState.Cancelled, report.Of("a").State);
        Assert.Equal(TaskState.Cancelled, report.Of("b").State);
        Assert.Equal(0, b.ExecuteCalls);
    }

    [Fact]
    public async Task User_cancel_does_not_interrupt_installer_and_stops_after_it()
    {
        using var cts = new CancellationTokenSource();
        var installer = new FakeTask("i", TaskKind.Installer)
        {
            Execute = async (_, ct) =>
            {
                await cts.CancelAsync();
                await Task.Delay(50, CancellationToken.None);
                return ct.IsCancellationRequested ? ExecuteResult.Failed("bị hủy giữa chừng") : ExecuteResult.Completed();
            },
        };
        installer.Descriptor = installer.Descriptor with { AllowForcedStop = false, Risk = RiskLevel.SoftwareChange };
        var next = new FakeTask("n");
        var report = await Factory.Orchestrator(out _).RunAsync([installer, next], null, cts.Token);

        Assert.Equal(TaskState.Success, report.Of("i").State);
        Assert.False(installer.LastToken.CanBeCanceled);
        Assert.Equal(TaskState.Cancelled, report.Of("n").State);
        Assert.Equal(RunHaltReason.CancelledByUser, report.HaltReason);
    }

    [Fact]
    public async Task Installer_timeout_is_not_killed_and_halts_queue()
    {
        var release = new TaskCompletionSource();
        var installer = new FakeTask("i", TaskKind.Installer)
        {
            Execute = async (_, _) => { await release.Task; return ExecuteResult.Completed(); },
        };
        installer.Descriptor = installer.Descriptor with { AllowForcedStop = false, Timeout = TimeSpan.FromMilliseconds(100), Risk = RiskLevel.SoftwareChange };
        var next = new FakeTask("n");
        var report = await Factory.Orchestrator(out _).RunAsync([installer, next]);
        release.SetResult();

        Assert.Equal(TaskState.NeedsAttention, report.Of("i").State);
        Assert.Contains("không cưỡng chế", report.Of("i").Reason);
        Assert.Equal(TaskState.Pending, report.Of("n").State);
        Assert.Equal(RunHaltReason.UnresponsiveTask, report.HaltReason);
    }

    [Fact]
    public async Task Task_ignoring_cancellation_halts_queue()
    {
        var release = new TaskCompletionSource();
        var a = new FakeTask("a") { Execute = async (_, _) => { await release.Task; return ExecuteResult.Completed(); } };
        a.Descriptor = a.Descriptor with { Timeout = TimeSpan.FromMilliseconds(100) };
        var b = new FakeTask("b");
        var report = await Factory.Orchestrator(out _).RunAsync([a, b]);
        release.SetResult();

        Assert.Equal(TaskState.NeedsAttention, report.Of("a").State);
        Assert.Equal(TaskState.Pending, report.Of("b").State);
        Assert.Equal(0, b.ExecuteCalls);
    }

    [Fact]
    public async Task Reboot_required_stops_queue_without_rebooting()
    {
        var a = new FakeTask("a") { Execute = (_, _) => Task.FromResult(ExecuteResult.RebootRequired("đổi tên máy")) };
        var b = new FakeTask("b");
        var report = await Factory.Orchestrator(out _).RunAsync([a, b]);

        Assert.Equal(TaskState.WaitingForReboot, report.Of("a").State);
        Assert.Equal(TaskState.Pending, report.Of("b").State);
        Assert.Contains("khởi động lại", report.Of("b").Reason);
        Assert.Equal(RunHaltReason.RebootRequired, report.HaltReason);
    }

    [Fact]
    public async Task Only_one_task_runs_at_a_time()
    {
        var running = 0;
        var max = 0;
        Task<ExecuteResult> Work(int _, CancellationToken __)
        {
            var now = Interlocked.Increment(ref running);
            max = Math.Max(max, now);
            Thread.Sleep(20);
            Interlocked.Decrement(ref running);
            return Task.FromResult(ExecuteResult.Completed());
        }

        var tasks = Enumerable.Range(0, 5).Select(i => (ITaskModule)new FakeTask("i" + i, TaskKind.Installer) { Execute = Work }).ToList();
        await Factory.Orchestrator(out _).RunAsync(tasks);

        Assert.Equal(1, max);
    }

    [Fact]
    public async Task Concurrent_runs_on_same_orchestrator_are_rejected()
    {
        var gate = new TaskCompletionSource();
        var a = new FakeTask("a") { Execute = async (_, _) => { await gate.Task; return ExecuteResult.Completed(); } };
        var orchestrator = Factory.Orchestrator(out _);
        var first = orchestrator.RunAsync([a]);

        await Assert.ThrowsAsync<InvalidOperationException>(() => orchestrator.RunAsync([new FakeTask("b")]));
        gate.SetResult();
        await first;
    }

    [Fact]
    public async Task Invalid_queue_is_rejected_before_anything_runs()
    {
        var a = new FakeTask("a");
        var dup = new FakeTask("a");
        var missing = new FakeTask("m", TaskKind.Configuration, "khong-co");
        var ex = await Assert.ThrowsAsync<QueueValidationException>(() => Factory.Orchestrator(out _).RunAsync([a, dup, missing]));

        Assert.Contains(ex.Errors, e => e.Contains("trùng", StringComparison.Ordinal));
        Assert.Contains(ex.Errors, e => e.Contains("khong-co", StringComparison.Ordinal));
        Assert.Equal(0, a.ExecuteCalls);
    }

    [Fact]
    public void Dependency_cycle_is_detected()
    {
        var a = new FakeTask("a", TaskKind.Configuration, "b");
        var b = new FakeTask("b", TaskKind.Configuration, "a");
        var errors = TaskOrchestrator.ValidateQueue([a, b]);

        Assert.Contains(errors, e => e.Contains("vòng", StringComparison.Ordinal));
    }

    [Fact]
    public void Descriptor_rules_are_enforced()
    {
        var bad = new FakeTask("x");
        bad.Descriptor = bad.Descriptor with { Rollback = RollbackSupport.NotPossible, RollbackNote = null, Timeout = TimeSpan.Zero };
        var errors = bad.Descriptor.Validate();

        Assert.Contains(errors, e => e.Contains("lý do", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("timeout", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Waits_for_resources_before_starting()
    {
        var delay = new RecordingDelay();
        var a = new FakeTask("a");
        var report = await Factory.Orchestrator(out _, new ScriptedGovernor(false, false, true), delay).RunAsync([a]);

        Assert.Equal(TaskState.Success, report.Of("a").State);
        Assert.Equal(2, delay.Delays.Count);
    }

    [Fact]
    public async Task Gives_up_when_resources_never_recover()
    {
        var options = new OrchestratorOptions { ResourcePollInterval = TimeSpan.FromMinutes(1), MaxResourceWait = TimeSpan.FromMinutes(3) };
        var a = new FakeTask("a");
        var report = await Factory.Orchestrator(out _, new ScriptedGovernor(Enumerable.Repeat(false, 50).ToArray()), null, options).RunAsync([a]);

        Assert.Equal(TaskState.NeedsAttention, report.Of("a").State);
        Assert.Equal(0, a.ExecuteCalls);
    }

    [Fact]
    public async Task Secrets_in_failure_reasons_are_redacted()
    {
        var a = new FakeTask("a") { Execute = (_, _) => Task.FromResult(ExecuteResult.Failed("lỗi với password=Hunter2!")) };
        var report = await Factory.Orchestrator(out var log).RunAsync([a]);

        Assert.DoesNotContain("Hunter2", report.Of("a").Reason);
        Assert.DoesNotContain(log.Snapshot(), e => e.Message.Contains("Hunter2", StringComparison.Ordinal));
    }
}
