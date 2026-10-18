using LamMotCham.Core.Tasks;
using LamMotCham.Windows.Checks;
using LamMotCham.Windows.Cleanup;
using LamMotCham.Windows.Config;
using LamMotCham.Windows.Disk;
using LamMotCham.Windows.Infrastructure;
using LamMotCham.Windows.Probe;
using LamMotCham.Windows.Software;

namespace LamMotCham.Windows;

/// <summary>Các dịch vụ dùng chung mà task cần.</summary>
public sealed record CatalogServices(InventoryProvider Inventory, WinGetClient WinGet, RollbackStore Rollback);

/// <summary>
/// Danh mục mọi task có trong bản này. Chỉ task có trong danh mục mới chạy được;
/// máy chủ hay file cấu hình không thể thêm task mới (bộ quy tắc 02, ranh giới tin cậy).
/// </summary>
public static class WindowsCatalog
{
    public const string GroupMaintenance = "Cập nhật và bảo trì";
    public const string GroupExperience = "Trải nghiệm Windows";
    public const string GroupPrivacy = "Quyền riêng tư";
    public const string GroupPerformance = "Khởi động và hiệu năng";
    public const string GroupHandoverSystem = "Windows và bảo mật";
    public const string GroupHandoverHardware = "Phần cứng";

    private const string Advanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string DesktopIcons = @"Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel";
    private const string ContentDelivery = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";

    public static IReadOnlyList<TaskCatalogEntry> Build(CatalogServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var list = new List<TaskCatalogEntry>();
        list.AddRange(Software(services));
        list.AddRange(WindowsSettings(services));
        list.AddRange(DiskAndAccount(services));
        list.AddRange(CleanupEntries());
        list.AddRange(Handover(services));
        return list;
    }

    public static TaskCatalogEntry Rename(string newName) => new()
    {
        Module = new RenameComputerTask(newName),
        Category = TaskCategory.DiskAndAccount,
        Description = "Tên mới có hiệu lực sau khi khởi động lại.",
        Impact = TaskImpact.Medium,
        IconKey = "laptop",
        SelectedByDefault = false,
    };

    /// <summary>Sửa tài khoản cục bộ: chỉ tạo khi người dùng bấm Lưu trong hộp sửa tài khoản; không nằm trong gói nhanh.</summary>
    public static TaskCatalogEntry UserFullName(string userName, string fullName) => AccountEntry(new Accounts.UserFullNameTask(userName, fullName),
        "Tên hiện ở màn hình đăng nhập và menu Start.");

    public static TaskCatalogEntry UserPassword(string userName, string password) => AccountEntry(new Accounts.UserPasswordTask(userName, password),
        "Mật khẩu không được ghi vào log hay báo cáo.");

    public static TaskCatalogEntry UserRename(string userName, string newName) => AccountEntry(new Accounts.UserRenameTask(userName, newName),
        "Tên dùng để đăng nhập; thư mục C:\\Users giữ tên cũ.");

    private static TaskCatalogEntry AccountEntry(ITaskModule module, string description) => new()
    {
        Module = module,
        Category = TaskCategory.DiskAndAccount,
        Description = description,
        Impact = TaskImpact.Medium,
        IconKey = "user",
        SelectedByDefault = false,
    };

    /// <summary>Chia ổ (R3): chỉ tạo sau khi xem trước và xác nhận riêng; không bao giờ nằm trong gói nhanh.</summary>
    public static TaskCatalogEntry SplitDisk(DiskSplitPlan plan) => new()
    {
        Module = new DiskSplitTask(plan),
        Category = TaskCategory.DiskAndAccount,
        Description = "Thu nhỏ ổ có sẵn và tạo ổ NTFS mới từ phần trống vừa tách.",
        Impact = TaskImpact.Medium,
        IconKey = "disk",
        SelectedByDefault = false,
    };

    private static IEnumerable<TaskCatalogEntry> Software(CatalogServices s) =>
        SoftwareCatalog.All.Select(p => new TaskCatalogEntry
        {
            Module = new WinGetInstallTask(p, s.WinGet),
            Category = TaskCategory.Software,
            Group = p.Category,
            Description = p.Description,
            Impact = TaskImpact.Medium,
            SelectedByDefault = p.Recommended,
            IconKey = p.IconKey,
            LogoKey = SoftwareCatalog.LogoKeyFor(p.Key),
        });

    private static IEnumerable<TaskCatalogEntry> WindowsSettings(CatalogServices s)
    {
        var store = s.Rollback;

        yield return Setting(new TimeZoneTask(store), GroupMaintenance, "Đặt múi giờ SE Asia Standard Time.", "refresh",
            new TaskDetails("Đổi múi giờ hệ thống sang SE Asia Standard Time.", "Đồng hồ, lịch và giờ ghi log đúng giờ Việt Nam.", "Không có.",
                "Người dùng thường", "Giờ hiển thị trên toàn máy đổi theo múi giờ mới.", "Múi giờ cũ được lưu lại và có thể khôi phục."));
        yield return Setting(new TimeSyncTask(), GroupMaintenance, "Yêu cầu Windows đồng bộ giờ rồi đo lại độ lệch.", "clock",
            new TaskDetails("Yêu cầu dịch vụ Windows Time đồng bộ ngay.", "Giờ sai làm lỗi chứng chỉ, đăng nhập và cập nhật.",
                "Có Internet; dịch vụ Windows Time không bị tắt.", "Quản trị viên", "Không đổi máy chủ thời gian.", "Không cần hoàn tác."));
        yield return Check(new InventoryCheckTask("win.update", "Trạng thái Windows Update", s.Inventory, InventoryChecks.WindowsUpdate),
            TaskCategory.WindowsSettings, GroupMaintenance, "Dịch vụ, lần cập nhật gần nhất, khởi động lại đang chờ.", "refresh");

        yield return Setting(Reg(store, "win.fileext", "Hiện phần mở rộng file", [new(Advanced, "HideFileExt", 0)],
                v => v[0] == 0 ? "đang hiện" : "đang ẩn"),
            GroupExperience, "Hiện đuôi .pdf, .docx, .exe trong File Explorer.", "doc",
            new TaskDetails("Đặt HideFileExt = 0 cho người dùng hiện tại.", "Dễ nhận biết file thật và file giả mạo.", "Không có.",
                "Người dùng thường", "Chỉ đổi cách hiển thị tên file.", "Giá trị cũ được lưu lại."));
        yield return Setting(Reg(store, "win.launchto", "File Explorer mở vào This PC", [new(Advanced, "LaunchTo", 1)],
                v => v[0] == 1 ? "mở vào This PC" : "mở vào Home"),
            GroupExperience, "Mở File Explorer là thấy ngay các ổ đĩa.", "folder",
            new TaskDetails("Đặt LaunchTo = 1.", "Người dùng phổ thông tìm ổ đĩa nhanh hơn.", "Không có.", "Người dùng thường",
                "Chỉ đổi trang mở đầu của File Explorer.", "Giá trị cũ được lưu lại."));
        yield return Setting(Reg(store, "win.desktopicons", "Biểu tượng This PC, Thùng rác, Control Panel",
                [
                    new(DesktopIcons, "{20D04FE0-3AEA-1069-A2D8-08002B30309D}", 0),
                    new(DesktopIcons, "{645FF040-5081-101B-9F08-00AA002F954E}", 0),
                    new(DesktopIcons, "{5399E694-6CE5-4D6C-8FCE-1D8870FDCBA0}", 0),
                ],
                v => v.Count(x => x == 0) + "/3 biểu tượng đang hiện"),
            GroupExperience, "Hiện ba biểu tượng hệ thống trên desktop.", "monitor",
            new TaskDetails("Bật hiển thị ba biểu tượng hệ thống trên desktop.", "Quen thuộc với người dùng phổ thông.", "Không có.",
                "Người dùng thường", "Không ảnh hưởng file trên desktop.", "Giá trị cũ được lưu lại."));
        yield return Setting(Reg(store, "win.taskbarleft", "Căn trái thanh Taskbar", [new(Advanced, "TaskbarAl", 0)],
                v => v[0] == 0 ? "căn trái" : "căn giữa",
                () => Environment.OSVersion.Version.Build >= 22000 ? PreconditionResult.Met() : PreconditionResult.NotSupported("Chỉ áp dụng cho Windows 11.")),
            GroupExperience, "Đưa nút Start về góc trái như Windows 10.", "monitor",
            new TaskDetails("Đặt TaskbarAl = 0.", "Theo thói quen của người dùng.", "Windows 11.", "Người dùng thường",
                "Chỉ đổi vị trí biểu tượng trên taskbar.", "Giá trị cũ được lưu lại."), selected: false);

        yield return Setting(Reg(store, "privacy.adid", "Tắt mã quảng cáo cá nhân hóa",
                [new(@"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", 0)], v => v[0] == 0 ? "đã tắt" : "đang bật"),
            GroupPrivacy, "Ứng dụng không dùng mã quảng cáo để cá nhân hóa.", "lock",
            new TaskDetails("Đặt AdvertisingInfo\\Enabled = 0.", "Hạn chế theo dõi quảng cáo.", "Không có.", "Người dùng thường",
                "Quảng cáo vẫn có thể hiện nhưng không cá nhân hóa.", "Giá trị cũ được lưu lại."));
        yield return Setting(Reg(store, "privacy.tips", "Tắt mẹo, gợi ý và thông báo chào mừng",
                [
                    new(ContentDelivery, "SubscribedContent-338389Enabled", 0),
                    new(ContentDelivery, "SubscribedContent-310093Enabled", 0),
                    new(ContentDelivery, "SubscribedContent-338388Enabled", 0),
                    new(ContentDelivery, "SystemPaneSuggestionsEnabled", 0),
                    new(ContentDelivery, "SoftLandingEnabled", 0),
                ],
                v => v.All(x => x == 0) ? "đã tắt" : "đang bật"),
            GroupPrivacy, "Giảm thông báo gợi ý sau khi cập nhật.", "lock",
            new TaskDetails("Tắt các mục gợi ý trong ContentDeliveryManager của người dùng hiện tại.", "Máy gọn hơn khi bàn giao.", "Không có.",
                "Người dùng thường", "Không ảnh hưởng thông báo của ứng dụng khác.", "Giá trị cũ được lưu lại."));
        yield return Setting(Reg(store, "privacy.tailored", "Tắt trải nghiệm cá nhân hóa từ dữ liệu chẩn đoán",
                [new(@"Software\Microsoft\Windows\CurrentVersion\Privacy", "TailoredExperiencesWithDiagnosticDataEnabled", 0)],
                v => v[0] == 0 ? "đã tắt" : "đang bật"),
            GroupPrivacy, "Không dùng dữ liệu chẩn đoán để gợi ý.", "lock",
            new TaskDetails("Đặt TailoredExperiencesWithDiagnosticDataEnabled = 0.", "Tôn trọng quyền riêng tư của khách.", "Không có.",
                "Người dùng thường", "Không tắt việc gửi dữ liệu chẩn đoán bắt buộc.", "Giá trị cũ được lưu lại."));
        yield return Setting(new BitLockerOffTask(), GroupPrivacy, "Giải mã các ổ đang bật BitLocker / mã hóa thiết bị.", "lock",
            new TaskDetails("Yêu cầu Windows giải mã các ổ đang mã hóa: ổ dữ liệu trước, ổ Windows sau.",
                "Tránh khách bị khóa máy và mất dữ liệu khi không biết khóa khôi phục.",
                "Quyền quản trị; có ổ đang mã hóa. Ổ Windows chỉ tắt sau khi ổ dữ liệu đã giải mã xong.", "Quản trị viên",
                "Dữ liệu không còn được mã hóa. Giải mã chạy nền từ vài phút đến hơn một giờ; máy vẫn dùng được.",
                "Không tự bật lại. Bật lại trong Settings › Privacy & security › Device encryption."), impact: TaskImpact.Medium);

        yield return Setting(new PowerTimeoutsTask(store), GroupPerformance, "Cắm sạc: màn hình 15 phút, ngủ 30 phút. Pin: 5 và 15 phút.", "bolt",
            new TaskDetails("Đổi 4 giá trị thời gian của gói nguồn đang dùng.", "Tiết kiệm pin, tránh máy nóng khi gập.",
                "Không có. Giá trị “Pin” không có tác dụng trên máy không có pin.", "Quản trị viên", "Máy tắt màn hình và ngủ sớm hơn khi không dùng.",
                "Giá trị cũ được lưu lại."), impact: TaskImpact.Medium);
        yield return Check(new InventoryCheckTask("win.startup", "Ứng dụng khởi động cùng Windows", s.Inventory, InventoryChecks.StartupApps),
            TaskCategory.WindowsSettings, GroupPerformance, "Liệt kê để xem xét, không tự tắt.", "power");
        yield return Check(new InventoryCheckTask("win.trial", "Phần mềm dùng thử cài sẵn", s.Inventory, InventoryChecks.TrialSoftware),
            TaskCategory.WindowsSettings, GroupPerformance, "Phát hiện và hướng dẫn gỡ bằng trình gỡ chính thức. Không tự gỡ.", "trash");
    }

    private static IEnumerable<TaskCatalogEntry> DiskAndAccount(CatalogServices s)
    {
        yield return Check(new InventoryCheckTask("disk.bitlocker", "BitLocker / mã hóa thiết bị", s.Inventory, InventoryChecks.BitLocker, PrivilegeLevel.Administrator),
            TaskCategory.DiskAndAccount, null, "Đọc trạng thái mã hóa. Không hiển thị và không ghi khóa khôi phục.", "lock");
        yield return Check(new InventoryCheckTask("disk.health", "Dung lượng và sức khỏe ổ đĩa", s.Inventory, InventoryChecks.DiskHealth),
            TaskCategory.DiskAndAccount, null, "Cảnh báo khi ổ hệ thống còn dưới 20 GB hoặc ổ báo lỗi.", "disk");
    }

    private static IEnumerable<TaskCatalogEntry> CleanupEntries()
    {
        CleanupGroup[] groups = [OldFilesGroup.UserTemp(), OldFilesGroup.WindowsTemp(), OldFilesGroup.ThumbnailCache(), new RecycleBinGroup()];
        return groups.Select(g => new TaskCatalogEntry
        {
            Module = new CleanupTask(g),
            Category = TaskCategory.Cleanup,
            Description = g.Scope,
            Impact = g.IsSafe ? TaskImpact.Low : TaskImpact.Medium,
            SelectedByDefault = g.IsSafe,
            IconKey = g.Key switch { "recycle" => "trash", "thumb" => "monitor", _ => "broom" },
        });
    }

    private static IEnumerable<TaskCatalogEntry> Handover(CatalogServices s)
    {
        (string Id, string Name, Func<InventorySnapshot, VerifyResult> Eval, string Icon, bool Manual, string Group)[] checks =
        [
            ("check.winver", "Phiên bản Windows", InventoryChecks.WindowsVersion, "monitor", false, GroupHandoverSystem),
            ("check.activation", "Kích hoạt Windows", InventoryChecks.Activation, "badge", false, GroupHandoverSystem),
            ("check.security", "Windows Security", InventoryChecks.Security, "shield", false, GroupHandoverSystem),
            ("check.firewall", "Tường lửa", InventoryChecks.Firewall, "shield", false, GroupHandoverSystem),
            ("check.drivers", "Driver", InventoryChecks.Drivers, "chip", false, GroupHandoverSystem),
            ("check.wifi", "Wi-Fi", InventoryChecks.Wifi, "wifi", false, GroupHandoverHardware),
            ("check.bluetooth", "Bluetooth", InventoryChecks.Bluetooth, "bluetooth", false, GroupHandoverHardware),
            ("check.battery", "Pin", InventoryChecks.Battery, "battery", false, GroupHandoverHardware),
            ("check.speaker", "Loa", InventoryChecks.Speaker, "speaker", true, GroupHandoverHardware),
            ("check.mic", "Micro", InventoryChecks.Microphone, "mic", true, GroupHandoverHardware),
            ("check.camera", "Camera", InventoryChecks.Camera, "camera", true, GroupHandoverHardware),
            ("check.keyboard", "Bàn phím", InventoryChecks.Keyboard, "keyboard", true, GroupHandoverHardware),
            ("check.touchpad", "Touchpad", InventoryChecks.Touchpad, "touchpad", true, GroupHandoverHardware),
            ("check.brightness", "Độ sáng màn hình", InventoryChecks.Brightness, "monitor", true, GroupHandoverHardware),
            ("check.ports", "Cổng kết nối", InventoryChecks.Ports, "plug", true, GroupHandoverHardware),
        ];

        return checks.Select(c => new TaskCatalogEntry
        {
            Module = new InventoryCheckTask(c.Id, c.Name, s.Inventory, c.Eval),
            Category = TaskCategory.Handover,
            Group = c.Group,
            Description = c.Manual ? "Ứng dụng phát hiện thiết bị; kỹ thuật viên cần thử thật." : "Ứng dụng tự kiểm tra.",
            Impact = TaskImpact.ReadOnly,
            IconKey = c.Icon,
            RequiresManualConfirmation = c.Manual,
        });
    }

    private static RegistryValuesTask Reg(
        RollbackStore store, string id, string name, IReadOnlyList<RegistryDword> values,
        Func<IReadOnlyList<int?>, string> describe, Func<PreconditionResult>? precondition = null) =>
        new(new TaskDescriptor
        {
            Id = id,
            DisplayName = name,
            Version = 1,
            Risk = RiskLevel.Reversible,
            Privilege = PrivilegeLevel.User,
            Kind = TaskKind.Configuration,
            Timeout = TimeSpan.FromSeconds(20),
            Rollback = RollbackSupport.Supported,
        }, values, store, describe, precondition);

    private static TaskCatalogEntry Setting(ITaskModule module, string group, string description, string icon, TaskDetails details,
        bool selected = true, TaskImpact impact = TaskImpact.Low) => new()
        {
            Module = module,
            Category = TaskCategory.WindowsSettings,
            Group = group,
            Description = description,
            Impact = impact,
            Details = details,
            SelectedByDefault = selected,
            IconKey = icon,
        };

    private static TaskCatalogEntry Check(InventoryCheckTask module, TaskCategory category, string? group, string description, string icon) => new()
    {
        Module = module,
        Category = category,
        Group = group,
        Description = description,
        Impact = TaskImpact.ReadOnly,
        IconKey = icon,
        Details = new TaskDetails("Không thay đổi gì.", "Biết tình trạng máy trước khi bàn giao.", "Không có.",
            module.Descriptor.Privilege == PrivilegeLevel.Administrator ? "Quản trị viên (để đọc đầy đủ)" : "Người dùng thường", "Không có.", "Không cần."),
    };
}
