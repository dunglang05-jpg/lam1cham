namespace LamMotCham.Core.Resources;

/// <summary>Ảnh chụp tài nguyên tại một thời điểm. Giá trị null nghĩa là không đọc được.</summary>
public sealed record ResourceSnapshot(double? AvailableMemoryPercent, double? CpuPercent = null, ulong? AvailableMemoryBytes = null);

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
    /// <summary>
    /// Ngưỡng % bắt đầu tạm dừng. 15% ban đầu (tài liệu 04) quá chặt: laptop 8 GB mở Chrome thường chỉ còn ~1 GB (12%)
    /// và cả hàng đợi đứng chờ (PO báo 01/11/2026). Hạ còn 8% và chỉ dừng khi RAM trống cũng dưới <see cref="PauseBelowAvailableBytes"/>.
    /// </summary>
    public double PauseBelowAvailableMemoryPercent { get; init; } = 8;

    /// <summary>Ngưỡng % cho phép chạy tiếp; phải cao hơn ngưỡng tạm dừng (hysteresis).</summary>
    public double ResumeAboveAvailableMemoryPercent { get; init; } = 12;

    /// <summary>Chỉ tạm dừng khi RAM trống dưới mức này (máy nhiều RAM còn 8% vẫn dư sức cài). Không đọc được số byte thì chỉ xét %.</summary>
    public ulong PauseBelowAvailableBytes { get; init; } = 1UL << 30;

    /// <summary>RAM trống đạt mức này thì cho chạy tiếp dù % còn thấp.</summary>
    public ulong ResumeAboveAvailableBytes { get; init; } = 3UL << 29;

    /// <summary>
    /// true: RAM thấp thì chờ trước khi khởi chạy task mới. Mặc định false theo yêu cầu PO (01/11/2026):
    /// "không cần đợi RAM trống, cài luôn" — RAM thấp chỉ ghi cảnh báo vào log rồi chạy tiếp.
    /// </summary>
    public bool PauseWhenLow { get; init; }

    /// <summary>Thời gian tối thiểu giữ trạng thái tạm dừng trước khi được chạy tiếp.</summary>
    public TimeSpan Cooldown { get; init; } = TimeSpan.FromSeconds(10);

    public void Validate()
    {
        if (PauseBelowAvailableMemoryPercent is < 0 or > 100 || ResumeAboveAvailableMemoryPercent is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(PauseBelowAvailableMemoryPercent), "Ngưỡng phải trong khoảng 0..100.");
        }

        if (ResumeAboveAvailableMemoryPercent <= PauseBelowAvailableMemoryPercent || ResumeAboveAvailableBytes <= PauseBelowAvailableBytes)
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

            var bytes = snapshot.AvailableMemoryBytes;
            var current = Describe(available, bytes);
            if (!_paused)
            {
                var low = available < _options.PauseBelowAvailableMemoryPercent
                          && (bytes is null || bytes < _options.PauseBelowAvailableBytes);
                if (low && !_options.PauseWhenLow)
                {
                    return ResourceDecision.Allow($"RAM trống thấp ({current}): vẫn chạy theo cấu hình. Nếu cài lỗi, đóng bớt ứng dụng rồi chạy lại.");
                }

                if (low)
                {
                    _paused = true;
                    _pausedAt = now;
                    return ResourceDecision.Deny($"Đang chờ RAM trống: còn {current}, cần {ResumeText}. Đóng bớt ứng dụng (ví dụ Chrome) để chạy tiếp.");
                }

                return ResourceDecision.Allow();
            }

            var cooledDown = now - _pausedAt >= _options.Cooldown;
            var recovered = available > _options.ResumeAboveAvailableMemoryPercent
                            || (bytes is { } b && b >= _options.ResumeAboveAvailableBytes);
            if (recovered && cooledDown)
            {
                _paused = false;
                return ResourceDecision.Allow($"RAM trống đã đủ ({current}).");
            }

            return ResourceDecision.Deny($"Đang chờ RAM trống: còn {current}, cần {ResumeText}. Đóng bớt ứng dụng (ví dụ Chrome) để chạy tiếp.");
        }
    }

    private string ResumeText =>
        $"trên {Gb(_options.ResumeAboveAvailableBytes)} hoặc {_options.ResumeAboveAvailableMemoryPercent:0.#}%";

    private static string Describe(double percent, ulong? bytes) =>
        bytes is { } b ? $"{Gb(b)} ({percent:0.#}%)" : $"{percent:0.#}%";

    private static string Gb(ulong bytes) =>
        (bytes / (double)(1UL << 30)).ToString("0.0", System.Globalization.CultureInfo.GetCultureInfo("vi-VN")) + " GB";
}

/// <summary>Governor luôn cho phép; chỉ dùng khi chưa có monitor cho nền tảng hiện tại.</summary>
public sealed class UnrestrictedResourceGovernor : IResourceGovernor
{
    public ResourceDecision Evaluate() => ResourceDecision.Allow();
}
