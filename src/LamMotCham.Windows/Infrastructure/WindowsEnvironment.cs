using System.Security.Principal;

namespace LamMotCham.Windows.Infrastructure;

/// <summary>Đường dẫn và thông tin môi trường Windows dùng chung.</summary>
public static class WindowsEnvironment
{
    public const string AppFolderName = "LamMotCham";

    /// <summary>Thư mục dữ liệu ứng dụng: %LOCALAPPDATA%\LamMotCham (Q-08, đề xuất mặc định).</summary>
    public static string DataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppFolderName);

    public static string LogsDirectory => Path.Combine(DataDirectory, "Logs");

    public static string ReportsDirectory => Path.Combine(DataDirectory, "Reports");

    public static string RollbackDirectory => Path.Combine(DataDirectory, "Rollback");

    public static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>Đường dẫn tuyệt đối tới công cụ trong System32 để không chạy nhầm file cùng tên cạnh EXE.</summary>
    public static string SystemTool(string fileName) => Path.Combine(Environment.SystemDirectory, fileName);

    public static string PowerShellPath =>
        Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
}
