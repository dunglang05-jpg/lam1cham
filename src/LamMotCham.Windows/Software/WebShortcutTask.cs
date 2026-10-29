using System.Text;

namespace LamMotCham.Windows.Software;

/// <summary>
/// Ứng dụng loại "Trang web" trên Web Admin: tạo lối tắt (.url) trên màn hình nền chung của máy tới trang https.
/// Không tải hay chạy file nào.
/// </summary>
public static class WebShortcutTask
{
    public static string ShortcutPath(SoftwarePackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), SafeFileName(package.Name) + ".url");
    }

    public static string Content(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);
        return "[InternetShortcut]\r\nURL=" + url.AbsoluteUri + "\r\n";
    }

    public static async Task CreateAsync(SoftwarePackage package, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (package.Website is not { Scheme: "https" } url)
        {
            throw new InvalidOperationException("Thiếu trang web https để tạo lối tắt.");
        }

        var path = ShortcutPath(package);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, Content(url), new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Lối tắt có đúng trang web đang khai báo không.</summary>
    public static bool Exists(SoftwarePackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        var path = ShortcutPath(package);
        try
        {
            return package.Website is { } url && File.Exists(path) &&
                   File.ReadAllText(path).Contains("URL=" + url.AbsoluteUri, StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static bool Remove(SoftwarePackage package)
    {
        var path = ShortcutPath(package);
        try
        {
            File.Delete(path);
            return !File.Exists(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Tên file an toàn từ tên ứng dụng (tên đã được kiểm tra: chữ, số, khoảng trắng và . + - _ ( )).</summary>
    public static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string([.. (name ?? "").Where(c => !invalid.Contains(c))]).Trim().Trim('.');
        return safe.Length == 0 ? "Ung dung" : safe;
    }
}
