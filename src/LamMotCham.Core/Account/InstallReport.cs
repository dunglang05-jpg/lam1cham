using System.Text.Json;
using LamMotCham.Core.Tasks;

namespace LamMotCham.Core.Account;

/// <summary>Một ứng dụng trong báo cáo cài gửi lên mục "Lịch sử cài" của Web Admin (QĐ-006).</summary>
public sealed record InstallReportItem
{
    public required string Name { get; init; }

    public string Version { get; init; } = "";

    /// <summary>Nguồn cài: mã WinGet hoặc tên miền của link tải. Không gửi đường dẫn file trên máy.</summary>
    public string Source { get; init; } = "";

    public long Bytes { get; init; }

    public double DownloadSeconds { get; init; }

    public double WaitSeconds { get; init; }

    public double InstallSeconds { get; init; }

    /// <summary>Chữ kết quả như mục Lịch sử cài đang dùng: "Hoàn tất", "Lỗi", "Đã có sẵn"...</summary>
    public required string Result { get; init; }

    public int Code { get; init; }

    /// <summary>Lý do, đã qua bộ ẩn dữ liệu nhạy cảm.</summary>
    public string Reason { get; init; } = "";

    public int Attempt { get; init; } = 1;
}

/// <summary>
/// Báo cáo một lượt cài phần mềm (action=installReport). Chỉ gồm tên máy, Windows, kết quả từng ứng dụng;
/// cắt độ dài theo giới hạn của máy chủ (api.php: tối đa 60 mục).
/// </summary>
public sealed record InstallReportPayload(string ComputerName, string OsName, double TotalSeconds, IReadOnlyList<InstallReportItem> Items)
{
    public const int MaxItems = 60;

    public const string Done = "Hoàn tất";
    public const string Error = "Lỗi";

    /// <summary>Chữ kết quả cho một trạng thái task. Máy chủ đếm "Hoàn tất" và "Lỗi".</summary>
    public static string ResultFor(TaskState state, bool alreadyInstalled) => state switch
    {
        TaskState.Success => alreadyInstalled ? "Đã có sẵn" : Done,
        TaskState.Failed => Error,
        TaskState.NeedsAttention => "Cần kiểm tra",
        TaskState.WaitingForReboot => "Chờ khởi động lại",
        TaskState.Cancelled => "Đã hủy",
        TaskState.Skipped => "Bỏ qua",
        TaskState.NotSupported => "Không hỗ trợ",
        TaskState.NotTested => "Cần kiểm tra",
        _ => "Chưa chạy",
    };

    internal void WriteTo(Utf8JsonWriter w)
    {
        w.WriteString("pc", Cut(ComputerName, 60));
        w.WriteString("os", Cut(OsName, 80));
        w.WriteNumber("total", Math.Round(Math.Max(0, TotalSeconds), 1));
        w.WriteStartArray("items");
        foreach (var item in Items.Take(MaxItems))
        {
            w.WriteStartObject();
            w.WriteString("Name", Cut(item.Name, 40));
            w.WriteString("Version", Cut(item.Version, 60));
            w.WriteString("Source", Cut(item.Source, 120));
            w.WriteNumber("Bytes", Math.Max(0, item.Bytes));
            w.WriteNumber("DownloadSec", Math.Round(Math.Max(0, item.DownloadSeconds), 1));
            w.WriteNumber("WaitSec", Math.Round(Math.Max(0, item.WaitSeconds), 1));
            w.WriteNumber("InstallSec", Math.Round(Math.Max(0, item.InstallSeconds), 1));
            w.WriteString("Result", Cut(item.Result, 30));
            w.WriteNumber("Code", item.Code);
            w.WriteString("Reason", Cut(item.Reason, 300));
            w.WriteNumber("Attempt", Math.Max(1, item.Attempt));
            w.WriteEndObject();
        }

        w.WriteEndArray();
    }

    private static string Cut(string? value, int max)
    {
        var v = (value ?? "").Trim();
        return v.Length <= max ? v : v[..max];
    }
}
