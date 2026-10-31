using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.RegularExpressions;

namespace LamMotCham.Windows.Software;

/// <summary>
/// File cài đã tải về. Giữ một handle chỉ đọc (FileShare.Read) suốt lúc kiểm tra chữ ký và chạy cài,
/// để không tiến trình nào sửa hay thay file giữa chừng. Dispose là xóa thư mục tạm.
/// </summary>
public sealed class DownloadedFile : IDisposable
{
    private FileStream? _lock;

    internal DownloadedFile(string directory, string path, long bytes, TimeSpan elapsed, Uri source)
    {
        Directory = directory;
        Path = path;
        Bytes = bytes;
        Elapsed = elapsed;
        Source = source;
        _lock = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    public string Directory { get; }

    public string Path { get; }

    public long Bytes { get; }

    public TimeSpan Elapsed { get; }

    /// <summary>Địa chỉ cuối cùng (sau chuyển hướng) đã tải.</summary>
    public Uri Source { get; }

    public void Dispose()
    {
        _lock?.Dispose();
        _lock = null;
        LinkDownloader.TryDeleteDirectory(Directory);
    }
}

/// <summary>
/// Tải file cài bằng link của Web Admin (QĐ-006): chỉ https (kể cả sau chuyển hướng), giới hạn dung lượng,
/// lưu vào thư mục tạm chỉ quản trị viên và SYSTEM ghi được. Dò link mới nhất bằng mẫu trên Admin có giới hạn thời gian.
/// </summary>
public static class LinkDownloader
{
    /// <summary>Giới hạn dung lượng một file cài.</summary>
    public const long MaxBytes = 6L * 1024 * 1024 * 1024;

    private const int MaxPageChars = 3 * 1024 * 1024;

    private static readonly HttpClient Http = CreateClient();

    /// <summary>Link tải: dò trên trang (nếu có mẫu) rồi mới dùng link trực tiếp.</summary>
    public static async Task<(Uri? Url, string Note)> ResolveAsync(SoftwarePackage package, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (package.PageUrl is { } page && package.PageRegex is { } pattern)
        {
            try
            {
                var found = await FindOnPageAsync(page, pattern, cancellationToken).ConfigureAwait(false);
                if (found is not null)
                {
                    return (found, $"Dò được link mới nhất trên {page.Host}.");
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or RegexMatchTimeoutException or InvalidDataException)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }

                if (package.DirectUrl is null)
                {
                    return (null, $"Không dò được link trên {page.Host}: {ex.Message}");
                }
            }
        }

        return package.DirectUrl is { } direct
            ? (direct, "Dùng link tải trực tiếp.")
            : (null, "Không tìm thấy link tải.");
    }

    internal static async Task<Uri?> FindOnPageAsync(Uri page, string pattern, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var response = await Http.GetAsync(page, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
        if (html.Length > MaxPageChars)
        {
            throw new InvalidDataException("Trang tải quá lớn.");
        }

        return FindLink(html, pattern, response.RequestMessage?.RequestUri ?? page);
    }

    /// <summary>Lấy link đầu tiên khớp mẫu; link tương đối được ghép với địa chỉ trang. Chỉ chấp nhận https.</summary>
    internal static Uri? FindLink(string html, string pattern, Uri baseUri)
    {
        var regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
        foreach (Match m in regex.Matches(html))
        {
            var value = System.Net.WebUtility.HtmlDecode(m.Value.Trim().Trim('"', '\''));
            if (Uri.TryCreate(baseUri, value, out var uri) && uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo))
            {
                return uri;
            }
        }

        return null;
    }

    /// <param name="onBytes">Báo số byte đã tải và tổng dung lượng (null nếu máy chủ không báo) để hiện tiến độ.</param>
    public static async Task<DownloadedFile> DownloadAsync(Uri url, InstallKind kind, CancellationToken cancellationToken, Action<long, long?>? onBytes = null)
    {
        ArgumentNullException.ThrowIfNull(url);
        if (url.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidDataException("Chỉ tải file qua https.");
        }

        var dir = CreatePrivateDirectory();
        var watch = Stopwatch.StartNew();
        try
        {
            using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var final = response.RequestMessage?.RequestUri ?? url;
            if (final.Scheme != Uri.UriSchemeHttps)
            {
                throw new InvalidDataException("Link tải chuyển hướng sang địa chỉ không mã hóa (http); không tải.");
            }

            if (response.Content.Headers.ContentLength is > MaxBytes)
            {
                throw new InvalidDataException("File cài lớn hơn giới hạn cho phép.");
            }

            var path = System.IO.Path.Combine(dir, FileNameFor(final, response.Content.Headers.ContentDisposition, kind));
            long? length = response.Content.Headers.ContentLength is > 0 and var l ? l : null;
            long total = 0;
            onBytes?.Invoke(0, length);
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    total += read;
                    if (total > MaxBytes)
                    {
                        throw new InvalidDataException("File cài lớn hơn giới hạn cho phép.");
                    }

                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    onBytes?.Invoke(total, length);
                }
            }

            if (total == 0)
            {
                throw new InvalidDataException("File tải về rỗng.");
            }

            return new DownloadedFile(dir, path, total, watch.Elapsed, final);
        }
        catch
        {
            TryDeleteDirectory(dir);
            throw;
        }
    }

    /// <summary>Tên file an toàn theo đuôi đúng loại cài; không dùng nguyên tên do máy chủ gửi.</summary>
    internal static string FileNameFor(Uri source, ContentDispositionHeaderValue? disposition, InstallKind kind)
    {
        var ext = kind switch { InstallKind.Msi => ".msi", InstallKind.Zip => ".zip", _ => ".exe" };
        var candidate = disposition?.FileNameStar ?? disposition?.FileName ?? System.IO.Path.GetFileName(source.AbsolutePath);
        candidate = (candidate ?? "").Trim('"', ' ');
        var safe = new string([.. candidate.Where(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_')]);
        if (safe.Length is < 3 or > 80 || !safe.EndsWith(ext, StringComparison.OrdinalIgnoreCase) || safe.StartsWith('.'))
        {
            safe = "setup" + ext;
        }

        return safe;
    }

    /// <summary>Thư mục tạm mới chỉ Administrators và SYSTEM có quyền (ứng dụng luôn chạy quyền quản trị — QĐ-005).</summary>
    internal static string CreatePrivateDirectory()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "LamMotCham-tai-" + Guid.NewGuid().ToString("N"));
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var sid in new[] { WellKnownSidType.BuiltinAdministratorsSid, WellKnownSidType.LocalSystemSid })
        {
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(sid, null),
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
        }

        new DirectoryInfo(path).Create(security);
        return path;
    }

    internal static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (System.IO.Directory.Exists(directory))
            {
                System.IO.Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // File cài có thể còn bị trình cài giữ; thư mục tạm sẽ được Windows dọn sau.
        }
    }

    private static HttpClient CreateClient()
    {
        var http = new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 8,
            ConnectTimeout = TimeSpan.FromSeconds(20),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        })
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("LamMotCham", "1"));
        http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("vi-VN,vi;q=0.9,en;q=0.5");
        return http;
    }
}
