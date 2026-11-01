using System.Globalization;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using LamMotCham.Core.Account;
using LamMotCham.Core.Logging;
using LamMotCham.Core.Tasks;
using LamMotCham.Windows.Software;

namespace LamMotCham.AppCheck;

/// <summary>Một dòng kết quả kiểm tra.</summary>
internal sealed record CheckRow(string App, string Item, string Status, string Detail);

/// <summary>
/// Kiểm tra danh sách ứng dụng của Web Admin trên máy Windows của GitHub (QĐ-006):
/// - link tải: tải về, kiểm tra chữ ký số và hãng ký có trong danh sách tin cậy của phần mềm;
/// - WinGet: nhà phát hành của gói khớp ô "Nhà phát hành" trên Admin;
/// - trang web: còn mở được;
/// - (tùy chọn) cài thử 7-Zip qua WinGet, WinRAR qua link, tạo lối tắt web, rồi xác minh như phần mềm thật.
/// Mã thoát 1 khi có lỗi làm laptop không cài được ứng dụng đang bật.
/// </summary>
internal static class Program
{
    private const string Pass = "Đạt";
    private const string Fail = "Lỗi";
    private const string Warn = "Cảnh báo";
    private const string Skip = "Bỏ qua";
    private const string Greyed = "Hiện mờ";

    private static readonly HttpClient Web = CreateWeb();

    private static readonly JsonSerializerOptions JsonOut = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static async Task<int> Main(string[] args)
    {
        var snapshot = Arg(args, "--snapshot");
        var summaryPath = Arg(args, "--summary");
        var jsonPath = Arg(args, "--json");
        var install = args.Contains("--install", StringComparer.Ordinal);
        using var stop = new CancellationTokenSource(TimeSpan.FromMinutes(args.Any(a => a is "--e2e-all" or "--e2e-nang") ? 100 : 50));
        var ct = stop.Token;

        if (Arg(args, "--thu-link") is { } testUrl)
        {
            return await ThuLink.RunAsync(new Uri(testUrl), Arg(args, "--ten") ?? "Ứng dụng thử", Arg(args, "--tham-so") ?? "", Arg(args, "--nhan-biet") ?? "", Arg(args, "--duong-dan") ?? "", ct);
        }

        var (json, source) = await LoadListAsync(snapshot, ct);
        var parsed = RemoteCatalog.Parse(json);
        SoftwareCatalog.Use(parsed.Packages);
        Console.WriteLine($"Nguồn danh sách: {source}. {parsed.Packages.Count} ứng dụng.");
        foreach (var w in parsed.Warnings)
        {
            Console.WriteLine("Cảnh báo danh sách: " + w);
        }

        Console.WriteLine($"::notice title=Nguồn danh sách ứng dụng::{source}, {parsed.Packages.Count} ứng dụng.");
        if (args.Contains("--e2e-nang", StringComparer.Ordinal))
        {
            return await E2EAll.HeavyAsync(summaryPath, ct);
        }

        if (args.Contains("--e2e-all", StringComparer.Ordinal))
        {
            return await E2EAll.RunAsync(summaryPath, ct);
        }

        if (args.Contains("--e2e", StringComparer.Ordinal))
        {
            return await E2E.RunAsync(parsed.Packages, summaryPath, ct);
        }

        var winGet = new WinGetClient();
        var winGetVersion = WinGetClient.ParseVersion(await SafeAsync(() => winGet.GetVersionAsync(ct), null));
        Console.WriteLine("WinGet trên máy CI: " + (winGetVersion?.ToString() ?? "không có"));

        var rows = new List<CheckRow>();
        foreach (var p in parsed.Packages)
        {
            Console.WriteLine("— " + p.Name);
            rows.AddRange(await CheckAppAsync(p, winGet, winGetVersion is not null, ct));
        }

        if (install)
        {
            rows.AddRange(await InstallTestsAsync(parsed.Packages, winGet, winGetVersion is not null, ct));
        }

        foreach (var r in rows)
        {
            Console.WriteLine($"[{r.Status}] {r.App} · {r.Item}: {r.Detail}");
        }

        var markdown = Summary(rows, source, parsed.Packages.Count, winGetVersion);
        if (summaryPath is not null)
        {
            await File.AppendAllTextAsync(summaryPath, markdown, Encoding.UTF8, CancellationToken.None);
        }

        if (jsonPath is not null)
        {
            await File.WriteAllTextAsync(
                jsonPath,
                JsonSerializer.Serialize(rows, JsonOut),
                Encoding.UTF8,
                CancellationToken.None);
        }

        return rows.Any(r => r.Status == Fail) ? 1 : 0;
    }

    /// <summary>Danh sách thật từ Web Admin nếu có tài khoản kiểm tra (secret), ngược lại dùng ảnh chụp trong repo.</summary>
    private static async Task<(string Json, string Source)> LoadListAsync(string? snapshot, CancellationToken ct)
    {
        var key = Environment.GetEnvironmentVariable("LMC_API_KEY");
        var user = Environment.GetEnvironmentVariable("LMC_CI_USER");
        var pass = Environment.GetEnvironmentVariable("LMC_CI_PASS");
        if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(user) && !string.IsNullOrWhiteSpace(pass))
        {
            using var http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false });
            var client = new AccountClient(http, new AccountClientOptions(AccountClientOptions.DefaultEndpoint, key.Trim()));
            const string Machine = "lmc-kiem-tra-ci";
            var login = await client.LoginAsync(user, pass, Machine, ct);
            if (login.Session is { } session)
            {
                var list = await client.ListAppsAsync(session.Token, Machine, ct);
                if (list.IsSuccess)
                {
                    return (list.AppsJson!, "Web Admin (lấy trực tiếp)");
                }

                Console.WriteLine("Không lấy được danh sách từ Web Admin: " + list.Status.Message);
            }
            else
            {
                Console.WriteLine("Không đăng nhập được tài khoản kiểm tra: " + login.Message);
            }
        }

        if (snapshot is null || !File.Exists(snapshot))
        {
            throw new InvalidOperationException("Không có tài khoản kiểm tra và không có file ảnh chụp danh sách (--snapshot).");
        }

        return (await File.ReadAllTextAsync(snapshot, ct), "ảnh chụp danh sách trong repo (" + Path.GetFileName(snapshot) + ")");
    }

    private static async Task<List<CheckRow>> CheckAppAsync(SoftwarePackage p, WinGetClient winGet, bool hasWinGet, CancellationToken ct)
    {
        var rows = new List<CheckRow>();
        if (!p.IsAvailable)
        {
            rows.Add(new CheckRow(p.Name, "Danh mục", Greyed, p.UnavailableReason!));
            return rows;
        }

        if (p.Kind == InstallKind.Web)
        {
            rows.Add(await CheckPageAsync(p.Name, "Trang web", p.Website!, ct));
            return rows;
        }

        if (p.UsesWinGet)
        {
            if (!hasWinGet)
            {
                rows.Add(new CheckRow(p.Name, "WinGet " + p.WinGetId, Skip, "Máy CI không có WinGet."));
            }
            else
            {
                var problem = await SafeAsync(() => winGet.CheckPublisherAsync(p, ct), "Lỗi khi chạy winget show.");
                rows.Add(problem is null
                    ? new CheckRow(p.Name, "WinGet " + p.WinGetId, Pass, $"Nhà phát hành khớp \"{p.ExpectedPublisher}\".")
                    : new CheckRow(p.Name, "WinGet " + p.WinGetId, Fail, problem));
            }
        }

        if (p.HasLink)
        {
            // Link chỉ là dự phòng khi đã có WinGet: lỗi link không làm laptop mất cách cài.
            var (url, note) = await LinkDownloader.ResolveAsync(p, ct);
            if (url is null)
            {
                rows.Add(new CheckRow(p.Name, "Link tải", p.UsesWinGet ? Warn : Fail, note));
            }
            else
            {
                rows.Add(await CheckDownloadAsync(p.Name, "Link tải", url, p.Kind, p.ZipExe, p.UsesWinGet, ct));
            }
        }

        foreach (var v in p.Variants)
        {
            rows.Add(v.Kind == InstallKind.Web
                ? await CheckPageAsync(p.Name, "Phiên bản: " + v.Label, v.Url, ct)
                : await CheckDownloadAsync(p.Name, "Phiên bản: " + v.Label, v.Url, v.Kind, null, fallbackOnly: false, ct));
        }

        return rows;
    }

    private static async Task<CheckRow> CheckDownloadAsync(string app, string item, Uri url, InstallKind kind, string? zipExe, bool fallbackOnly, CancellationToken ct)
    {
        var bad = fallbackOnly ? Warn : Fail;
        try
        {
            using var file = await LinkDownloader.DownloadAsync(url, kind, ct);
            var target = file.Path;
            if (kind == InstallKind.Zip)
            {
                var dir = Path.Combine(file.Directory, "zip");
                ZipFile.ExtractToDirectory(file.Path, dir);
                target = zipExe is null ? "" : Directory.EnumerateFiles(dir, zipExe, SearchOption.AllDirectories).FirstOrDefault() ?? "";
                if (target.Length == 0)
                {
                    return new CheckRow(app, item, bad, $"Không thấy {zipExe} trong file ZIP.");
                }
            }

            var sig = Authenticode.Check(target);
            var size = string.Create(CultureInfo.InvariantCulture, $"{file.Bytes / 1048576.0:0.0} MB từ {file.Source.Host}");
            if (sig.IsTrusted)
            {
                return new CheckRow(app, item, Pass, $"Ký bởi \"{sig.SignerText}\" ({size}).");
            }

            return sig.IsValid
                ? new CheckRow(app, item, bad, $"Chữ ký hợp lệ nhưng hãng \"{sig.SignerText}\" (O={sig.Organization}; CN={sig.CommonName}) chưa có trong danh sách tin cậy ({size}).")
                : new CheckRow(app, item, bad, $"{sig.Detail} ({size}).");
        }
#pragma warning disable CA1031 // Công cụ kiểm tra: mọi lỗi tải đều ghi thành một dòng kết quả.
        catch (Exception ex) when (!ct.IsCancellationRequested)
#pragma warning restore CA1031
        {
            return new CheckRow(app, item, bad, $"Không tải được từ {url.Host}: {ex.Message}");
        }
    }

    private static async Task<CheckRow> CheckPageAsync(string app, string item, Uri url, CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            using var response = await Web.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            var code = (int)response.StatusCode;
            return code is >= 200 and < 400
                ? new CheckRow(app, item, Pass, $"Mở được ({code}).")
                : new CheckRow(app, item, Warn, $"Trang trả về {code} (có thể chặn máy CI).");
        }
#pragma warning disable CA1031 // Công cụ kiểm tra: trang không mở được chỉ là cảnh báo.
        catch (Exception ex) when (!ct.IsCancellationRequested)
#pragma warning restore CA1031
        {
            return new CheckRow(app, item, Warn, "Không mở được: " + ex.Message);
        }
    }

    /// <summary>Cài thử như phần mềm thật: kiểm tra điều kiện → cài → xác minh; lối tắt web thì hoàn tác sau khi thử.</summary>
    private static async Task<List<CheckRow>> InstallTestsAsync(IReadOnlyList<SoftwarePackage> packages, WinGetClient winGet, bool hasWinGet, CancellationToken ct)
    {
        var rows = new List<CheckRow>();
        if (hasWinGet)
        {
            // Ứng dụng nhỏ, máy CI thường chưa có sẵn.
            var candidates = new[] { "SumatraPDF.SumatraPDF", "VideoLAN.VLC", "7zip.7zip" };
            var viaWinGet = candidates.Select(id => packages.FirstOrDefault(p => p.WinGetId == id && p.UsesWinGet))
                .FirstOrDefault(p => p is not null && InstallDetector.FindEvidence(p) is null);
            rows.Add(viaWinGet is null
                ? new CheckRow("WinGet", "Cài thử qua WinGet", Warn, "Máy CI đã có sẵn mọi ứng dụng dùng để thử.")
                : await RunAsync("Cài thử qua WinGet", new AppInstallTask(viaWinGet, winGet), ct));
        }

        var winRar = packages.FirstOrDefault(p => p.WinGetId == "RARLab.WinRAR" && p.HasLink);
        if (winRar is not null)
        {
            // Bỏ mã WinGet để buộc đi đường link tải (kiểm tra tải, chữ ký, cài im lặng, xác minh).
            rows.Add(await RunAsync("Cài thử qua link", new AppInstallTask(winRar with { WinGetId = null, ExpectedPublisher = "" }, winGet), ct));
        }

        var web = packages.FirstOrDefault(p => p.Kind == InstallKind.Web && p.IsAvailable);
        if (web is not null)
        {
            var task = new AppInstallTask(web, winGet);
            rows.Add(await RunAsync("Tạo lối tắt web", task, ct));
            var undo = await task.RollbackAsync(new TaskContext(new InMemoryRunLogger(), 1), ct);
            rows.Add(new CheckRow(web.Name, "Hoàn tác lối tắt", undo.Outcome == VerifyOutcome.Passed ? Pass : Fail, undo.Detail ?? ""));
        }

        return rows;
    }

    private static async Task<CheckRow> RunAsync(string item, AppInstallTask task, CancellationToken ct)
    {
        var log = new InMemoryRunLogger();
        var context = new TaskContext(log, 1);
        string Trail() => string.Join(" | ", log.Snapshot().Select(e => e.Message));
        try
        {
            var pre = await task.CheckPreconditionsAsync(context, ct);
            if (pre.Outcome == PreconditionOutcome.AlreadyInDesiredState)
            {
                return new CheckRow(task.Package.Name, item, Warn, "Máy CI đã có sẵn ứng dụng: " + pre.Reason);
            }

            if (pre.Outcome != PreconditionOutcome.Met)
            {
                return new CheckRow(task.Package.Name, item, Fail, $"Không cài được: {pre.Reason}. {Trail()}");
            }

            var run = await task.ExecuteAsync(context, ct);
            if (run.Outcome == ExecuteOutcome.Failed)
            {
                return new CheckRow(task.Package.Name, item, Fail, $"{run.Reason} {Trail()}");
            }

            var verify = await task.VerifyAsync(context, ct);
            return verify.Outcome == VerifyOutcome.Passed
                ? new CheckRow(task.Package.Name, item, Pass, $"Đi đường {task.Route}: {verify.Detail}")
                : new CheckRow(task.Package.Name, item, Fail, $"Cài xong nhưng chưa xác minh được ({verify.Outcome}): {verify.Detail} {Trail()}");
        }
#pragma warning disable CA1031 // Công cụ kiểm tra: lỗi bất ngờ ghi thành một dòng kết quả.
        catch (Exception ex) when (!ct.IsCancellationRequested)
#pragma warning restore CA1031
        {
            return new CheckRow(task.Package.Name, item, Fail, $"{ex.GetType().Name}: {ex.Message} {Trail()}");
        }
    }

    private static string Summary(List<CheckRow> rows, string source, int apps, Version? winGet)
    {
        static string Cell(string s) => s.Replace("|", "\\|", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
        var sb = new StringBuilder();
        sb.AppendLine("## Kiểm tra danh sách ứng dụng (QĐ-006)");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"- Nguồn: {source} · {apps} ứng dụng · WinGet trên máy CI: {winGet?.ToString() ?? "không có"}");
        foreach (var status in new[] { Fail, Warn, Pass, Greyed, Skip })
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"- {status}: {rows.Count(r => r.Status == status)}");
        }

        sb.AppendLine();
        sb.AppendLine("| Kết quả | Ứng dụng | Mục | Chi tiết |");
        sb.AppendLine("|---|---|---|---|");
        var order = new[] { Fail, Warn, Greyed, Skip, Pass };
        foreach (var r in rows.OrderBy(r => Array.IndexOf(order, r.Status)))
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"| {r.Status} | {Cell(r.App)} | {Cell(r.Item)} | {Cell(r.Detail)} |");
        }

        return sb.ToString();
    }

    private static async Task<T?> SafeAsync<T>(Func<Task<T?>> action, T? fallback)
    {
        try
        {
            return await action();
        }
#pragma warning disable CA1031 // Công cụ kiểm tra: lỗi được báo bằng giá trị thay thế.
        catch (Exception)
#pragma warning restore CA1031
        {
            return fallback;
        }
    }

    private static string? Arg(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static HttpClient CreateWeb()
    {
        var http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 8 });
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Mozilla", "5.0"));
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("(compatible; LamMotCham-KiemTra/1.0)"));
        http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("vi-VN,vi;q=0.9,en;q=0.5");
        return http;
    }
}
