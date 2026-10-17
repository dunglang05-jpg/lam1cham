using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using LamMotCham.Core.Logging;
using LamMotCham.Core.Orchestration;
using LamMotCham.Core.Tasks;

namespace LamMotCham.Core.Reporting;

public sealed record ReportFiles(string JsonPath, string HtmlPath);

/// <summary>Ghi báo cáo cục bộ (JSON cho máy đọc, HTML cho người đọc). Không upload.</summary>
public static class ReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    public static ReportFiles Write(
        string directory,
        RunReport report,
        IReadOnlyList<KeyValuePair<string, string>> machineInfo,
        string appVersion,
        TimeZoneInfo? displayTimeZone = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(machineInfo);

        Directory.CreateDirectory(directory);
        var tz = displayTimeZone ?? TimeZoneInfo.Local;
        var stamp = TimeZoneInfo.ConvertTime(report.StartedAt, tz).ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var baseName = $"BaoCao-{stamp}";

        var machine = machineInfo.Select(kv => new KeyValuePair<string, string>(kv.Key, Redactor.Redact(kv.Value))).ToList();

        var jsonPath = Path.Combine(directory, baseName + ".json");
        var payload = new
        {
            schemaVersion = 1,
            appVersion,
            startedAt = report.StartedAt,
            finishedAt = report.FinishedAt,
            haltReason = report.HaltReason,
            machine = machine.ToDictionary(kv => kv.Key, kv => kv.Value),
            summary = Enum.GetValues<TaskState>()
                .Where(s => report.Count(s) > 0)
                .ToDictionary(s => s.ToString(), report.Count),
            tasks = report.Tasks.Select(t => new
            {
                id = t.TaskId,
                name = t.DisplayName,
                kind = t.Kind,
                risk = t.Risk,
                state = t.State,
                reason = Redactor.Redact(t.Reason),
                attempts = t.Attempts,
                startedAt = t.StartedAt,
                finishedAt = t.FinishedAt,
            }),
        };
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(payload, JsonOptions), new UTF8Encoding(false));

        var htmlPath = Path.Combine(directory, baseName + ".html");
        File.WriteAllText(htmlPath, BuildHtml(report, machine, appVersion, tz), new UTF8Encoding(false));

        return new ReportFiles(jsonPath, htmlPath);
    }

    public static string StateDisplayName(TaskState state) => state switch
    {
        TaskState.Pending => "Đang chờ",
        TaskState.Running => "Đang chạy",
        TaskState.Success => "Thành công",
        TaskState.Failed => "Thất bại",
        TaskState.Skipped => "Bỏ qua",
        TaskState.NeedsAttention => "Cần chú ý",
        TaskState.Cancelled => "Đã hủy",
        TaskState.WaitingForReboot => "Chờ khởi động lại",
        TaskState.NotTested => "Cần kiểm tra thủ công",
        TaskState.NotSupported => "Không hỗ trợ",
        _ => state.ToString(),
    };

    private static string BuildHtml(
        RunReport report,
        IReadOnlyList<KeyValuePair<string, string>> machine,
        string appVersion,
        TimeZoneInfo tz)
    {
        static string E(string? s) => WebUtility.HtmlEncode(s ?? string.Empty);
        string T(DateTimeOffset? d) => d is null ? "" : TimeZoneInfo.ConvertTime(d.Value, tz).ToString("dd/MM/yyyy HH:mm:ss", Vi);

        var sb = new StringBuilder();
        sb.Append("""
            <!doctype html>
            <html lang="vi"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
            <title>Báo cáo Làm 1 Chạm</title>
            <style>
            body{font-family:"Segoe UI",Arial,sans-serif;margin:24px;color:#1b2430;background:#f6f8fb}
            h1{color:#1d5fd6;margin:0 0 4px}h2{margin-top:28px;color:#1d3d73}
            .meta{color:#5a6577}table{border-collapse:collapse;width:100%;background:#fff}
            th,td{border:1px solid #dde3ec;padding:8px 10px;text-align:left;vertical-align:top}
            th{background:#eaf1fd}.s{font-weight:600;white-space:nowrap}
            .Success{color:#15803d}.Failed{color:#b91c1c}.NeedsAttention,.WaitingForReboot{color:#b45309}
            .Skipped,.Cancelled,.NotSupported,.NotTested,.Pending{color:#5a6577}
            .sum span{display:inline-block;margin:4px 12px 4px 0;padding:4px 10px;border-radius:12px;background:#fff;border:1px solid #dde3ec}
            </style></head><body>
            """);
        sb.Append("<h1>Báo cáo thiết lập máy</h1>");
        sb.Append(CultureInfo.InvariantCulture,
            $"<div class=\"meta\">Làm 1 Chạm {E(appVersion)} · Bắt đầu {E(T(report.StartedAt))} · Kết thúc {E(T(report.FinishedAt))}</div>");

        if (report.HaltReason != RunHaltReason.None)
        {
            var halt = report.HaltReason switch
            {
                RunHaltReason.RebootRequired => "Lượt chạy dừng lại để chờ khởi động lại máy. Hãy khởi động lại rồi chạy lại các task còn lại.",
                RunHaltReason.UnresponsiveTask => "Lượt chạy dừng vì một task không phản hồi. Cần kiểm tra máy trước khi chạy tiếp.",
                RunHaltReason.CancelledByUser => "Người dùng đã hủy lượt chạy.",
                _ => report.HaltReason.ToString(),
            };
            sb.Append(CultureInfo.InvariantCulture, $"<p class=\"NeedsAttention\"><b>{E(halt)}</b></p>");
        }

        sb.Append("<div class=\"sum\">");
        foreach (var state in Enum.GetValues<TaskState>().Where(s => report.Count(s) > 0))
        {
            sb.Append(CultureInfo.InvariantCulture,
                $"<span class=\"{state}\">{E(StateDisplayName(state))}: {report.Count(state)}</span>");
        }

        sb.Append("</div><h2>Thông tin máy</h2><table>");
        foreach (var (key, value) in machine)
        {
            sb.Append(CultureInfo.InvariantCulture, $"<tr><th>{E(key)}</th><td>{E(value)}</td></tr>");
        }

        sb.Append("</table><h2>Chi tiết tác vụ</h2><table><tr><th>Tác vụ</th><th>Trạng thái</th><th>Chi tiết</th><th>Số lần</th><th>Thời gian</th></tr>");
        foreach (var t in report.Tasks)
        {
            var duration = t.Duration is { } d ? $"{d.TotalSeconds:0.0} giây" : "";
            sb.Append(CultureInfo.InvariantCulture,
                $"<tr><td>{E(t.DisplayName)}</td><td class=\"s {t.State}\">{E(StateDisplayName(t.State))}</td>" +
                $"<td>{E(Redactor.Redact(t.Reason))}</td><td>{t.Attempts}</td><td>{E(duration)}</td></tr>");
        }

        sb.Append("</table><p class=\"meta\">Báo cáo được lưu cục bộ trên máy này và không được gửi đi đâu.</p></body></html>");
        return sb.ToString();
    }
}
