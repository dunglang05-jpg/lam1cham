using LamMotCham.Core.Logging;
using LamMotCham.Core.Resources;
using LamMotCham.Core.Tasks;

namespace LamMotCham.Core.Orchestration;

/// <summary>
/// Điều phối hàng đợi task theo tài liệu 04.
/// MVP chạy tuần tự từng task: bảo đảm chỉ một installer chạy tại một thời điểm và không chạy
/// tối ưu hệ thống song song với installer.
/// </summary>
public sealed class TaskOrchestrator
{
    private readonly IRunLogger _log;
    private readonly IResourceGovernor _governor;
    private readonly IDelayScheduler _delay;
    private readonly OrchestratorOptions _options;
    private readonly TimeProvider _time;
    private int _running;
    private bool _unresponsive;

    public TaskOrchestrator(
        IRunLogger logger,
        IResourceGovernor? governor = null,
        IDelayScheduler? delayScheduler = null,
        OrchestratorOptions? options = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _log = logger;
        _governor = governor ?? new UnrestrictedResourceGovernor();
        _time = timeProvider ?? TimeProvider.System;
        _delay = delayScheduler ?? new SystemDelayScheduler(_time);
        _options = options ?? new OrchestratorOptions();
    }

    /// <summary>Kiểm tra hàng đợi trước khi chạy: khai báo, ID trùng, phụ thuộc thiếu hoặc vòng.</summary>
    public static IReadOnlyList<string> ValidateQueue(IReadOnlyList<ITaskModule> tasks)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        var errors = new List<string>();

        if (tasks.Any(t => t is null || t.Descriptor is null))
        {
            errors.Add("Hàng đợi chứa task null hoặc thiếu khai báo.");
            return errors;
        }

        foreach (var task in tasks)
        {
            errors.AddRange(task.Descriptor.Validate());
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var task in tasks)
        {
            if (!ids.Add(task.Descriptor.Id))
            {
                errors.Add($"ID task bị trùng: '{task.Descriptor.Id}'.");
            }
        }

        foreach (var task in tasks)
        {
            foreach (var dep in task.Descriptor.DependsOn ?? [])
            {
                if (!ids.Contains(dep))
                {
                    errors.Add($"Task '{task.Descriptor.Id}' phụ thuộc vào task không có trong hàng đợi: '{dep}'.");
                }
            }
        }

        if (errors.Count == 0 && TryOrder(tasks, out _) is { } cycleError)
        {
            errors.Add(cycleError);
        }

        return errors;
    }

    /// <summary>Chạy hàng đợi chưa qua hộp xác nhận: task cần xác nhận riêng (<see cref="TaskDescriptor.RequiresConfirmation"/>) bị từ chối.</summary>
    public Task<RunReport> RunAsync(
        IReadOnlyList<ITaskModule> tasks,
        IProgress<TaskStatusUpdate>? progress = null,
        CancellationToken cancellationToken = default) =>
        RunAsync(tasks, confirmedByUser: false, progress, cancellationToken);

    /// <param name="tasks">Các task cần chạy.</param>
    /// <param name="confirmedByUser">true khi người dùng vừa xác nhận đúng danh sách này trong hộp xác nhận.</param>
    /// <param name="progress">Nhận cập nhật trạng thái.</param>
    /// <param name="cancellationToken">Hủy lượt chạy.</param>
    public async Task<RunReport> RunAsync(
        IReadOnlyList<ITaskModule> tasks,
        bool confirmedByUser,
        IProgress<TaskStatusUpdate>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tasks);

        if (Interlocked.Exchange(ref _running, 1) == 1)
        {
            throw new InvalidOperationException("Đang có một lượt chạy khác; không được chạy chồng.");
        }

        try
        {
            var errors = ValidateQueue(tasks);
            if (errors.Count == 0 && !confirmedByUser)
            {
                // Lớp chặn thứ hai: không dựa hoàn toàn vào giao diện để hỏi xác nhận trước task rủi ro.
                errors = [.. tasks.Where(t => t.Descriptor.RequiresConfirmation)
                    .Select(t => $"Task '{t.Descriptor.Id}' cần người dùng xác nhận trước khi chạy.")];
            }

            if (errors.Count > 0)
            {
                throw new QueueValidationException(errors);
            }

            _ = TryOrder(tasks, out var ordered);
            return await RunOrderedAsync(ordered, progress, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }

    private async Task<RunReport> RunOrderedAsync(
        IReadOnlyList<ITaskModule> ordered,
        IProgress<TaskStatusUpdate>? progress,
        CancellationToken userToken)
    {
        var startedAt = _time.GetUtcNow();
        var outcomes = new Dictionary<string, TaskOutcome>(StringComparer.Ordinal);
        var halt = RunHaltReason.None;

        foreach (var task in ordered)
        {
            outcomes[task.Descriptor.Id] = NewOutcome(task, TaskState.Pending, null);
            progress?.Report(new TaskStatusUpdate(task.Descriptor.Id, TaskState.Pending, null, 0));
        }

        _log.Info($"Bắt đầu lượt chạy với {ordered.Count} task.");

        foreach (var task in ordered)
        {
            var d = task.Descriptor;
            TaskOutcome outcome;

            if (halt is RunHaltReason.RebootRequired or RunHaltReason.UnresponsiveTask)
            {
                var reason = halt == RunHaltReason.RebootRequired
                    ? "Chờ khởi động lại máy trước khi chạy tiếp."
                    : "Hàng đợi đã dừng vì một task trước không phản hồi.";
                outcome = outcomes[d.Id] with { Reason = reason };
            }
            else if (userToken.IsCancellationRequested)
            {
                halt = RunHaltReason.CancelledByUser;
                outcome = Finish(task, TaskState.Cancelled, "Người dùng đã hủy trước khi task bắt đầu.", 0, null);
            }
            else if (FindUnsatisfiedDependency(d, outcomes) is { } dep)
            {
                outcome = Finish(
                    task,
                    TaskState.Skipped,
                    $"Bỏ qua vì task phụ thuộc '{dep.DisplayName}' kết thúc với trạng thái {dep.State}.",
                    0,
                    null);
            }
            else
            {
                // Kiểm tra chỉ đọc rất nhẹ: không chờ ngưỡng RAM (ngưỡng bảo vệ cài đặt và thay đổi cấu hình).
                var waitOutcome = d.Kind == TaskKind.Check
                    ? null
                    : await WaitForResourcesAsync(task, progress, userToken).ConfigureAwait(false);
                outcome = waitOutcome ?? await RunSingleAsync(task, progress, userToken).ConfigureAwait(false);

                if (outcome.State == TaskState.WaitingForReboot)
                {
                    halt = RunHaltReason.RebootRequired;
                }
                else if (outcome.State == TaskState.Cancelled)
                {
                    halt = RunHaltReason.CancelledByUser;
                }
                else if (_unresponsive)
                {
                    halt = RunHaltReason.UnresponsiveTask;
                }
            }

            outcomes[d.Id] = outcome;
            progress?.Report(new TaskStatusUpdate(d.Id, outcome.State, outcome.Reason, outcome.Attempts));
            if (outcome.State != TaskState.Pending)
            {
                _log.Log(LevelFor(outcome.State), $"{d.DisplayName}: {outcome.State}. {outcome.Reason}".Trim(), d.Id);
            }
        }

        _unresponsive = false;
        var report = new RunReport
        {
            StartedAt = startedAt,
            FinishedAt = _time.GetUtcNow(),
            Tasks = [.. ordered.Select(t => outcomes[t.Descriptor.Id])],
            HaltReason = halt,
        };

        _log.Info($"Kết thúc lượt chạy: {report.Count(TaskState.Success)} thành công, {report.Count(TaskState.Failed)} thất bại, " +
                  $"{report.Count(TaskState.NeedsAttention)} cần chú ý, {report.Count(TaskState.Skipped)} bỏ qua.");
        return report;
    }

    private async Task<TaskOutcome?> WaitForResourcesAsync(
        ITaskModule task,
        IProgress<TaskStatusUpdate>? progress,
        CancellationToken userToken)
    {
        var waited = TimeSpan.Zero;
        var announced = false;
        string? lastReason = null;

        while (true)
        {
            var decision = _governor.Evaluate();
            if (decision.CanStartNewTask)
            {
                if (decision.Reason is not null)
                {
                    _log.Info(decision.Reason, task.Descriptor.Id);
                }

                return null;
            }

            if (!announced)
            {
                _log.Warning(decision.Reason ?? "Đang chờ tài nguyên.", task.Descriptor.Id);
                announced = true;
            }

            // Báo lại mỗi lần đọc để màn hình hiện số RAM trống mới nhất (không chỉ lần đầu).
            if (decision.Reason != lastReason)
            {
                progress?.Report(new TaskStatusUpdate(task.Descriptor.Id, TaskState.Pending, decision.Reason, 0));
                lastReason = decision.Reason;
            }

            if (waited >= _options.MaxResourceWait)
            {
                return Finish(
                    task,
                    TaskState.NeedsAttention,
                    $"Tài nguyên không phục hồi sau {_options.MaxResourceWait.TotalMinutes:0.#} phút. {decision.Reason}",
                    0,
                    null);
            }

            try
            {
                await _delay.DelayAsync(_options.ResourcePollInterval, userToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (userToken.IsCancellationRequested)
            {
                return Finish(task, TaskState.Cancelled, "Người dùng đã hủy khi đang chờ tài nguyên.", 0, null);
            }

            waited += _options.ResourcePollInterval;
        }
    }

    private async Task<TaskOutcome> RunSingleAsync(
        ITaskModule task,
        IProgress<TaskStatusUpdate>? progress,
        CancellationToken userToken)
    {
        var d = task.Descriptor;
        var startedAt = _time.GetUtcNow();
        var attempt = 0;

        while (true)
        {
            attempt++;
            progress?.Report(new TaskStatusUpdate(d.Id, TaskState.Running, attempt > 1 ? $"Thử lại lần {attempt}" : null, attempt));
            _log.Info($"Bắt đầu {d.DisplayName} (lần {attempt}).", d.Id);

            using var timeoutCts = new CancellationTokenSource(d.Timeout);
            using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(userToken, timeoutCts.Token);

            // Task không được phép dừng cưỡng chế (ví dụ trình cài đặt bên ngoài) không nhận lệnh hủy:
            // người dùng hủy thì chờ bước hiện tại xong; quá thời gian thì ngừng chờ nhưng không dừng tiến trình.
            var taskToken = d.AllowForcedStop ? attemptCts.Token : CancellationToken.None;
            var waitToken = d.AllowForcedStop ? attemptCts.Token : timeoutCts.Token;
            var attemptNo = attempt;

            // Tiến độ trong task chỉ được báo khi lần thử còn chạy: báo trễ (từ bộ đếm giờ) sau khi task xong
            // sẽ bị bỏ, để dòng tác vụ không quay lại "Đang chạy".
            using var gate = new ProgressGate();
            var context = new TaskContext(
                _log,
                attempt,
                progress is null ? null : new ActionProgress<TaskProgress>(p =>
                    gate.Run(() =>
                    {
                        // Thanh không lùi trong một lần thử (ví dụ bước nhích theo thời gian chạy trước số byte tải thật).
                        var fraction = p.Fraction is { } f ? gate.Advance(f) : (double?)null;
                        progress.Report(new TaskStatusUpdate(d.Id, TaskState.Running, p.Text, attemptNo, fraction));
                    })));
            var work = RunAttemptAsync(task, context, taskToken);

            AttemptResult result;
            try
            {
                result = await work.WaitAsync(waitToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (waitToken.IsCancellationRequested && !d.AllowForcedStop)
            {
                _ = work.ContinueWith(t => _ = t.Exception, TaskScheduler.Default);
                _unresponsive = true;
                return Finish(
                    task,
                    TaskState.NeedsAttention,
                    $"Quá thời gian cho phép ({d.Timeout.TotalMinutes:0.#} phút). Ứng dụng không cưỡng chế dừng trình cài đặt; " +
                    "hàng đợi đã dừng để tránh chạy chồng. Kiểm tra máy rồi chạy lại.",
                    attempt,
                    startedAt);
            }
            catch (OperationCanceledException) when (attemptCts.IsCancellationRequested)
            {
                var stopped = await WaitForStopAsync(work).ConfigureAwait(false);
                if (!stopped)
                {
                    _unresponsive = true;
                    return Finish(
                        task,
                        TaskState.NeedsAttention,
                        "Task không dừng sau khi bị hủy/quá thời gian. Hàng đợi đã dừng để tránh chạy chồng; cần kiểm tra máy.",
                        attempt,
                        startedAt);
                }

                return userToken.IsCancellationRequested
                    ? Finish(task, TaskState.Cancelled, "Người dùng đã hủy khi task đang chạy.", attempt, startedAt)
                    : Finish(task, TaskState.Failed, $"Quá thời gian cho phép ({d.Timeout.TotalSeconds:0} giây).", attempt, startedAt);
            }
            catch (OperationCanceledException ex)
            {
                // Task tự hủy vì lý do riêng (timeout nội bộ, thư viện mạng...), không do người dùng hay hàng đợi.
                // Không để văng khỏi hàng đợi; task có thể đã thay đổi máy nên không coi là thất bại chắc chắn.
                return Finish(
                    task,
                    TaskState.NeedsAttention,
                    $"Tác vụ dừng bất thường ({ex.GetType().Name}: {ex.Message}). Không xác minh được kết quả; kiểm tra lại trên máy.",
                    attempt,
                    startedAt);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                result = AttemptResult.Final(TaskState.Failed, $"Lỗi không mong đợi: {ex.GetType().Name}: {ex.Message}");
            }

            if (result.IsRetryable && attempt < d.Retry.MaxAttempts && !userToken.IsCancellationRequested)
            {
                var wait = d.Retry.GetDelayBeforeAttempt(attempt + 1);
                _log.Warning($"Lỗi tạm thời: {result.Reason}. Thử lại sau {wait.TotalSeconds:0.#} giây.", d.Id);
                progress?.Report(new TaskStatusUpdate(d.Id, TaskState.Running, $"Lỗi tạm thời, thử lại sau {wait.TotalSeconds:0} giây", attempt));
                try
                {
                    await _delay.DelayAsync(wait, userToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (userToken.IsCancellationRequested)
                {
                    return Finish(task, TaskState.Cancelled, "Người dùng đã hủy khi đang chờ thử lại.", attempt, startedAt);
                }

                continue;
            }

            var reason = result.IsRetryable && d.Retry.MaxAttempts > 1
                ? $"{result.Reason} (đã thử {attempt} lần)"
                : result.Reason;
            return Finish(task, result.State, reason, attempt, startedAt);
        }
    }

    /// <summary>Cho task một khoảng thời gian để tự dừng sau khi bị hủy.</summary>
    private async Task<bool> WaitForStopAsync(Task<AttemptResult> work)
    {
        var grace = Task.Delay(_options.CancellationGracePeriod, _time);
        var first = await Task.WhenAny(work, grace).ConfigureAwait(false);
        if (first != work)
        {
            // Quan sát exception về sau để không bị unobserved task exception.
            _ = work.ContinueWith(t => _ = t.Exception, TaskScheduler.Default);
            return false;
        }

        _ = work.Exception;
        return true;
    }

    private static async Task<AttemptResult> RunAttemptAsync(ITaskModule task, TaskContext context, CancellationToken token)
    {
        // Mọi tác vụ (cài phần mềm, tối ưu Windows, dọn dẹp…) đều báo đang ở bước nào, để người dùng biết máy vẫn đang làm
        // (PO 01/11/2026). Tác vụ tự báo chi tiết hơn (ví dụ % tải) thì ghi đè các dòng chung này.
        var pre = await PhaseAsync(
                context,
                ProgressSteps.Check,
                ProgressSteps.Execute,
                8,
                "Đang kiểm tra trạng thái hiện tại",
                c => task.CheckPreconditionsAsync(c, token)).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Precondition trả về null.");

        switch (pre.Outcome)
        {
            case PreconditionOutcome.NotSupported:
                return AttemptResult.Final(TaskState.NotSupported, pre.Reason ?? "Máy không hỗ trợ task này.");

            case PreconditionOutcome.NotMet:
                return AttemptResult.Final(TaskState.Skipped, pre.Reason ?? "Điều kiện chạy không đạt.");

            case PreconditionOutcome.AlreadyInDesiredState:
                {
                    var verify = await PhaseAsync(context, ProgressSteps.Verify, 1, 15, "Đã đúng sẵn, đang xác minh lại", c => VerifySafelyAsync(task, c, token))
                        .ConfigureAwait(false);
                    return MapVerify(
                        verify,
                        "Đã ở trạng thái mong muốn, không thay đổi.",
                        "Precondition báo đã đúng nhưng xác minh không đạt.");
                }

            case PreconditionOutcome.Met:
                break;

            default:
                throw new InvalidOperationException($"Precondition không hợp lệ: {pre.Outcome}.");
        }

        var exec = await PhaseAsync(context, ProgressSteps.Execute, ProgressSteps.Verify, 20, "Đang thực hiện", c => task.ExecuteAsync(c, token))
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("Execute trả về null.");

        switch (exec.Outcome)
        {
            case ExecuteOutcome.Failed:
                {
                    var reason = exec.ErrorCode is null ? exec.Reason : $"{exec.Reason} (mã lỗi {exec.ErrorCode})";
                    return exec.IsTransient
                        ? AttemptResult.Retryable(reason ?? "Lỗi tạm thời.")
                        : AttemptResult.Final(TaskState.Failed, reason ?? "Thực hiện thất bại.");
                }

            case ExecuteOutcome.CompletedRebootRequired:
                return AttemptResult.Final(TaskState.WaitingForReboot, exec.Reason ?? "Cần khởi động lại máy để hoàn tất.");

            case ExecuteOutcome.Completed:
                {
                    var verify = await PhaseAsync(context, ProgressSteps.Verify, 1, 15, "Đang xác minh kết quả", c => VerifySafelyAsync(task, c, token))
                        .ConfigureAwait(false);
                    if (verify.Outcome == VerifyOutcome.Passed && verify.Detail is null)
                    {
                        verify = verify with { Detail = exec.Reason };
                    }

                    return MapVerify(verify, null, "Xác minh không đạt.");
                }

            default:
                throw new InvalidOperationException($"Kết quả execute không hợp lệ: {exec.Outcome}.");
        }
    }

    /// <summary>
    /// Chạy một bước (kiểm tra, thực hiện, xác minh) và giữ cho dòng tiến độ luôn "sống": báo chữ của bước lúc bắt đầu;
    /// nếu tác vụ im lặng quá 1,5 giây thì mỗi giây báo lại chữ gần nhất và nhích thanh dần từ <paramref name="from"/>
    /// về phía <paramref name="to"/> (không bao giờ tới). Thanh không lùi vì lớp báo tiến độ của lần thử đã chặn lùi.
    /// </summary>
    private static async Task<T> PhaseAsync<T>(
        TaskContext context,
        double from,
        double to,
        double halfSeconds,
        string text,
        Func<TaskContext, Task<T>> body)
    {
        if (context.Progress is not { } outer)
        {
            return await body(context).ConfigureAwait(false);
        }

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var gate = new Lock();
        var lastText = text;
        var lastSelf = TimeSpan.Zero;
        var relay = context with
        {
            Progress = new ActionProgress<TaskProgress>(p =>
            {
                lock (gate)
                {
                    lastSelf = watch.Elapsed;
                    if (!string.IsNullOrWhiteSpace(p.Text))
                    {
                        lastText = p.Text;
                    }
                }

                outer.Report(p);
            }),
        };

        context.Report(from, text);
        using var stop = new CancellationTokenSource();
        var pulse = Task.Run(
            async () =>
            {
                while (true)
                {
                    try
                    {
                        await Task.Delay(1000, stop.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }

                    string current;
                    lock (gate)
                    {
                        if (watch.Elapsed - lastSelf < TimeSpan.FromSeconds(1.5))
                        {
                            continue;
                        }

                        current = lastText;
                    }

                    var eased = from + (to - from) * (1 - Math.Pow(0.5, watch.Elapsed.TotalSeconds / halfSeconds));
                    context.Report(Math.Min(eased, to - 0.001), current);
                }
            },
            CancellationToken.None);

        try
        {
            return await body(relay).ConfigureAwait(false);
        }
        finally
        {
            await stop.CancelAsync().ConfigureAwait(false);
            await pulse.ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Lỗi trong Verify không được biến thành SUCCESS và cũng không phải FAILED chắc chắn,
    /// vì Execute có thể đã thay đổi hệ thống: trả về Inconclusive.
    /// </summary>
    private static async Task<VerifyResult> VerifySafelyAsync(ITaskModule task, TaskContext context, CancellationToken token)
    {
        try
        {
            return await task.VerifyAsync(context, token).ConfigureAwait(false)
                ?? VerifyResult.Inconclusive("Verify trả về null.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return VerifyResult.Inconclusive($"Lỗi khi xác minh: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>Ánh xạ kết quả Verify sang trạng thái. Chỉ <see cref="VerifyOutcome.Passed"/> cho ra SUCCESS.</summary>
    private static AttemptResult MapVerify(VerifyResult verify, string? passedPrefix, string failedPrefix) => verify.Outcome switch
    {
        VerifyOutcome.Passed => AttemptResult.Final(
            TaskState.Success,
            passedPrefix is null ? verify.Detail : Join(passedPrefix, verify.Detail)),
        VerifyOutcome.Failed => AttemptResult.Final(TaskState.Failed, Join(failedPrefix, verify.Detail)),
        VerifyOutcome.Attention => AttemptResult.Final(TaskState.NeedsAttention, verify.Detail ?? "Cần kiểm tra lại."),
        VerifyOutcome.RequiresPhysicalCheck => AttemptResult.Final(
            TaskState.NotTested,
            verify.Detail ?? "Cần kiểm tra vật lý."),
        VerifyOutcome.NotSupported => AttemptResult.Final(TaskState.NotSupported, verify.Detail ?? "Máy không hỗ trợ."),
        _ => AttemptResult.Final(TaskState.NeedsAttention, Join("Không xác minh được.", verify.Detail)),
    };

    private static TaskOutcome? FindUnsatisfiedDependency(TaskDescriptor descriptor, Dictionary<string, TaskOutcome> outcomes) =>
        descriptor.DependsOn
            .Select(id => outcomes[id])
            .FirstOrDefault(o => !o.State.SatisfiesDependency());

    /// <summary>Sắp xếp topo ổn định: giữ thứ tự gốc khi không có ràng buộc phụ thuộc.</summary>
    private static string? TryOrder(IReadOnlyList<ITaskModule> tasks, out IReadOnlyList<ITaskModule> ordered)
    {
        var placed = new HashSet<string>(StringComparer.Ordinal);
        var remaining = tasks.ToList();
        var result = new List<ITaskModule>(tasks.Count);

        while (remaining.Count > 0)
        {
            var next = remaining.FirstOrDefault(t => t.Descriptor.DependsOn.All(placed.Contains));
            if (next is null)
            {
                ordered = result;
                return "Phát hiện phụ thuộc vòng giữa các task: " +
                       string.Join(", ", remaining.Select(t => $"'{t.Descriptor.Id}'")) + ".";
            }

            result.Add(next);
            placed.Add(next.Descriptor.Id);
            remaining.Remove(next);
        }

        ordered = result;
        return null;
    }

    private TaskOutcome Finish(ITaskModule task, TaskState state, string? reason, int attempts, DateTimeOffset? startedAt) =>
        NewOutcome(task, state, reason) with
        {
            Attempts = attempts,
            StartedAt = startedAt,
            FinishedAt = state == TaskState.Pending ? null : _time.GetUtcNow(),
        };

    private static TaskOutcome NewOutcome(ITaskModule task, TaskState state, string? reason) => new()
    {
        TaskId = task.Descriptor.Id,
        DisplayName = task.Descriptor.DisplayName,
        Kind = task.Descriptor.Kind,
        Risk = task.Descriptor.Risk,
        State = state,
        Reason = reason is null ? null : Redactor.Redact(reason),
    };

    private static LogLevel LevelFor(TaskState state) => state switch
    {
        TaskState.Success => LogLevel.Info,
        TaskState.Failed => LogLevel.Error,
        _ => LogLevel.Warning,
    };

    private static string Join(string prefix, string? detail) =>
        string.IsNullOrWhiteSpace(detail) ? prefix : $"{prefix} {detail}";

    private readonly record struct AttemptResult(TaskState State, string? Reason, bool IsRetryable)
    {
        public static AttemptResult Final(TaskState state, string? reason) => new(state, reason, false);

        public static AttemptResult Retryable(string reason) => new(TaskState.Failed, reason, true);
    }
}

/// <summary>Chuyển tiếp tiến độ ngay (không qua SynchronizationContext); lớp ngoài đã tự đưa về luồng giao diện.</summary>
internal sealed class ActionProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}

/// <summary>Cho phép báo tiến độ đến khi Dispose; sau đó mọi lần báo bị bỏ qua.</summary>
internal sealed class ProgressGate : IDisposable
{
    private readonly Lock _lock = new();
    private bool _open = true;
    private double _highest;

    /// <summary>Phần đã xong không nhỏ hơn lần báo trước (gọi trong <see cref="Run"/>).</summary>
    public double Advance(double fraction)
    {
        _highest = Math.Max(_highest, fraction);
        return _highest;
    }

    public void Run(Action action)
    {
        lock (_lock)
        {
            if (_open)
            {
                action();
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _open = false;
        }
    }
}
