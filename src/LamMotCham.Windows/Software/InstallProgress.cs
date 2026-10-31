using System.Diagnostics;
using System.Globalization;
using LamMotCham.Core.Tasks;

namespace LamMotCham.Windows.Software;

/// <summary>
/// Báo tiến độ bên trong một lần cài để thanh tiến trình chạy liên tục (PO báo 01/11/2026: "thanh tiến trình hoàn toàn không chạy").
/// Tải: theo số byte thật. Trình cài không báo tiến độ: thanh nhích dần theo thời gian (không bao giờ tới mức xong)
/// và ghi rõ "Đang chạy trình cài" — chỉ báo xong sau khi xác minh.
/// </summary>
internal static class InstallProgress
{
    /// <summary>Phần thanh dành cho bước tải.</summary>
    public const double DownloadShare = 0.6;

    /// <summary>Mức tối đa trước khi xác minh xong.</summary>
    public const double BeforeVerify = 0.95;

    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    /// <summary>Hàm nhận số byte đã tải, báo tối đa khoảng 3 lần mỗi giây.</summary>
    public static Action<long, long?> Download(TaskContext context, string? prefix = null)
    {
        var watch = Stopwatch.StartNew();
        var last = TimeSpan.MinValue;
        var gate = new Lock();
        return (bytes, total) =>
        {
            lock (gate)
            {
                var now = watch.Elapsed;
                var done = total is { } t && bytes >= t;
                if (!done && now - last < TimeSpan.FromMilliseconds(300))
                {
                    return;
                }

                last = now;
            }

            context.Report(total is > 0 and var size ? DownloadShare * bytes / size : null, DownloadText(bytes, total, prefix));
        };
    }

    internal static string DownloadText(long bytes, long? total, string? prefix = null)
    {
        var mb = (bytes / 1048576.0).ToString("0.0", Vi);
        var text = total is > 0 and var size
            ? $"Đang tải {bytes * 100 / size}% · {mb}/{(size / 1048576.0).ToString("0.0", Vi)} MB"
            : $"Đang tải {mb} MB";
        return prefix is null ? text : prefix + text;
    }

    /// <summary>Phần thanh theo thời gian chờ: tăng nhanh lúc đầu, chậm dần, không vượt <paramref name="to"/>.</summary>
    internal static double Ease(double from, double to, TimeSpan elapsed, TimeSpan halfTime) =>
        from + (to - from) * (1 - Math.Pow(0.5, elapsed.TotalSeconds / Math.Max(1, halfTime.TotalSeconds)));

    /// <summary>Nhích thanh mỗi giây trong lúc chạy việc không báo tiến độ (trình cài, WinGet). Dispose để dừng.</summary>
    public static IDisposable Pulse(TaskContext context, double from, double to, TimeSpan halfTime, string text)
    {
        ArgumentNullException.ThrowIfNull(context);
        var watch = Stopwatch.StartNew();
        void Tick(object? _)
        {
            var elapsed = watch.Elapsed;
            context.Report(Ease(from, to, elapsed, halfTime), $"{text} · {elapsed.ToString(@"mm\:ss", CultureInfo.InvariantCulture)}");
        }

        Tick(null);
        return new Timer(Tick, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }
}
