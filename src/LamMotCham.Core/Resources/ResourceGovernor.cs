namespace LamMotCham.Core.Resources;

/// <summary>Ảnh chụp tài nguyên tại một thời điểm. Giá trị null nghĩa là không đọc được.</summary>
public sealed record ResourceSnapshot(double? AvailableMemoryPercent, double? CpuPercent = null);

public interface IResourceMonitor
{
    ResourceSnapshot Read();
}

public sealed record ResourceDecision(bool CanStartNewTask, string? Reason)
{
    public static ResourceDecision Allow(string? note = null) => new(true, note);

    public static ResourceDecision Deny(string reason) => new(false, reason);
}

public interface IResourceGovernor
{
    /// <summary>Quyết định có được khởi chạy task mới hay không. Không bao giờ dừng tiến trình đang chạy.</summary>
    ResourceDecision Evaluate();
}

public sealed record ResourceGovernorOptions
{
    /// <summary>Ngưỡng bắt đầu tạm dừng. 15% chỉ là giá trị khởi điểm để thử nghiệm (tài liệu 04).</summary>
    public double PauseBelowAvailableMemoryPercent { get; init; } = 15;

    /// <summary>Ngưỡng cho phép chạy tiếp; phải cao hơn ngưỡng tạm dừng (hysteresis).</summary>
    public double ResumeAboveAvailableMemoryPercent { get; init; } = 20;

    /// <summary>Thời gian tối thiểu giữ trạng thái tạm dừng trước khi được chạy tiếp.</summary>
    public TimeSpan Cooldown { get; init; } = TimeSpan.FromSeconds(10);

    public void Validate()
    {
        if (PauseBelowAvailableMemoryPercent is < 0 or > 100 || ResumeAboveAvailableMemoryPercent is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(PauseBelowAvailableMemoryPercent), "Ngưỡng phải trong khoảng 0..100.");
        }

        if (ResumeAboveAvailableMemoryPercent <= PauseBelowAvailableMemoryPercent)
        {
            throw new ArgumentException("Ngưỡng chạy tiếp phải cao hơn ngưỡng tạm dừng để tránh bật/tắt liên tục.");
        }

        if (Cooldown < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(Cooldown), "Cooldown không được âm.");
        }
    }
}

/// <summary>
/// Chặn khởi chạy task mới khi RAM khả dụng thấp, có hysteresis và cooldown.
/// </summary>
public sealed class ResourceGovernor : IResourceGovernor
{
    private readonly IResourceMonitor _monitor;
    private readonly ResourceGovernorOptions _options;
    private readonly TimeProvider _time;
    private readonly Lock _gate = new();
    private bool _paused;
    private DateTimeOffset _pausedAt;

    public ResourceGovernor(IResourceMonitor monitor, ResourceGovernorOptions? options = null, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        _monitor = monitor;
        _options = options ?? new ResourceGovernorOptions();
        _options.Validate();
        _time = timeProvider ?? TimeProvider.System;
    }

    public bool IsPaused
    {
        get
        {
            lock (_gate)
            {
                return _paused;
            }
        }
    }

    public ResourceDecision Evaluate()
    {
        var snapshot = _monitor.Read();
        var now = _time.GetUtcNow();

        lock (_gate)
        {
            if (snapshot.AvailableMemoryPercent is not { } available)
            {
                // Không đọc được số liệu: không chặn công việc, nhưng ghi rõ để người dùng biết.
                return _paused
                    ? ResourceDecision.Deny("Không đọc được RAM khả dụng; giữ trạng thái tạm dừng.")
                    : ResourceDecision.Allow("Không đọc được RAM khả dụng; tiếp tục nhưng không có bảo vệ tài nguyên.");
            }

            if (!_paused)
            {
                if (available < _options.PauseBelowAvailableMemoryPercent)
                {
                    _paused = true;
                    _pausedAt = now;
                    return ResourceDecision.Deny(
                        $"RAM khả dụng {available:0.#}% dưới ngưỡng {_options.PauseBelowAvailableMemoryPercent:0.#}%. Tạm dừng khởi chạy task mới.");
                }

                return ResourceDecision.Allow();
            }

            var cooledDown = now - _pausedAt >= _options.Cooldown;
            if (available > _options.ResumeAboveAvailableMemoryPercent && cooledDown)
            {
                _paused = false;
                return ResourceDecision.Allow($"RAM khả dụng đã phục hồi ({available:0.#}%).");
            }

            return ResourceDecision.Deny(
                $"Đang chờ RAM khả dụng vượt {_options.ResumeAboveAvailableMemoryPercent:0.#}% (hiện {available:0.#}%).");
        }
    }
}

/// <summary>Governor luôn cho phép; chỉ dùng khi chưa có monitor cho nền tảng hiện tại.</summary>
public sealed class UnrestrictedResourceGovernor : IResourceGovernor
{
    public ResourceDecision Evaluate() => ResourceDecision.Allow();
}
