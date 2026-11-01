using System.Net.Http.Headers;
using System.Text.Json;
using LamMotCham.Core.Logging;
using LamMotCham.Core.Orchestration;
using LamMotCham.Core.Tasks;
using LamMotCham.Windows.Software;

namespace LamMotCham.AppCheck;

/// <summary>
/// Thử một link tải trước khi PO nhập lên Admin (workflow thu-link): link trả về gì với trình tải của phần mềm và với
/// trình duyệt, file có chữ ký số của hãng tin cậy không, cài im lặng bằng tham số đề xuất có xong và nhận biết được không.
/// </summary>
internal static class ThuLink
{
    private const string BrowserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36";

    public static async Task<int> RunAsync(Uri url, string name, string silentArgs, string detectName, string detectPath, CancellationToken ct)
    {
        foreach (var (label, agent) in new[] { ("Trình tải của phần mềm", (string?)null), ("Trình duyệt Chrome", BrowserAgent) })
        {
            using var http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 8 });
            if (agent is null)
            {
                http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("LamMotCham", "1"));
            }
            else
            {
                http.DefaultRequestHeaders.UserAgent.ParseAdd(agent);
            }

            try
            {
                using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                Console.WriteLine($"::notice title={label}::{(int)response.StatusCode} → {response.RequestMessage?.RequestUri} · {response.Content.Headers.ContentType?.MediaType} · {response.Content.Headers.ContentLength?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "?"} byte");
            }
#pragma warning disable CA1031 // Ghi nhận mọi lỗi thành kết quả thử.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                Console.WriteLine($"::notice title={label}::lỗi {ex.Message}");
            }
        }

        var json = JsonSerializer.Serialize(new[]
        {
            new Dictionary<string, object>
            {
                ["Name"] = name, ["Initial"] = "T", ["Color"] = "#0068FF", ["Kind"] = "Exe", ["Checked"] = true,
                ["DirectUrl"] = url.AbsoluteUri, ["SilentArgs"] = silentArgs, ["WingetId"] = "",
                ["DetectNames"] = new[] { detectName }, ["DetectPaths"] = string.IsNullOrEmpty(detectPath) ? Array.Empty<string>() : new[] { detectPath },
                ["Category"] = "Thử",
            },
        });
        var parsed = RemoteCatalog.Parse(json);
        foreach (var w in parsed.Warnings)
        {
            Console.WriteLine("::warning title=Dữ liệu thử::" + w);
        }

        var package = parsed.Packages.Single();
        SoftwareCatalog.Use(parsed.Packages);
        var task = new AppInstallTask(package, new WinGetClient(() => null));
        var log = new InMemoryRunLogger();
        var report = await new TaskOrchestrator(log).RunAsync([task], confirmedByUser: true, null, ct);
        var outcome = report.Tasks.Single();
        foreach (var e in log.Snapshot().Where(e => e.TaskId == task.Descriptor.Id))
        {
            Console.WriteLine($"{e.Level} {e.Message}");
        }

        var m = task.LastMetrics;
        Console.WriteLine($"::notice title=Cài thử {name} (tham số \"{silentArgs}\")::{outcome.State} — {outcome.Reason} · tải {m?.Bytes / 1048576.0:0.0} MB từ {m?.Source} · cài {m?.Install.TotalSeconds:0} giây · mã {m?.ExitCode}");
        foreach (var e in log.Snapshot().Where(e => e.Message.StartsWith("Chữ ký số", StringComparison.Ordinal)))
        {
            Console.WriteLine($"::notice title=Chữ ký số::{e.Message}");
        }

        return outcome.State == TaskState.Success ? 0 : 1;
    }
}
