using System.Globalization;
using System.Text.Json;

namespace LamMotCham.Windows.Probe;

/// <summary>Kết quả thu thập thông tin máy (bản bất biến, đọc an toàn từ nhiều luồng).</summary>
public sealed class InventorySnapshot
{
    private readonly JsonElement _root;

    public InventorySnapshot(JsonElement root, DateTimeOffset collectedAt)
    {
        _root = root.Clone();
        CollectedAt = collectedAt;
        Errors = [.. Array("errors").Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : e.ToString())];
    }

    public DateTimeOffset CollectedAt { get; }

    /// <summary>true: bản đọc nhanh lúc mở ứng dụng (chưa có dữ liệu kiểm tra đầy đủ).</summary>
    public bool IsQuick => string.Equals(Str("source"), QuickProbe.SourceQuick, StringComparison.Ordinal);

    public IReadOnlyList<string> Errors { get; }

    public string? ErrorFor(string section) =>
        Errors.FirstOrDefault(e => e.StartsWith(section + ":", StringComparison.OrdinalIgnoreCase));

    public string? Str(string name) => Str(_root, name);

    public long? Num(string name) => Num(_root, name);

    public JsonElement? Obj(string name) =>
        _root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Object ? v : null;

    public IReadOnlyList<JsonElement> Array(string name) => Array(_root, name);

    public static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v)
            ? v.ValueKind switch
            {
                JsonValueKind.String => v.GetString(),
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                _ => v.ToString(),
            }
            : null;

    public static long? Num(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v))
        {
            return null;
        }

        return v.ValueKind switch
        {
            JsonValueKind.Number when v.TryGetInt64(out var n) => n,
            JsonValueKind.String when long.TryParse(v.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) => n,
            _ => null,
        };
    }

    public static bool? Bool(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v)
            ? v.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null,
            }
            : null;

    public static DateTimeOffset? Date(JsonElement e, string name) =>
        Str(e, name) is { } s && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d)
            ? d
            : null;

    public static IReadOnlyList<JsonElement> Array(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v))
        {
            return [];
        }

        return v.ValueKind switch
        {
            JsonValueKind.Array => [.. v.EnumerateArray()],
            JsonValueKind.Null or JsonValueKind.Undefined => [],
            _ => [v],
        };
    }
}
