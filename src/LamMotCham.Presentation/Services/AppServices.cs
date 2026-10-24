using System.Diagnostics;
using System.Reflection;
using LamMotCham.Core.Logging;
using LamMotCham.Core.Orchestration;
using LamMotCham.Core.Resources;
using LamMotCham.Core.Tasks;
using LamMotCham.Windows;
using LamMotCham.Windows.Infrastructure;
using LamMotCham.Windows.Probe;
using LamMotCham.Windows.Resources;
using LamMotCham.Windows.Software;

namespace LamMotCham.Presentation.Services;

/// <summary>Các dịch vụ dùng chung trong toàn ứng dụng.</summary>
public sealed class AppServices : IDisposable
{
    private AppServices(
        InMemoryRunLogger memory,
        IRunLogger log,
        InventoryProvider inventory,
        WinGetClient winGet,
        RollbackStore rollback,
        WindowsResourceMonitor monitor,
        TaskOrchestrator orchestrator,
        IReadOnlyList<TaskCatalogEntry> catalog,
        string version,
        AccountService account)
    {
        Memory = memory;
        Log = log;
        Inventory = inventory;
        WinGet = winGet;
        Rollback = rollback;
        Monitor = monitor;
        Orchestrator = orchestrator;
        Catalog = catalog;
        IsElevated = WindowsEnvironment.IsElevated();
        Version = version;
        Account = account;
    }

    public InMemoryRunLogger Memory { get; }

    public IRunLogger Log { get; }

    public InventoryProvider Inventory { get; }

    public WinGetClient WinGet { get; }

    public RollbackStore Rollback { get; }

    public WindowsResourceMonitor Monitor { get; }

    public TaskOrchestrator Orchestrator { get; }

    public IReadOnlyList<TaskCatalogEntry> Catalog { get; }

    public bool IsElevated { get; }

    public string Version { get; }

    /// <summary>Mô-đun "account": đăng nhập với máy chủ Web Admin; phiên chỉ trong bộ nhớ.</summary>
    public AccountService Account { get; }

    public static AppServices Create()
    {
        var memory = new InMemoryRunLogger(2_000);
        IRunLogger log = memory;
        try
        {
            log = new CompositeRunLogger(new RotatingFileLogger(WindowsEnvironment.LogsDirectory, maxFileBytes: 2_097_152, maxFiles: 5), memory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            memory.Warning("Không ghi được file log; chỉ giữ log trong bộ nhớ. " + ex.Message);
        }

        var inventory = new InventoryProvider();
        var winGet = new WinGetClient();
        var rollback = new RollbackStore(WindowsEnvironment.RollbackDirectory);
        var monitor = new WindowsResourceMonitor();
        var governor = new ResourceGovernor(monitor);
        var orchestrator = new TaskOrchestrator(log, governor);
        var catalog = WindowsCatalog.Build(new CatalogServices(inventory, winGet, rollback));
        var version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0] ?? "0.1.0";
        var account = AccountService.CreateDefault(log, version);
        log.Info("Khởi động Làm 1 Chạm.");
        return new AppServices(memory, log, inventory, winGet, rollback, monitor, orchestrator, catalog, version, account);
    }

    public void Dispose()
    {
        Account.Dispose();
        Inventory.Dispose();
    }
}

/// <summary>Các thao tác mở file, thư mục, trang cài đặt — chỉ với đích cố định hoặc do ứng dụng tạo ra.</summary>
public static class ShellActions
{
    public static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        ViaExplorer(path);
    }

    /// <summary>Mở file (báo cáo HTML...) bằng ứng dụng mặc định, với quyền người dùng thường.</summary>
    public static void OpenFile(string path)
    {
        if (File.Exists(path))
        {
            ViaExplorer(path);
        }
    }

    public static void OpenUri(string uri)
    {
        // Chỉ các URI cố định trong ứng dụng (ms-settings, ms-windows-store, trang hỗ trợ của Microsoft).
        ViaExplorer(uri);
    }

    /// <summary>
    /// Ứng dụng luôn chạy quyền quản trị (QĐ-005). Giao cho Explorer (đang chạy quyền người dùng) mở file/trang web,
    /// để trình duyệt và ứng dụng khác không chạy kèm quyền quản trị.
    /// </summary>
    private static void ViaExplorer(string target) =>
        Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"))
        {
            UseShellExecute = false,
            ArgumentList = { target },
        })?.Dispose();

    /// <summary>Khởi động lại máy. Chỉ gọi sau khi người dùng bấm "Khởi động lại ngay".</summary>
    public static void RestartComputer() =>
        Process.Start(new ProcessStartInfo(WindowsEnvironment.SystemTool("shutdown.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            ArgumentList = { "/r", "/t", "5", "/c", "Làm 1 Chạm: khởi động lại theo yêu cầu của người dùng." },
        })?.Dispose();
}
