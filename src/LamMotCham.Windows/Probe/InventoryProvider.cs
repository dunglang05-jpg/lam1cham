using LamMotCham.Windows.Infrastructure;

namespace LamMotCham.Windows.Probe;

/// <summary>
/// Thu thập và lưu tạm thông tin máy. Các task kiểm tra trong cùng một lượt chạy dùng chung một lần thu thập.
/// </summary>
public sealed class InventoryProvider : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeSpan _maxAge;
    private readonly TimeSpan _timeout;
    private InventorySnapshot? _cached;

    public InventoryProvider(TimeSpan? maxAge = null, TimeSpan? timeout = null)
    {
        _maxAge = maxAge ?? TimeSpan.FromMinutes(2);
        _timeout = timeout ?? TimeSpan.FromMinutes(3);
    }

    /// <summary>Buộc lần đọc kế tiếp thu thập lại (gọi trước mỗi lượt chạy và sau khi thay đổi máy).</summary>
    public void Invalidate() => Volatile.Write(ref _cached, null);

    public async Task<InventorySnapshot> GetAsync(CancellationToken cancellationToken)
    {
        var current = Volatile.Read(ref _cached);
        if (current is not null && DateTimeOffset.UtcNow - current.CollectedAt < _maxAge)
        {
            return current;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            current = Volatile.Read(ref _cached);
            if (current is not null && DateTimeOffset.UtcNow - current.CollectedAt < _maxAge)
            {
                return current;
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_timeout);
            using var doc = await PowerShellRunner.RunJsonAsync(InventoryScript.Text, cts.Token).ConfigureAwait(false);
            var snapshot = new InventorySnapshot(doc.RootElement, DateTimeOffset.UtcNow);
            Volatile.Write(ref _cached, snapshot);
            return snapshot;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();
}
