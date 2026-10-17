namespace LamMotCham.Core.Tasks;

/// <summary>
/// Chính sách retry có giới hạn và backoff. Chỉ áp dụng cho lỗi được task đánh dấu là tạm thời.
/// </summary>
public sealed record RetryPolicy
{
    /// <summary>Giới hạn cứng để tránh retry gần như vô hạn do cấu hình sai.</summary>
    public const int MaxAllowedAttempts = 5;

    /// <summary>Không retry: chỉ chạy một lần.</summary>
    public static RetryPolicy None { get; } = new() { MaxAttempts = 1 };

    /// <summary>Tổng số lần chạy, tính cả lần đầu.</summary>
    public int MaxAttempts { get; init; } = 1;

    public TimeSpan InitialDelay { get; init; } = TimeSpan.FromSeconds(2);

    public double BackoffFactor { get; init; } = 2.0;

    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Thời gian chờ trước lần thử thứ <paramref name="nextAttempt"/> (bắt đầu từ 2).</summary>
    public TimeSpan GetDelayBeforeAttempt(int nextAttempt)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(nextAttempt, 2);

        var exponent = nextAttempt - 2;
        var ms = InitialDelay.TotalMilliseconds * Math.Pow(BackoffFactor, exponent);
        return TimeSpan.FromMilliseconds(Math.Min(ms, MaxDelay.TotalMilliseconds));
    }

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (MaxAttempts is < 1 or > MaxAllowedAttempts)
        {
            errors.Add($"số lần thử phải trong khoảng 1..{MaxAllowedAttempts}.");
        }

        if (InitialDelay < TimeSpan.Zero || MaxDelay < TimeSpan.Zero)
        {
            errors.Add("thời gian chờ retry không được âm.");
        }

        if (BackoffFactor < 1.0)
        {
            errors.Add("hệ số backoff phải >= 1.");
        }

        return errors;
    }
}
