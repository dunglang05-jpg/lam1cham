using System.Text.Json;
using System.Text.Json.Nodes;
using LamMotCham.Windows.Infrastructure;

namespace LamMotCham.Windows.Probe;

/// <summary>
/// Thu thập và lưu tạm thông tin máy. Các nhóm của <see cref="InventoryScript"/> chạy song song rồi gộp lại;
/// các task kiểm tra trong cùng một lượt (và trong <c>maxAge</c>) dùng chung một lần thu thập.
/// </summary>
public sealed class InventoryProvider : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeSpan _maxAge;
    private readonly TimeSpan _timeout;
    private InventorySnapshot? _cached;

    public InventoryProvider(TimeSpan? maxAge = null, TimeSpan? timeout = null)
    {
        _maxAge = maxAge ?? TimeSpan.FromMinutes(3);
        _timeout = timeout ?? TimeSpan.FromMinutes(3);
    }

    /// <summary>Số nhóm thu thập (để hiển thị tiến độ).</summary>
    public static int GroupCount => InventoryScript.Groups.Count;

    /// <summary>Bản đầy đủ gần nhất còn hạn, hoặc null.</summary>
    public InventorySnapshot? Current =>
        Volatile.Read(ref _cached) is { } c && DateTimeOffset.UtcNow - c.CollectedAt < _maxAge ? c : null;

    /// <summary>Buộc lần đọc kế tiếp thu thập lại (gọi sau khi thay đổi máy).</summary>
    public void Invalidate() => Volatile.Write(ref _cached, null);

    public Task<InventorySnapshot> GetAsync(CancellationToken cancellationToken) => GetAsync(null, cancellationToken);

    /// <param name="progress">Báo số nhóm đã xong (1..<see cref="GroupCount"/>).</param>
    /// <param name="cancellationToken">Hủy thu thập.</param>
    public async Task<InventorySnapshot> GetAsync(IProgress<int>? progress, CancellationToken cancellationToken)
    {
        if (Current is { } fresh)
        {
            return fresh;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Current is { } again)
            {
                return again;
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_timeout);
            var done = 0;
            var parts = await Task.WhenAll(InventoryScript.Groups.Select(async g =>
            {
                var part = await RunGroupAsync(g.Name, g.Script, cts.Token).ConfigureAwait(false);
                progress?.Report(Interlocked.Increment(ref done));
                return part;
            })).ConfigureAwait(false);

            var snapshot = QuickProbe.ToSnapshot(Merge(parts));
            Volatile.Write(ref _cached, snapshot);
            return snapshot;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Gộp kết quả các nhóm: trường của nhóm sau không ghi đè trường đã có; nối danh sách lỗi.</summary>
    public static JsonObject Merge(IEnumerable<JsonObject> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        var merged = new JsonObject { ["source"] = "full" };
        var errors = new JsonArray();
        foreach (var part in parts)
        {
            foreach (var (key, value) in part.ToList())
            {
                if (key == "errors")
                {
                    if (value is JsonArray arr)
                    {
                        foreach (var e in arr)
                        {
                            errors.Add(e?.DeepClone());
                        }
                    }

                    continue;
                }

                if (!merged.ContainsKey(key))
                {
                    merged[key] = value?.DeepClone();
                }
            }
        }

        merged["errors"] = errors;
        return merged;
    }

    private static async Task<JsonObject> RunGroupAsync(string name, string script, CancellationToken cancellationToken)
    {
        try
        {
            using var doc = await PowerShellRunner.RunJsonAsync(script, cancellationToken).ConfigureAwait(false);
            return JsonNode.Parse(doc.RootElement.GetRawText()) as JsonObject ?? new JsonObject();
        }
        catch (Exception ex) when (ex is InvalidOperationException or JsonException or IOException or System.ComponentModel.Win32Exception or OperationCanceledException)
        {
            return new JsonObject { ["errors"] = new JsonArray($"{name}: {ex.Message}") };
        }
    }

    public void Dispose() => _gate.Dispose();
}
