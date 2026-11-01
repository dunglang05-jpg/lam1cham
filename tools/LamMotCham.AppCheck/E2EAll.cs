using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using LamMotCham.Core.Logging;
using LamMotCham.Core.Orchestration;
using LamMotCham.Core.Resources;
using LamMotCham.Core.Tasks;
using LamMotCham.Windows;
using LamMotCham.Windows.Config;
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

    /// <summary>Ứng dụng nặng (bộ Office, phần mềm diệt virus): chạy ở job riêng (--e2e-nang) để lỗi không kéo theo phần khác.</summary>
    internal static readonly string[] Heavy = ["Microsoft 365", "Microsoft Office", "Kaspersky", "ESET"];

    private static readonly string[] OfficeNames = ["Word", "Excel", "PowerPoint"];

    /// <summary>Phát hiện về dữ liệu trên Admin (link không ký số, tham số cài sai…): báo PO, không làm đỏ kiểm thử phần mềm.</summary>
    private static readonly List<string> Findings = [];

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

        // 3b. Dừng giữa chừng ("Dừng sau bước này"): bước đang chạy phải xong, các bước sau "Đã hủy", không cài thêm gì.
        await StopMidwayAsync(services, md, problems, ct);

        // 3c. Link hỏng (404) → tự chuyển WinGet trong cùng lượt.
        var hasWinGet = WinGetClient.ParseVersion(await services.WinGet.GetVersionAsync(ct)) is not null;
        Console.WriteLine("::notice title=WinGet trên máy kiểm thử::" + (hasWinGet ? "có" : "KHÔNG có — kiểm thử luôn tình huống máy thiếu WinGet"));
        if (hasWinGet && SoftwareCatalog.Current.FirstOrDefault(p => p.Name == "VLC media player" && p.UsesWinGet) is { } vlc)
        {
            var broken = vlc with { DirectUrl = new Uri("https://dunglam.top/khong-ton-tai-l1c-kiem-thu.exe"), PageUrl = null, PageRegex = null, SilentArgs = "/S" };
            var set = await RunBatchAsync("Link hỏng → tự chuyển WinGet (VLC)", [SoftwareEntry(new AppInstallTask(broken, services.WinGet))], md, problems, ct);
            if (set.Results[0].Entry.Module is AppInstallTask t && t.Route != InstallRoute.WinGet)
            {
                problems.Add($"Link hỏng → WinGet: VLC kết thúc bằng đường {t.Route}, không chuyển sang WinGet.");
            }
        }

        // 3d. Máy không có WinGet: ứng dụng có link vẫn cài được bằng link.
        if (SoftwareCatalog.Current.FirstOrDefault(p => p.Name == "UltraViewer" && p.HasLink) is { } ultra)
        {
            await RunBatchAsync("Máy không có WinGet (UltraViewer bằng link)", [SoftwareEntry(new AppInstallTask(ultra, new WinGetClient(() => null)))], md, problems, ct);
        }

        // 3d'. Thử tham số cài im lặng đề xuất sửa trên Admin: Viber là bộ cài WiX Burn, "/S" làm trình cài tự hủy (1602).
        if (SoftwareCatalog.Current.FirstOrDefault(p => p.Name == "Viber" && p.HasLink) is { } viber)
        {
            var fixedArgs = viber with { SilentArgs = "/quiet /norestart" };
            var set = await RunBatchAsync("Đề xuất Admin: Viber với /quiet /norestart (chỉ link)", [SoftwareEntry(new AppInstallTask(fixedArgs, new WinGetClient(() => null)))], md, notes, ct);
            Console.WriteLine($"::notice title=Đề xuất Admin · Viber::Tham số /quiet /norestart → {set.Results[0].Outcome.State} — {set.Results[0].Outcome.Reason}");
        }

        // 3e. Mọi ứng dụng tùy chọn còn lại (trừ ứng dụng nặng): WinGet, link, lối tắt web, ứng dụng hiện mờ.
        var optionalApps = WindowsCatalog.Build(services)
            .Where(e => e.Category == TaskCategory.Software && !e.SelectedByDefault && !Heavy.Contains(e.Module.Descriptor.DisplayName))
            .ToList();
        var appsSet = await RunBatchAsync("Phần mềm tùy chọn", optionalApps, md, problems, ct);
        foreach (var (entry, outcome) in appsSet.Results.Where(r => r.Entry.Module is AppInstallTask { Package.Kind: InstallKind.Web } && r.Outcome.State == TaskState.Success))
        {
            var undo = await entry.Module.RollbackAsync(new TaskContext(new InMemoryRunLogger(), 1), ct);
            if (undo.Outcome != VerifyOutcome.Passed)
            {
                problems.Add($"Xóa lối tắt web {entry.Module.Descriptor.DisplayName}: {undo.Outcome} — {undo.Detail}");
            }
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

        foreach (var f in Findings.Distinct())
        {
            Console.WriteLine("::warning title=Phát hiện về danh sách ứng dụng trên Admin::" + f);
            notes.Add(f);
        }

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

    /// <summary>Chạy riêng các ứng dụng nặng, mỗi ứng dụng một lượt (job --e2e-nang).</summary>
    public static async Task<int> HeavyAsync(string? summaryPath, CancellationToken ct)
    {
        using var inventory = new InventoryProvider();
        var services = new CatalogServices(inventory, new WinGetClient(), new RollbackStore(Path.Combine(Path.GetTempPath(), "LamMotCham-e2e-hoan-tac")));
        var problems = new List<string>();
        var md = new StringBuilder("## Kiểm thử ứng dụng nặng\n\n");
        foreach (var entry in WindowsCatalog.Build(services).Where(e => e.Category == TaskCategory.Software && Heavy.Contains(e.Module.Descriptor.DisplayName)))
        {
            // Trình cài không có tham số im lặng cần người bấm qua từng bước: trên máy CI chỉ tải và kiểm tra chữ ký số
            // của mọi phiên bản, không chạy (lần trước ESET chờ 60 phút vì không ai bấm).
            var app = (AppInstallTask)entry.Module;
            var variants = app.Package.Variants.Where(v => v.Kind != InstallKind.Web).ToList();
            if (variants.Count > 0 && variants.All(v => string.IsNullOrWhiteSpace(v.SilentArgs)))
            {
                md.Append(CultureInfo.InvariantCulture, $"### {app.Package.Name} — trình cài cần thao tác tay: chỉ tải và kiểm tra chữ ký\n\n");
                foreach (var v in variants)
                {
                    string line;
                    try
                    {
                        using var file = await LinkDownloader.DownloadAsync(v.Url, v.Kind, ct);
                        var sig = Authenticode.Check(file.Path);
                        line = $"{v.Label}: {file.Bytes / 1048576.0:0.0} MB, {(sig.IsTrusted ? "chữ ký hợp lệ của hãng tin cậy" : "KHÔNG đạt chữ ký: " + sig.Detail)}";
                        if (!sig.IsTrusted)
                        {
                            problems.Add($"{app.Package.Name} · {line}");
                        }
                    }
#pragma warning disable CA1031 // Ghi nhận mọi lỗi tải thành kết quả kiểm thử.
                    catch (Exception ex)
#pragma warning restore CA1031
                    {
                        line = $"{v.Label}: không tải được — {ex.Message}";
                        problems.Add($"{app.Package.Name} · {line}");
                    }

                    md.Append("- ").Append(line).Append('\n');
                    Console.WriteLine($"::notice title={app.Package.Name}::{line}");
                }

                md.Append('\n');
                continue;
            }

            await RunBatchAsync(entry.Module.Descriptor.DisplayName, [entry], md, problems, ct);
        }

        // Office vừa cài (Microsoft 365): thử thật tính năng đưa Word, Excel, PowerPoint ra desktop, chạy lại, rồi hoàn tác.
        var office = WindowsCatalog.Build(services).Single(e => e.Id == "win.officedesktop");
        var first = await RunBatchAsync("Đưa Word, Excel, PowerPoint ra desktop", [office], md, problems, ct);
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
        var links = OfficeNames.Select(n => Path.Combine(desktop, n + ".lnk")).ToList();
        var present = links.Where(File.Exists).Select(Path.GetFileName).ToList();
        Console.WriteLine($"::notice title=Lối tắt Office trên desktop::{(present.Count == 0 ? "không có" : string.Join(", ", present))}");
        // Microsoft 365 vừa cài thành công thì tác vụ phải tạo đủ 3 lối tắt (không được "Không hỗ trợ").
        var officeInstalled = OfficeDesktopShortcutsTask.IsOfficeInstalled();
        Console.WriteLine($"::warning title=Kết quả lối tắt Office::Office trên máy: {(officeInstalled ? "có" : "không")}; tác vụ: {first.Results[0].Outcome.State} — {first.Results[0].Outcome.Reason}; lối tắt: {present.Count}/3");
        if (officeInstalled && (first.Results[0].Outcome.State != TaskState.Success || present.Count != 3))
        {
            problems.Add($"Đưa Office ra desktop: {first.Results[0].Outcome.State}, thấy {present.Count}/3 lối tắt.");
        }

        var again = await RunBatchAsync("Đưa Word, Excel, PowerPoint ra desktop — lượt 2", [WindowsCatalog.Build(services).Single(e => e.Id == "win.officedesktop")], md, problems, ct);
        var undoOffice = await office.Module.RollbackAsync(new TaskContext(new InMemoryRunLogger(), 1), ct);
        var afterUndo = links.Count(File.Exists);
        Console.WriteLine($"::notice title=Hoàn tác lối tắt Office::{undoOffice.Outcome} — {undoOffice.Detail}; còn {afterUndo} lối tắt");
        if (undoOffice.Outcome != VerifyOutcome.Passed || afterUndo != 0)
        {
            problems.Add($"Hoàn tác lối tắt Office: {undoOffice.Outcome}, còn {afterUndo} lối tắt.");
        }

        md.Append(problems.Count == 0 ? "**Kết quả: Đạt.**\n" : "**Kết quả: Lỗi**\n\n" + string.Join("\n", problems.Select(p => "- " + p)) + "\n");
        if (problems.Count > 0)
        {
            Console.WriteLine("::error title=Kiểm thử ứng dụng nặng (" + problems.Count + " lỗi)::" + string.Join(" ‖ ", problems));
        }

        if (summaryPath is not null)
        {
            await File.AppendAllTextAsync(summaryPath, md.ToString(), Encoding.UTF8, CancellationToken.None);
        }

        Console.WriteLine(md.ToString());
        return problems.Count == 0 ? 0 : 1;
    }

    private static TaskCatalogEntry SoftwareEntry(AppInstallTask task) => new()
    {
        Module = task,
        Category = TaskCategory.Software,
        Description = task.Package.Description,
    };

    /// <summary>Bấm "Dừng sau bước này" ngay khi ứng dụng đầu tiên bắt đầu cài.</summary>
    private static async Task StopMidwayAsync(CatalogServices services, StringBuilder md, List<string> problems, CancellationToken ct)
    {
        var names = new[] { "SumatraPDF", "7-Zip", "VLC media player" };
        var tasks = names.Select(n => SoftwareCatalog.Current.FirstOrDefault(p => p.Name == n && p.IsAvailable))
            .OfType<SoftwarePackage>()
            .Select(p => new AppInstallTask(p, services.WinGet))
            .ToList();
        if (tasks.Count < 2)
        {
            problems.Add("Dừng giữa chừng: không đủ ứng dụng để thử.");
            return;
        }

        // Máy CI có sẵn vài ứng dụng (ví dụ 7-Zip): chỉ coi là "bị cài dù đã dừng" khi trước đó chưa có.
        var preexisting = tasks.Where(t => InstallDetector.FindEvidence(t.Package) is not null).Select(t => t.Package.Name).ToHashSet(StringComparer.Ordinal);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var log = new InMemoryRunLogger();
        var orchestrator = new TaskOrchestrator(log, new ResourceGovernor(new WindowsResourceMonitor()));
        var progress = new StopOnFirstRun(stop);
        var report = await orchestrator.RunAsync([.. tasks], confirmedByUser: true, progress, stop.Token);
        var states = report.Tasks.Select(t => t.State).ToList();
        var line = string.Join("; ", tasks.Select((t, i) => $"{t.Package.Name}={states[i]}"));
        Console.WriteLine($"::notice title=Dừng giữa chừng::{line}; Halt={report.HaltReason}");
        md.Append(CultureInfo.InvariantCulture, $"### Dừng giữa chừng\n\n{line}; lý do dừng hàng đợi: {report.HaltReason}\n\n");
        if (states[0] != TaskState.Success && !(states[0] == TaskState.NotSupported && !services.WinGet.IsAvailable))
        {
            problems.Add($"Dừng giữa chừng: ứng dụng đang cài phải được cài xong, nhưng là {states[0]} — {report.Tasks[0].Reason}");
        }

        if (states.Skip(1).Any(s => s != TaskState.Cancelled) || report.HaltReason != RunHaltReason.CancelledByUser)
        {
            problems.Add($"Dừng giữa chừng: các bước sau phải \"Đã hủy\" ({line}; {report.HaltReason}).");
        }

        foreach (var t in tasks.Skip(1).Where(t => !preexisting.Contains(t.Package.Name)))
        {
            if (InstallDetector.FindEvidence(t.Package) is { } found)
            {
                problems.Add($"Dừng giữa chừng: {t.Package.Name} đã bị cài dù đã dừng ({found}).");
            }
        }
    }

    private sealed class StopOnFirstRun(CancellationTokenSource stop) : IProgress<TaskStatusUpdate>
    {
        public void Report(TaskStatusUpdate value)
        {
            if (value.State == TaskState.Running && !stop.IsCancellationRequested)
            {
                stop.Cancel();
            }
        }
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

            foreach (var w in log.Snapshot().Where(e => e.TaskId == d.Id && e.Message.StartsWith("Cài bằng link chưa được", StringComparison.Ordinal)))
            {
                Findings.Add($"{d.DisplayName}: {w.Message}");
            }

            if (outcome.Reason?.Contains("Máy không có WinGet để cài dự phòng", StringComparison.Ordinal) == true)
            {
                Findings.Add($"{d.DisplayName}: {outcome.Reason}");
            }
            else if (!Acceptable(entry, outcome, rebootExpected))
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

        // Phiên bản "Mở link": phần mềm chỉ mở trang tải cho kỹ thuật viên cài tay, nên "Cần chú ý" là đúng thiết kế.
        if (entry.Module is AppInstallTask { Direct.SelectedVariant.Kind: InstallKind.Web } && outcome.State == TaskState.NeedsAttention)
        {
            return true;
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
