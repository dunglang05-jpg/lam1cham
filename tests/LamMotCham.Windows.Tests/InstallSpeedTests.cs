using System.Diagnostics;
using LamMotCham.Core.Logging;
using LamMotCham.Core.Tasks;
using LamMotCham.Windows.Software;
using Xunit;

namespace LamMotCham.Windows.Tests;

/// <summary>
/// Tăng tốc cài phần mềm (PO duyệt 01/11/2026): ưu tiên link tải khi Admin có link + cách nhận biết,
/// WinGet làm dự phòng; tải trước file cài trong lúc app khác đang cài.
/// </summary>
public sealed class InstallSpeedTests
{
    private const string List = """
    [
      {"Name":"App Link","Initial":"A","Color":"#123456","Kind":"Exe","Checked":true,
       "DirectUrl":"https://127.0.0.1:1/khong-co.exe","SilentArgs":"/S","WingetId":"Google.Chrome","Publisher":"Google LLC",
       "DetectNames":["LamMotCham-khong-co-app-nay-9f3c"],"MachineWide":true,"Category":"Thử"},
      {"Name":"Chỉ WinGet","Initial":"W","Color":"#123456","Kind":"Exe","WingetId":"RARLab.WinRAR","Publisher":"win.rar GmbH",
       "DirectUrl":"https://127.0.0.1:1/rar.exe","SilentArgs":"/S","Category":"Thử"},
      {"Name":"Nhiều bản","Initial":"N","Color":"#123456","Kind":"Exe","Variants":[
         {"Label":"Tự cài","Kind":"Exe","DirectUrl":"https://127.0.0.1:1/a.exe","SilentArgs":""},
         {"Label":"Mở link","Kind":"Web","DirectUrl":"https://127.0.0.1:1/trang","SilentArgs":""}],
       "DetectNames":["LamMotCham-khong-co-app-nay-9f3c"],"Category":"Thử"}
    ]
    """;

    private static Dictionary<string, SoftwarePackage> Packages() =>
        RemoteCatalog.Parse(List).Packages.ToDictionary(p => p.Name);

    private static TaskContext Context() => new(new InMemoryRunLogger(), 1);

    [Fact]
    public void Link_is_preferred_only_when_admin_has_link_and_detection()
    {
        var p = Packages();
        var noWinGet = new WinGetClient(() => null);

        Assert.True(new AppInstallTask(p["App Link"], noWinGet).PrefersLink);

        // Không có cách nhận biết: vẫn WinGet trước để xác minh được sau cài.
        Assert.False(new AppInstallTask(p["Chỉ WinGet"], noWinGet).PrefersLink);

        // Phiên bản "Mở link" là cài tay: không tính là cài tự động bằng link.
        var multi = new AppInstallTask(p["Nhiều bản"], noWinGet);
        Assert.True(multi.PrefersLink);
        multi.Direct!.SelectedVariant = p["Nhiều bản"].Variants.Single(v => v.Kind == InstallKind.Web);
        Assert.False(multi.PrefersLink);
    }

    [Fact]
    public async Task Link_route_is_chosen_without_running_winget()
    {
        var calls = 0;
        var winGet = new WinGetClient(() =>
        {
            calls++;
            return null;
        });
        var task = new AppInstallTask(Packages()["App Link"], winGet);

        var result = await task.CheckPreconditionsAsync(Context(), CancellationToken.None);

        Assert.Equal(PreconditionOutcome.Met, result.Outcome);
        Assert.Equal(InstallRoute.Link, task.Route);
        Assert.Equal(0, calls);
        Assert.Equal("Chưa phát hiện", await task.DescribeCurrentStateAsync(CancellationToken.None));
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Download_failure_happens_before_installer_and_keeps_reason_without_winget()
    {
        var task = new AppInstallTask(Packages()["App Link"], new WinGetClient(() => null));
        await task.CheckPreconditionsAsync(Context(), CancellationToken.None);

        var result = await task.ExecuteAsync(Context(), CancellationToken.None);

        Assert.Equal(ExecuteOutcome.Failed, result.Outcome);
        Assert.False(task.Direct!.InstallerStarted);
        Assert.Contains("Không tải được", result.Reason, StringComparison.Ordinal);
        Assert.Equal(InstallRoute.Link, task.Route);
    }

    [Fact]
    public async Task Already_installed_app_is_verified_not_left_as_needs_attention()
    {
        // Lỗi 1.1.4: K-Lite đã cài sẵn hiện "Cần kiểm tra — Không xác minh được. Chưa chạy." vì Verify không biết cách xác minh.
        var folder = "LamMotCham-test-" + Guid.NewGuid().ToString("N");
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), folder);
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "app.exe"), [1]);
        try
        {
            var json = $$"""
            [{"Name":"Có sẵn","Initial":"C","Color":"#123456","Kind":"Exe","DirectUrl":"https://127.0.0.1:1/a.exe","SilentArgs":"/S",
              "WingetId":"Google.Chrome","Publisher":"Google LLC","DetectPaths":["%LocalAppData%\\{{folder}}\\app.exe"],"Category":"Thử"}]
            """;
            var task = new AppInstallTask(RemoteCatalog.Parse(json).Packages.Single(), new WinGetClient(() => null));

            var pre = await task.CheckPreconditionsAsync(Context(), CancellationToken.None);
            var verify = await task.VerifyAsync(Context(), CancellationToken.None);

            Assert.Equal(PreconditionOutcome.AlreadyInDesiredState, pre.Outcome);
            Assert.True(task.WasAlreadyInstalled);
            Assert.Equal(VerifyOutcome.Passed, verify.Outcome);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Download_progress_does_not_overflow_and_reads_simply()
    {
        // Lỗi 1.1.5: lần báo đầu tiên trừ TimeSpan.MinValue → OverflowException, Foxit báo "Thất bại".
        var reports = new List<TaskProgress>();
        var context = new TaskContext(new InMemoryRunLogger(), 1, new ListProgress(reports));
        var report = InstallProgress.Download(context);

        report(0, 1000);
        report(1000, 1000);

        Assert.Equal("Đang tải 0%", reports[0].Text);
        Assert.Equal("Đang tải 100%", reports[^1].Text);
        Assert.Equal(InstallProgress.DownloadShare, reports[^1].Fraction);
    }

    [Fact]
    public void Progress_errors_never_break_a_task()
    {
        var context = new TaskContext(new InMemoryRunLogger(), 1, new ThrowingProgress());
        context.Report(0.5, "x");
    }

    private sealed class ListProgress(List<TaskProgress> list) : IProgress<TaskProgress>
    {
        public void Report(TaskProgress value) => list.Add(value);
    }

    private sealed class ThrowingProgress : IProgress<TaskProgress>
    {
        public void Report(TaskProgress value) => throw new OverflowException("thử");
    }

    [Fact]
    public async Task Prefetched_file_is_handed_over_once_then_task_downloads_itself()
    {
        var direct = new DirectInstallTask(Packages()["App Link"]);
        using var prefetcher = new LinkPrefetcher((_, _, _) => Task.FromResult<(DownloadedFile?, string)>((FakeFile(), "Link trực tiếp.")));

        Assert.Equal(1, prefetcher.Start([direct]));
        var first = await prefetcher.TakeAsync(direct, CancellationToken.None);
        Assert.NotNull(first);
        Assert.True(File.Exists(first.File.Path));
        Assert.Equal("Link trực tiếp.", first.Note);

        Assert.Null(await prefetcher.TakeAsync(direct, CancellationToken.None));

        first.File.Dispose();
        Assert.False(Directory.Exists(first.File.Directory));
    }

    [Fact]
    public async Task Changing_variant_after_prefetch_discards_the_file()
    {
        var package = Packages()["Nhiều bản"];
        var direct = new DirectInstallTask(package);
        DownloadedFile? made = null;
        using var prefetcher = new LinkPrefetcher((_, _, _) =>
        {
            made = FakeFile();
            return Task.FromResult<(DownloadedFile?, string)>((made, ""));
        });

        prefetcher.Start([direct]);
        direct.SelectedVariant = package.Variants[1];

        Assert.Null(await prefetcher.TakeAsync(direct, CancellationToken.None));
        Assert.NotNull(made);
        Assert.False(Directory.Exists(made.Directory));
    }

    [Fact]
    public async Task Unused_prefetched_files_are_deleted_when_the_run_ends()
    {
        var p = Packages();
        var made = new List<DownloadedFile>();
        var prefetcher = new LinkPrefetcher((_, _, _) =>
        {
            var f = FakeFile();
            lock (made)
            {
                made.Add(f);
            }

            return Task.FromResult<(DownloadedFile?, string)>((f, ""));
        });

        var a = new DirectInstallTask(p["App Link"]);
        var b = new DirectInstallTask(p["Chỉ WinGet"]);
        prefetcher.Start([a, b]);
        await WaitUntil(() => made.Count == 2);

        prefetcher.Dispose();

        await WaitUntil(() => made.All(f => !Directory.Exists(f.Directory)));
        Assert.Null(await prefetcher.TakeAsync(a, CancellationToken.None));
    }

    [Fact]
    public async Task At_most_two_downloads_run_at_once()
    {
        var p = Packages();
        var running = 0;
        var peak = 0;
        var gate = new TaskCompletionSource();
        using var prefetcher = new LinkPrefetcher(async (_, _, ct) =>
        {
            var now = Interlocked.Increment(ref running);
            InterlockedMax(ref peak, now);
            await gate.Task.WaitAsync(ct);
            Interlocked.Decrement(ref running);
            return (null, "");
        });

        var tasks = Enumerable.Range(0, 5).Select(_ => new DirectInstallTask(p["App Link"])).ToList();
        prefetcher.Start(tasks);
        await WaitUntil(() => Volatile.Read(ref running) == LinkPrefetcher.MaxParallel);
        await Task.Delay(200);
        Assert.Equal(LinkPrefetcher.MaxParallel, Volatile.Read(ref peak));

        gate.SetResult();
        foreach (var t in tasks)
        {
            Assert.Null(await prefetcher.TakeAsync(t, CancellationToken.None));
        }

        Assert.Equal(LinkPrefetcher.MaxParallel, peak);
    }

    private static DownloadedFile FakeFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "l1c-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "setup.exe");
        File.WriteAllBytes(path, [1, 2, 3]);
        return new DownloadedFile(dir, path, 3, TimeSpan.FromSeconds(1), new Uri("https://example.com/setup.exe"));
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), "Hết thời gian chờ.");
            await Task.Delay(20);
        }
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while (value > (current = Volatile.Read(ref target)))
        {
            if (Interlocked.CompareExchange(ref target, value, current) == current)
            {
                return;
            }
        }
    }
}
