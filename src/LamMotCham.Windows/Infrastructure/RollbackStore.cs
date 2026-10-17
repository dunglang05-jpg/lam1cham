using System.Text.Json;

namespace LamMotCham.Windows.Infrastructure;

/// <summary>
/// Lưu giá trị cũ trước khi thay đổi để có thể hoàn tác, kể cả sau khi đóng ứng dụng.
/// Chỉ lưu giá trị cấu hình, không lưu bí mật.
/// </summary>
public sealed class RollbackStore
{
    private readonly string _directory;
    private readonly Lock _gate = new();

    public RollbackStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = directory;
    }

    public void Save(string taskId, IReadOnlyDictionary<string, string?> previousValues)
    {
        ArgumentNullException.ThrowIfNull(previousValues);
        lock (_gate)
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllText(PathFor(taskId), JsonSerializer.Serialize(previousValues));
        }
    }

    /// <summary>Không ghi đè bản đã lưu: giữ giá trị gốc từ lần thay đổi đầu tiên.</summary>
    public void SaveIfAbsent(string taskId, IReadOnlyDictionary<string, string?> previousValues)
    {
        lock (_gate)
        {
            if (!File.Exists(PathFor(taskId)))
            {
                Save(taskId, previousValues);
            }
        }
    }

    public IReadOnlyDictionary<string, string?>? Load(string taskId)
    {
        lock (_gate)
        {
            var path = PathFor(taskId);
            return File.Exists(path)
                ? JsonSerializer.Deserialize<Dictionary<string, string?>>(File.ReadAllText(path))
                : null;
        }
    }

    public void Clear(string taskId)
    {
        lock (_gate)
        {
            var path = PathFor(taskId);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private string PathFor(string taskId)
    {
        var safe = string.Concat(taskId.Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_'));
        return Path.Combine(_directory, safe + ".json");
    }
}
