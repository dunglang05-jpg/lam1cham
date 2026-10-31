using System.Diagnostics;
using System.Globalization;
using System.Text;
using LamMotCham.Core.Logging;
using LamMotCham.Core.Orchestration;
using LamMotCham.Core.Resources;
using LamMotCham.Core.Tasks;
using LamMotCham.Windows.Resources;
using LamMotCham.Windows.Software;

namespace LamMotCham.AppCheck;

/// <summary>
/// Kiểm thử đầu-cuối luồng "Cài phần mềm" giống hệt khi kỹ thuật viên bấm chạy (PO 01/11/2026: "kiểm thử ok xong hết rồi báo"):
/// cùng hàng đợi, cùng governor RAM, cùng bộ tải trước, cùng báo tiến độ. Chạy 2 lượt trên máy Windows sạch của GitHub:
/// lượt 1 phải cài xong và xác minh; lượt 2 phải báo "Đã có sẵn". Mỗi lượt kiểm tra luôn cả dòng tiến độ:
/// thanh không lùi, không quay lại "Đang chạy" sau khi xong, chữ ngắn gọn, không lộ lỗi kỹ thuật.
/// </summary>
internal static class E2E
{
    /// <summary>Đúng bộ PO đã thử trên LAPTOP-PERIUM, thêm UniKey (ZIP) để phủ thêm kiểu cài.</summary>
    private static readonly string[] Apps = ["Foxit PDF Reader", "Adobe Acrobat Reader", "K-Lite Codec Pack", "UniKey"];

    internal sealed record Update(TimeSpan At, TaskStatusUpdate Data);

    internal sealed class Capture(Stopwatch clock) : IProgress<TaskStatusUpdate>
    {
        private readonly Lock _gate = new();
        private readonly List<Update> _items = [];

        public void Report(TaskStatusUpdate value)
        {
            lock (_gate)
            {
                _items.Add(new Update(clock.Elapsed, value));
            }
        }

        public List<Update> Snapshot()
        {
            lock (_gate)
            {
                return [.. _items];
            }
        }
    }

    public static async Task<int> RunAsync(IReadOnlyList<SoftwarePackage> packages, string? summaryPath, CancellationToken ct)
    {
        var picked = Apps.Select(n => packages.FirstOrDefault(p => p.Name == n)).ToList();
        var problems = new List<string>();
        var md = new StringBuilder("## Kiểm thử đầu-cuối: Cài phần mềm\n\n");
        for (var i = 0; i < Apps.Length; i++)
        {
            if (picked[i] is null)
            {
                problems.Add($"Không thấy \"{Apps[i]}\" trong danh sách ứng dụng.");
            }
        }

        var list = picked.OfType<SoftwarePackage>().ToList();
        var winGet = new WinGetClient();
        foreach (var round in new[] { 1, 2 })
        {
            var tasks = list.Select(p => new AppInstallTask(p, winGet)).ToList();
            var routes = string.Join(", ", tasks.Select(t => $"{t.Package.Name}: {(t.PrefersLink ? "link" : "WinGet")}"));
            Console.WriteLine($"=== Lượt {round}: {routes}");
            var log = new InMemoryRunLogger();
            var orchestrator = new TaskOrchestrator(log, new ResourceGovernor(new WindowsResourceMonitor()));
            var clock = Stopwatch.StartNew();
            var capture = new Capture(clock);
            var prefetch = LinkPrefetcher.Begin(tasks, m => log.Info(m));
            RunReport report;
            try
            {
                report = await orchestrator.RunAsync(tasks, confirmedByUser: true, capture, ct);
            }
            finally
            {
                LinkPrefetcher.End(prefetch);
            }

            var total = clock.Elapsed;
            var updates = capture.Snapshot();
            md.Append(CultureInfo.InvariantCulture, $"### Lượt {round} — tổng {total.TotalMinutes:0.0} phút\n\n| Ứng dụng | Đường cài | Kết quả | Thời gian | Tải | Phải chờ tải | Cài | Các bước hiện trên màn hình |\n|---|---|---|---|---|---|---|---|\n");
            foreach (var task in tasks)
            {
                var outcome = report.Tasks.Single(o => o.TaskId == task.Descriptor.Id);
                var mine = updates.Where(u => u.Data.TaskId == task.Descriptor.Id).ToList();
                var steps = string.Join(" → ", mine.Where(u => u.Data.State == TaskState.Running && !string.IsNullOrWhiteSpace(u.Data.Message))
                    .Select(u => Step(u.Data.Message!)).Distinct());
                var m = task.LastMetrics;
                md.Append(CultureInfo.InvariantCulture,
                    $"| {task.Package.Name} | {task.Route} | {outcome.State}{(task.WasAlreadyInstalled ? " (đã có sẵn)" : "")} | {outcome.Duration?.TotalSeconds:0} giây | {m?.Download.TotalSeconds:0} | {m?.Wait.TotalSeconds:0} | {m?.Install.TotalSeconds:0} | {steps} |\n");
                Console.WriteLine($"[{task.Package.Name}] {outcome.State} · {task.Route} · {outcome.Duration?.TotalSeconds:0}s · {outcome.Reason}");
                Console.WriteLine("   bước: " + steps);
                Console.WriteLine($"::notice title=Lượt {round} · {task.Package.Name}::{outcome.State} qua {task.Route} trong {outcome.Duration?.TotalSeconds:0} giây. Bước: {steps}");
                var warnings = log.Snapshot().Where(e => e.TaskId == task.Descriptor.Id && e.Level >= LogLevel.Warning).Select(e => e.Message).ToList();
                if (warnings.Count > 0)
                {
                    // Ví dụ link lỗi rồi chuyển sang WinGet: vẫn cài được nhưng chậm hơn — báo để sửa link trên Admin.
                    Console.WriteLine($"::warning title=Lượt {round} · {task.Package.Name}::{string.Join(" | ", warnings).Replace('\n', ' ')}");
                }


                problems.AddRange(CheckOutcome(round, task, outcome));
                problems.AddRange(CheckProgress(round, task.Package.Name, mine));
            }

            var leftovers = Directory.GetDirectories(Path.GetTempPath(), "LamMotCham-tai-*");
            if (leftovers.Length > 0)
            {
                problems.Add($"Lượt {round}: còn {leftovers.Length} thư mục tải tạm chưa xóa.");
            }

            md.Append('\n');
            if (problems.Count > 0)
            {
                Console.WriteLine("--- Log lượt " + round);
                foreach (var e in log.Snapshot())
                {
                    Console.WriteLine($"{e.Level} {e.TaskId} {e.Message}");
                }
            }
        }

        md.Append(problems.Count == 0 ? "**Kết quả: Đạt.**\n" : "**Kết quả: Lỗi**\n\n" + string.Join("\n", problems.Select(p => "- " + p)) + "\n");
        foreach (var p in problems)
        {
            Console.WriteLine("::error title=Kiểm thử đầu-cuối::" + p);
        }

        if (summaryPath is not null)
        {
            await File.AppendAllTextAsync(summaryPath, md.ToString(), Encoding.UTF8, CancellationToken.None);
        }

        Console.WriteLine(md.ToString());
        return problems.Count == 0 ? 0 : 1;
    }

    internal static string Step(string message)
    {
        // Gom "Đang tải 3%", "Đang tải 57%"… thành một bước khi in tóm tắt.
        return message.StartsWith("Đang tải ", StringComparison.Ordinal) && message.EndsWith('%') ? "Đang tải N%" : message;
    }

    private static IEnumerable<string> CheckOutcome(int round, AppInstallTask task, TaskOutcome outcome)
    {
        var name = task.Package.Name;
        if (outcome.State != TaskState.Success)
        {
            yield return $"Lượt {round} · {name}: kết quả {outcome.State} — {outcome.Reason}";
        }

        if (round == 2 && !task.WasAlreadyInstalled)
        {
            yield return $"Lượt {round} · {name}: phải báo \"Đã có sẵn\" nhưng lại cài lại.";
        }

        if (round == 2 && outcome.Duration is { } d && d > TimeSpan.FromSeconds(90))
        {
            yield return $"Lượt {round} · {name}: đã có sẵn mà mất {d.TotalSeconds:0} giây.";
        }
    }

    internal static IEnumerable<string> CheckProgress(int round, string name, List<Update> mine)
    {
        var where = $"Lượt {round} · {name}";
        var terminalAt = mine.FindIndex(u => u.Data.State.IsTerminal());
        if (terminalAt < 0)
        {
            yield return $"{where}: không có trạng thái cuối.";
            yield break;
        }

        if (mine.Skip(terminalAt + 1).Any(u => u.Data.State == TaskState.Running))
        {
            yield return $"{where}: dòng tác vụ quay lại \"Đang chạy\" sau khi đã xong.";
        }

        var running = mine.Take(terminalAt).Where(u => u.Data.State == TaskState.Running).ToList();
        if (running.Count == 0 || running.All(u => u.Data.Fraction is null))
        {
            yield return $"{where}: không báo tiến độ nào (thanh sẽ đứng yên).";
        }

        // Thanh không được lùi trong cùng một lần thử.
        var last = -1.0;
        var attempt = 0;
        foreach (var u in running)
        {
            if (u.Data.Attempt != attempt)
            {
                attempt = u.Data.Attempt;
                last = -1;
            }

            if (u.Data.Fraction is { } f)
            {
                if (f + 1e-9 < last)
                {
                    yield return $"{where}: thanh lùi từ {last:P0} xuống {f:P0} ở bước \"{u.Data.Message}\".";
                    break;
                }

                last = f;
            }
        }

        // Thanh không đứng yên quá 20 giây khi đang chạy (người dùng tưởng treo).
        for (var i = 1; i < running.Count; i++)
        {
            var gap = running[i].At - running[i - 1].At;
            if (gap > TimeSpan.FromSeconds(20))
            {
                yield return $"{where}: {gap.TotalSeconds:0} giây không cập nhật tiến độ (sau \"{running[i - 1].Data.Message}\").";
                break;
            }
        }

        foreach (var text in running.Select(u => u.Data.Message).OfType<string>().Distinct())
        {
            if (text.Contains("Exception", StringComparison.Ordinal) || text.Length > 40 || text.Contains(" · ", StringComparison.Ordinal))
            {
                yield return $"{where}: chữ tiến độ không gọn hoặc lộ lỗi kỹ thuật: \"{text}\".";
            }
        }
    }
}
