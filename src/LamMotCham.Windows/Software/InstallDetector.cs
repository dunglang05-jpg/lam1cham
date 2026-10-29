using Microsoft.Win32;

namespace LamMotCham.Windows.Software;

/// <summary>
/// Xác minh ứng dụng đã có trên máy theo dữ liệu nhận biết trên Web Admin: tên trong "Installed apps"
/// (khóa Uninstall của Windows, cả 64-bit, 32-bit và người dùng hiện tại) hoặc đường dẫn file.
/// </summary>
public static class InstallDetector
{
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    /// <summary>Có dữ liệu để xác minh không. Không có thì không được báo "đã cài".</summary>
    public static bool CanDetect(SoftwarePackage package) =>
        package is not null && (package.DetectNames.Count > 0 || package.DetectPaths.Count > 0);

    /// <summary>Bằng chứng đã cài (tên hiển thị hoặc đường dẫn tìm thấy), null nếu chưa thấy.</summary>
    public static string? FindEvidence(SoftwarePackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        foreach (var raw in package.DetectPaths)
        {
            var path = Environment.ExpandEnvironmentVariables(raw);
            if (!path.Contains('%', StringComparison.Ordinal) && File.Exists(path))
            {
                return path;
            }
        }

        if (package.DetectNames.Count == 0)
        {
            return null;
        }

        foreach (var name in InstalledDisplayNames())
        {
            if (package.DetectNames.Any(n => name.Contains(n, StringComparison.OrdinalIgnoreCase)))
            {
                return name;
            }
        }

        return null;
    }

    /// <summary>Tên các ứng dụng trong "Installed apps".</summary>
    internal static IEnumerable<string> InstalledDisplayNames()
    {
        var names = new List<string>();
        Read(RegistryHive.LocalMachine, RegistryView.Registry64, names);
        Read(RegistryHive.LocalMachine, RegistryView.Registry32, names);
        Read(RegistryHive.CurrentUser, RegistryView.Default, names);
        return names;
    }

    private static void Read(RegistryHive hive, RegistryView view, List<string> names)
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(hive, view);
            using var key = root.OpenSubKey(UninstallKey);
            if (key is null)
            {
                return;
            }

            foreach (var sub in key.GetSubKeyNames())
            {
                using var app = key.OpenSubKey(sub);
                if (app?.GetValue("DisplayName") is string name && !string.IsNullOrWhiteSpace(name))
                {
                    names.Add(name);
                }
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            // Không đọc được một nhánh registry: bỏ qua nhánh đó, không coi là đã cài.
        }
    }
}
