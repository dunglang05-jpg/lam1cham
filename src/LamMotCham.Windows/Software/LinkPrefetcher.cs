using System.Collections.Concurrent;
using System.Diagnostics;

namespace LamMotCham.Windows.Software;

/// <summary>File cài đã tải trước, kèm thời gian trình cài phải chờ thêm khi lấy ra.</summary>
public sealed record PrefetchedFile(DownloadedFile File, string Note, TimeSpan Waited);

/// <summary>
/// Tải trước file cài của các ứng dụng cài bằng link trong lúc ứng dụng khác đang cài (PO duyệt 01/11/2026),
/// để lúc tới lượt chỉ còn kiểm tra chữ ký và chạy trình cài. Chỉ tải, không chạy gì;
/// chữ ký số vẫn được kiểm tra ngay trước khi cài. Tải lỗi thì bỏ qua — lúc cài sẽ tải lại như bình thường.
/// Mỗi lượt chạy một bộ tải trước; hết lượt thì hủy và xóa file chưa dùng.
/// </summary>
public sealed class LinkPrefetcher : IDisposable
{
    /// <summary>Số file tải cùng lúc.</summary>
    public const int MaxParallel = 2;

    /// <summary>Không tải trước khi ổ hệ thống còn ít hơn mức này (file tải trước nằm chờ trên ổ).</summary>
    public const long MinFreeBytes = 10L * 1024 * 1024 * 1024;

    private static LinkPrefetcher? _current;

    private readonly ConcurrentDictionary<DirectInstallTask, Job> _jobs = new(ReferenceEqualityComparer.Instance);
    private readonly SemaphoreSlim _slots = new(MaxParallel, MaxParallel);
    private readonly CancellationTokenSource _cts = new();
    private readonly Func<DirectInstallTask, Action<long, long?>, CancellationToken, Task<(DownloadedFile? File, string Note)>> _download;
    private int _disposed;

    public LinkPrefetcher(Func<DirectInstallTask, Action<long, long?>, CancellationToken, Task<(DownloadedFile? File, string Note)>>? download = null)
    {
        _download = download ?? DownloadAsync;
    }

    /// <summary>Bộ tải trước của lượt chạy hiện tại (null khi không có lượt nào).</summary>
    public static LinkPrefetcher? Current => Volatile.Read(ref _current);

    /// <summary>Bắt đầu một lượt: tải trước cho các ứng dụng sẽ cài bằng link (theo thứ tự cài).</summary>
    public static LinkPrefetcher Begin(IEnumerable<AppInstallTask> apps, Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(apps);
        var prefetcher = new LinkPrefetcher();
        Interlocked.Exchange(ref _current, prefetcher)?.Dispose();
        if (!HasRoomOnSystemDrive())
        {
            log?.Invoke("Ổ hệ thống còn dưới 10 GB: không tải trước, mỗi ứng dụng tải lúc tới lượt.");
            return prefetcher;
        }

        var started = prefetcher.Start(apps.Where(a => a.PrefersLink).Select(a => a.Direct!));
        if (started > 0)
        {
            log?.Invoke($"Tải trước {started} file cài (tối đa {MaxParallel} file cùng lúc) trong lúc cài.");
        }

        return prefetcher;
    }

    /// <summary>Kết thúc lượt: hủy tải dở và xóa file chưa dùng.</summary>
    public static void End(LinkPrefetcher prefetcher)
    {
        ArgumentNullException.ThrowIfNull(prefetcher);
        Interlocked.CompareExchange(ref _current, null, prefetcher);
        prefetcher.Dispose();
    }

    /// <summary>Xếp hàng tải cho từng task. Trả về số task đã xếp hàng.</summary>
    internal int Start(IEnumerable<DirectInstallTask> tasks)
    {
        var count = 0;
        foreach (var task in tasks)
        {
            if (!task.IsAutomatic || _jobs.ContainsKey(task))
            {
                continue;
            }

            var job = new Job(task.SelectedVariant);
            job.Work = RunAsync(task, job);
            _jobs[task] = job;
            count++;
        }

        return count;
    }

    /// <summary>
    /// Lấy file đã tải trước cho task (chờ nếu đang tải). Null nếu không tải trước, tải lỗi,
    /// hoặc người dùng đã đổi phiên bản — khi đó task tự tải như bình thường.
    /// </summary>
    /// <param name="onBytes">Trong lúc chờ file đang tải trước, báo số byte đã tải và tổng dung lượng (khoảng 0,5 giây một lần).</param>
    public async Task<PrefetchedFile?> TakeAsync(DirectInstallTask task, CancellationToken cancellationToken, Action<long, long?>? onBytes = null)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (!_jobs.TryRemove(task, out var job))
        {
            return null;
        }

        var watch = Stopwatch.StartNew();
        (DownloadedFile? File, string Note) result;
        try
        {
            var work = job.Work!;
            while (onBytes is not null && !work.IsCompleted)
            {
                onBytes(job.Bytes, job.Total);
                await Task.WhenAny(work, Task.Delay(500, cancellationToken)).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }

            result = await work.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            DisposeWhenDone(job);
            throw;
        }

        if (result.File is null)
        {
            return null;
        }

        if (!ReferenceEquals(job.Variant, task.SelectedVariant))
        {
            result.File.Dispose();
            return null;
        }

        return new PrefetchedFile(result.File, result.Note, watch.Elapsed);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        _cts.Cancel();
        foreach (var task in _jobs.Keys.ToList())
        {
            if (_jobs.TryRemove(task, out var job))
            {
                DisposeWhenDone(job);
            }
        }
    }

    private async Task<(DownloadedFile? File, string Note)> RunAsync(DirectInstallTask task, Job job)
    {
        await Task.Yield();
        var token = _cts.Token;
        try
        {
            await _slots.WaitAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return (null, "");
        }

        try
        {
            return await _download(task, job.OnBytes, token).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Tải trước chỉ là tăng tốc: lỗi bất kỳ thì lúc cài tải lại như bình thường.
        catch (Exception ex) when (ex is not OutOfMemoryException)
#pragma warning restore CA1031
        {
            return (null, "");
        }
        finally
        {
            _slots.Release();
        }
    }

    private static async Task<(DownloadedFile? File, string Note)> DownloadAsync(DirectInstallTask task, Action<long, long?> onBytes, CancellationToken token)
    {
        // Đã có trên máy thì lúc tới lượt sẽ báo "Đã cài sẵn": không tải phí.
        if (InstallDetector.FindEvidence(task.Package) is not null)
        {
            return (null, "");
        }

        var (url, note) = await task.ResolveAsync(token).ConfigureAwait(false);
        return url is null
            ? (null, note)
            : (await LinkDownloader.DownloadAsync(url, task.EffectiveKind, token, onBytes).ConfigureAwait(false), note);
    }

    private static void DisposeWhenDone(Job job) =>
        _ = job.Work!.ContinueWith(
            t =>
            {
                if (t.IsCompletedSuccessfully)
                {
                    t.Result.File?.Dispose();
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    private static bool HasRoomOnSystemDrive()
    {
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\");
            return drive.AvailableFreeSpace >= MinFreeBytes;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private sealed class Job(PackageVariant? variant)
    {
        private long _bytes;
        private long _total = -1;

        public PackageVariant? Variant { get; } = variant;

        public long Bytes => Interlocked.Read(ref _bytes);

        public long? Total => Interlocked.Read(ref _total) is >= 0 and var t ? t : (long?)null;

        public void OnBytes(long bytes, long? total)
        {
            Interlocked.Exchange(ref _bytes, bytes);
            Interlocked.Exchange(ref _total, total ?? -1);
        }

        public Task<(DownloadedFile? File, string Note)>? Work { get; set; }
    }
}
