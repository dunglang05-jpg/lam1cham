using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using LamMotCham.Core.Logging;
using LamMotCham.Core.Orchestration;
using LamMotCham.Core.Resources;
using LamMotCham.Core.Tasks;
using LamMotCham.Windows;
using LamMotCham.Windows.Infrastructure;
using LamMotCham.Windows.Probe;
using LamMotCham.Windows.Resources;
using LamMotCham.Windows.Software;

namespace LamMotCham.AppCheck;

/// <summary>
/// Kiểm thử đầu-cuối MỌI tính năng trên máy Windows sạch của GitHub (PO 01/11/2026: "kiểm thử tất cả các tính năng"):
/// dựng đúng danh mục tác vụ như ứng dụng (WindowsCatalog + danh sách app của Admin), chạy qua đúng hàng đợi/governor/tải trước:
/// 1) "Thiết lập đầy đủ" lượt 1 — phần mềm + tối ưu Windows + dọn dẹp + kiểm tra bàn giao;
/// 2) "Thiết lập đầy đủ" lượt 2 — phải đã đúng sẵn / đã có sẵn, không cài lại;
/// 3) các mục tùy chọn (không chọn sẵn) của Cài đặt &amp; Tối ưu và Dọn dẹp;
/// 4) hoàn tác các thay đổi Windows hỗ trợ hoàn tác;
/// 5) Ổ đĩa &amp; Tài khoản: đổi họ tên, mật khẩu, tên đăng nhập của tài khoản thử; đổi tên máy (chờ khởi động lại).
/// Mỗi lượt kiểm tra cả dòng tiến độ như <see cref="E2E"/>.
/// </summary>
internal static class E2EAll
{
    /// <summary>Tài khoản thử do workflow tạo trước (net user). Không phải tài khoản thật của ai.</summary>
    private const string TestUser = "l1ctest";

    private static readonly string[] CrashMarkers = ["dừng bất thường", "Exception", "Lỗi không mong đợi", "Lỗi khi xác minh"];

    public static async Task<int> RunAsync(string? summaryPath, CancellationToken ct)
    {
        using var inventory = new InventoryProvider();
        var services = new CatalogServices(
            inventory,
            new WinGetClient(),
            new RollbackStore(Path.Combine(Path.GetTempPath(), "LamMotCham-e2e-hoan-tac")));
        var problems = new List<string>();
        var notes = new List<string>();
        var md = new StringBuilder("## Kiểm thử đầu-cuối: tất cả tính năng\n\n");

        // 1–2. Thiết lập đầy đủ, hai lượt.
        TaskReportSet? first = null;
        foreach (var round in new[] { 1, 2 })
        {
            var entries = WindowsCatalog.Build(services).Where(e => e.IsInPack(TaskPack.FullSetup)).ToList();
            var set = await RunBatchAsync($"Thiết lập đầy đủ — lượt {round}", entries, md, problems, ct);
            first ??= set;
            foreach (var (entry, outcome) in set.Results)
            {
                if (round == 2 && entry.Module is AppInstallTask app && outcome.State == TaskState.Success && !app.WasAlreadyInstalled)
                {
                    problems.Add($"Thiết lập đầy đủ lượt 2 · {entry.Module.Descriptor.DisplayName}: đã có sẵn mà vẫn cài lại.");
                }
            }
        }

        // 3. Mục tùy chọn của Cài đặt & Tối ưu và Dọn dẹp (không nằm trong gói nhanh).
        var optional = WindowsCatalog.Build(services)
            .Where(e => !e.SelectedByDefault && e.Category is TaskCategory.WindowsSettings or TaskCategory.Cleanup)
            .ToList();
        if (optional.Count > 0)
        {
            await RunBatchAsync("Mục tùy chọn (Cài đặt & Tối ưu, Dọn dẹp)", optional, md, problems, ct);
        }

        // 4. Hoàn tác những thay đổi Windows đã áp dụng ở lượt 1.
        md.Append("### Hoàn tác thay đổi Windows\n\n| Tác vụ | Kết quả |\n|---|---|\n");
        var undoLines = new List<string>();
        foreach (var (entry, outcome) in first!.Results.Where(r =>
                     r.Entry.Category == TaskCategory.WindowsSettings &&
                     r.Entry.Module.Descriptor.Rollback == RollbackSupport.Supported &&
                     r.Outcome.State == TaskState.Success))
        {
            VerifyResult undo;
            try
            {
                undo = await entry.Module.RollbackAsync(new TaskContext(new InMemoryRunLogger(), 1), ct);
            }
#pragma warning disable CA1031 // Ghi nhận mọi lỗi thành kết quả kiểm thử.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                undo = VerifyResult.Failed($"{ex.GetType().Name}: {ex.Message}");
            }

            var name = entry.Module.Descriptor.DisplayName;
            md.Append(CultureInfo.InvariantCulture, $"| {name} | {undo.Outcome} — {undo.Detail} |\n");
            undoLines.Add($"{name}: {undo.Outcome}");
            if (undo.Outcome is not (VerifyOutcome.Passed or VerifyOutcome.NotSupported))
            {
                problems.Add($"Hoàn tác · {name}: {undo.Outcome} — {undo.Detail}");
            }
        }

        Console.WriteLine("::notice title=Hoàn tác thay đổi Windows::" + (undoLines.Count == 0 ? "Không có thay đổi để hoàn tác." : string.Join("; ", undoLines)));
        md.Append('\n');

        // 5. Ổ đĩa & Tài khoản.
        var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(18)) + "aA1!";
        await RunBatchAsync(
            "Tài khoản người dùng (tài khoản thử)",
            [
                WindowsCatalog.UserFullName(TestUser, "Khách Hàng Thử"),
                WindowsCatalog.UserPassword(TestUser, password),
                WindowsCatalog.UserRename(TestUser, TestUser + "2"),
            ],
            md,
            problems,
            ct);
        await RunBatchAsync("Đổi tên máy", [WindowsCatalog.Rename("L1C-KIEMTHU")], md, problems, ct, rebootExpected: true);

        notes.Add("Chia ổ đĩa không chạy trên máy CI (ổ hệ thống của máy ảo không thu nhỏ an toàn được); đã có kiểm thử đơn vị cho kế hoạch chia ổ.");
        md.Append("### Ghi chú\n\n").Append(string.Join("\n", notes.Select(n => "- " + n))).Append("\n\n");
        md.Append(problems.Count == 0 ? "**Kết quả: Đạt.**\n" : "**Kết quả: Lỗi**\n\n" + string.Join("\n", problems.Select(p => "- " + p)) + "\n");
        if (problems.Count > 0)
        {
            Console.WriteLine("::error title=Kiểm thử tất cả tính năng (" + problems.Count + " lỗi)::" + string.Join(" ‖ ", problems));
        }

        if (summaryPath is not null)
        {
            await File.AppendAllTextAsync(summaryPath, md.ToString(), Encoding.UTF8, CancellationToken.None);
        }

        Console.WriteLine(md.ToString());
        return problems.Count == 0 ? 0 : 1;
    }

    private sealed record TaskReportSet(List<(TaskCatalogEntry Entry, TaskOutcome Outcome)> Results);

    private static async Task<TaskReportSet> RunBatchAsync(
        string title,
        List<TaskCatalogEntry> entries,
        StringBuilder md,
        List<string> problems,
        CancellationToken ct,
        bool rebootExpected = false)
    {
        Console.WriteLine($"=== {title}: {entries.Count} tác vụ");
        var log = new InMemoryRunLogger();
        var orchestrator = new TaskOrchestrator(log, new ResourceGovernor(new WindowsResourceMonitor()));
        var clock = Stopwatch.StartNew();
        var capture = new E2E.Capture(clock);
        var modules = entries.Select(e => e.Module).ToList();
        var prefetch = LinkPrefetcher.Begin(modules.OfType<AppInstallTask>(), m => log.Info(m));
        RunReport report;
        try
        {
            report = await orchestrator.RunAsync(modules, confirmedByUser: true, capture, ct);
        }
        finally
        {
            LinkPrefetcher.End(prefetch);
        }

        var updates = capture.Snapshot();
        var results = new List<(TaskCatalogEntry, TaskOutcome)>();
        var lines = new List<string>();
        var before = problems.Count;
        md.Append(CultureInfo.InvariantCulture, $"### {title} — {entries.Count} tác vụ, {clock.Elapsed.TotalMinutes:0.0} phút\n\n| Nhóm | Tác vụ | Kết quả | Thời gian | Lý do |\n|---|---|---|---|---|\n");
        foreach (var entry in entries)
        {
            var d = entry.Module.Descriptor;
            var outcome = report.Tasks.Single(o => o.TaskId == d.Id);
            results.Add((entry, outcome));
            var reason = (outcome.Reason ?? "").Replace('|', '/').Replace('\n', ' ');
            md.Append(CultureInfo.InvariantCulture, $"| {entry.Category} | {d.DisplayName} | {outcome.State} | {outcome.Duration?.TotalSeconds:0} giây | {reason} |\n");
            lines.Add($"{d.DisplayName}={outcome.State}");
            Console.WriteLine($"[{outcome.State}] {entry.Category} · {d.DisplayName} · {outcome.Duration?.TotalSeconds:0}s · {reason}");

            if (!Acceptable(entry, outcome, rebootExpected))
            {
                problems.Add($"{title} · {d.DisplayName}: {outcome.State} — {reason}");
            }

            var mine = updates.Where(u => u.Data.TaskId == d.Id).ToList();
            if (outcome.State is not (TaskState.Pending or TaskState.Cancelled) && mine.Any(u => u.Data.State == TaskState.Running))
            {
                problems.AddRange(E2E.CheckProgress(0, d.DisplayName, mine).Select(p => title + " · " + p));
            }
        }

        md.Append('\n');
        Console.WriteLine($"::notice title={title} ({clock.Elapsed.TotalMinutes:0.0} phút)::{string.Join("; ", lines)}");
        if (problems.Count > before)
        {
            Console.WriteLine("--- Log: " + title);
            foreach (var e in log.Snapshot().Where(e => e.Level >= LogLevel.Warning))
            {
                Console.WriteLine($"{e.Level} {e.TaskId} {e.Message}");
            }
        }

        return new TaskReportSet(results);
    }

    /// <summary>
    /// Trạng thái chấp nhận được trên máy CI (Windows Server, không pin, không webcam…):
    /// tác vụ thay đổi máy phải Thành công, hoặc Không hỗ trợ/Bỏ qua kèm lý do rõ; tác vụ kiểm tra chỉ cần không lỗi bất thường.
    /// </summary>
    private static bool Acceptable(TaskCatalogEntry entry, TaskOutcome outcome, bool rebootExpected)
    {
        var reason = outcome.Reason ?? "";
        if (CrashMarkers.Any(m => reason.Contains(m, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (entry.IsReadOnly)
        {
            return outcome.State.IsTerminal();
        }

        return outcome.State switch
        {
            TaskState.Success => true,
            TaskState.WaitingForReboot => rebootExpected,
            TaskState.NotSupported or TaskState.Skipped => reason.Length > 0,
            _ => false,
        };
    }
}
