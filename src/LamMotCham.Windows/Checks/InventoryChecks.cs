using System.Globalization;
using System.Text.Json;
using LamMotCham.Core.Tasks;
using LamMotCham.Windows.Probe;
using Microsoft.Win32;

namespace LamMotCham.Windows.Checks;

/// <summary>Task kiểm tra chỉ đọc: đọc thông tin máy (dùng chung một lần thu thập) rồi đánh giá.</summary>
public sealed class InventoryCheckTask : ReadOnlyCheckTask
{
    private readonly InventoryProvider _provider;
    private readonly Func<InventorySnapshot, VerifyResult> _evaluate;

    public InventoryCheckTask(string id, string name, InventoryProvider provider, Func<InventorySnapshot, VerifyResult> evaluate, PrivilegeLevel privilege = PrivilegeLevel.User)
    {
        _provider = provider;
        _evaluate = evaluate;
        Descriptor = new TaskDescriptor
        {
            Id = id,
            DisplayName = name,
            Version = 1,
            Risk = RiskLevel.ReadOnly,
            Privilege = privilege,
            Kind = TaskKind.Check,
            Timeout = TimeSpan.FromMinutes(4),
            Rollback = RollbackSupport.NotApplicable,
        };
    }

    public override TaskDescriptor Descriptor { get; }

    public override async Task<VerifyResult> VerifyAsync(TaskContext context, CancellationToken cancellationToken)
    {
        var snapshot = await _provider.GetAsync(cancellationToken).ConfigureAwait(false);
        return _evaluate(snapshot);
    }
}

/// <summary>
/// Các quy tắc đánh giá. Không có dữ liệu thì trả "chưa xác định" chứ không cho "Đạt"
/// (UI/UX Handoff mục 12). Thiết bị cần nghe/nhìn/chạm thử thì trả "cần kiểm tra vật lý".
/// </summary>
public static class InventoryChecks
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    private static readonly string[] TrialPatterns = ["McAfee", "Norton", "Avast", "AVG ", "Avira", "Trend Micro", "WildTangent", "ExpressVPN"];

    public static VerifyResult WindowsVersion(InventorySnapshot s)
    {
        if (s.Num("osBuild") is not { } build)
        {
            return VerifyResult.Inconclusive("Không đọc được phiên bản Windows.");
        }

        var text = $"{s.Str("osCaption")} {s.Str("osDisplayVersion")} · build {build}.{s.Num("osUbr")}";
        return build >= 22000
            ? VerifyResult.Passed(text)
            : VerifyResult.Attention(text + ". Windows 10 đã hết hỗ trợ thông thường từ 14/10/2025.");
    }

    public static VerifyResult Activation(InventorySnapshot s)
    {
        if (s.ErrorFor("activation") is { } err)
        {
            return VerifyResult.Inconclusive("Không đọc được trạng thái kích hoạt: " + err);
        }

        if (s.Obj("activation") is not { } a)
        {
            return VerifyResult.Attention("Không thấy giấy phép Windows trên máy.");
        }

        var status = InventorySnapshot.Num(a, "status");
        return status == 1
            ? VerifyResult.Passed($"{InventorySnapshot.Str(a, "name")?.Split(',')[0]} · đã kích hoạt.")
            : VerifyResult.Attention($"Windows chưa được kích hoạt (trạng thái {status}).");
    }

    public static VerifyResult WindowsUpdate(InventorySnapshot s)
    {
        if (s.Obj("update") is not { } u)
        {
            return VerifyResult.Inconclusive("Không đọc được trạng thái Windows Update.");
        }

        var problems = new List<string>();
        if (string.Equals(InventorySnapshot.Str(u, "startType"), "Disabled", StringComparison.OrdinalIgnoreCase))
        {
            problems.Add("dịch vụ Windows Update đang bị tắt");
        }

        var last = InventorySnapshot.Date(u, "lastSuccess") ?? InventorySnapshot.Date(u, "lastHotfix");
        var reboot = s.Array("pendingReboot");
        if (reboot.Count > 0)
        {
            problems.Add("đang chờ khởi động lại để hoàn tất cập nhật");
        }

        var lastText = last is { } d ? $"cài bản cập nhật gần nhất {(DateTimeOffset.UtcNow - d).Days} ngày trước" : "chưa rõ lần cập nhật gần nhất";
        if (last is { } l && DateTimeOffset.UtcNow - l > TimeSpan.FromDays(45))
        {
            problems.Add("đã hơn 45 ngày chưa cập nhật");
        }

        return problems.Count == 0
            ? VerifyResult.Passed("Đang bật · " + lastText + ".")
            : VerifyResult.Attention(Capitalize(string.Join("; ", problems)) + " · " + lastText + ".");
    }

    public static VerifyResult Security(InventorySnapshot s)
    {
        foreach (var av in s.Array("antivirus"))
        {
            var name = InventorySnapshot.Str(av, "name") ?? "";
            var state = InventorySnapshot.Num(av, "state") ?? 0;
            var enabled = ((state >> 12) & 0xF) == 1;
            if (enabled && !name.Contains("Defender", StringComparison.OrdinalIgnoreCase))
            {
                return VerifyResult.Passed($"Đang bảo vệ bởi {name}.");
            }
        }

        if (s.Obj("defender") is not { } d)
        {
            return VerifyResult.Inconclusive("Không đọc được trạng thái Microsoft Defender.");
        }

        return InventorySnapshot.Bool(d, "realTime") == true && InventorySnapshot.Bool(d, "antivirus") == true
            ? VerifyResult.Passed("Microsoft Defender đang bật bảo vệ thời gian thực.")
            : VerifyResult.Attention("Bảo vệ thời gian thực đang tắt. Bật lại trong Windows Security.");
    }

    public static VerifyResult Firewall(InventorySnapshot s)
    {
        var profiles = s.Array("firewall");
        if (profiles.Count == 0)
        {
            return VerifyResult.Inconclusive("Không đọc được trạng thái tường lửa.");
        }

        var off = profiles.Where(p => InventorySnapshot.Bool(p, "enabled") != true).Select(p => InventorySnapshot.Str(p, "name")).ToList();
        return off.Count == 0
            ? VerifyResult.Passed($"Tường lửa bật ở {profiles.Count}/{profiles.Count} cấu hình.")
            : VerifyResult.Attention("Tường lửa đang tắt ở: " + string.Join(", ", off) + ".");
    }

    public static VerifyResult Wifi(InventorySnapshot s)
    {
        var wifi = s.Array("adapters").Where(a => (InventorySnapshot.Str(a, "media") ?? "").Contains("802.11", StringComparison.Ordinal)).ToList();
        if (wifi.Count == 0)
        {
            return s.ErrorFor("network") is null
                ? VerifyResult.Attention("Không thấy card Wi-Fi.")
                : VerifyResult.Inconclusive("Không đọc được danh sách card mạng.");
        }

        var a = wifi[0];
        var up = string.Equals(InventorySnapshot.Str(a, "status"), "Up", StringComparison.OrdinalIgnoreCase);
        return VerifyResult.Passed($"{InventorySnapshot.Str(a, "desc")} · {(up ? "đã kết nối" : "chưa kết nối")}.");
    }

    public static VerifyResult Bluetooth(InventorySnapshot s) => DeviceClass(s, "Bluetooth", "Bluetooth", manual: false);

    public static VerifyResult Camera(InventorySnapshot s)
    {
        var r = DeviceClass(s, "Camera", "camera", manual: true);
        return r.Outcome == VerifyOutcome.Attention && r.Detail?.StartsWith("Không thấy", StringComparison.Ordinal) == true
            ? DeviceClass(s, "Image", "camera", manual: true)
            : r;
    }

    public static VerifyResult Speaker(InventorySnapshot s)
    {
        var active = CountActiveEndpoints("Render");
        if (active is null)
        {
            return VerifyResult.Inconclusive("Không đọc được thiết bị âm thanh.");
        }

        var name = s.Array("sound").Select(x => InventorySnapshot.Str(x, "name")).FirstOrDefault(n => n is not null);
        return active > 0
            ? VerifyResult.RequiresPhysicalCheck($"Thấy {name ?? "thiết bị âm thanh"} ({active} đầu ra đang bật). Cần nghe thử.")
            : VerifyResult.Attention("Không có thiết bị phát âm thanh nào đang bật.");
    }

    public static VerifyResult Microphone(InventorySnapshot s)
    {
        var active = CountActiveEndpoints("Capture");
        if (active is null)
        {
            return VerifyResult.Inconclusive("Không đọc được thiết bị ghi âm.");
        }

        return active > 0
            ? VerifyResult.RequiresPhysicalCheck($"Thấy {active} micro đang bật. Cần nói thử và nghe lại.")
            : VerifyResult.Attention("Không có micro nào đang bật.");
    }

    public static VerifyResult Keyboard(InventorySnapshot s)
    {
        var kb = s.Array("keyboards");
        return kb.Count > 0
            ? VerifyResult.RequiresPhysicalCheck("Thấy bàn phím. Cần gõ thử tất cả các hàng phím.")
            : VerifyResult.Attention("Không thấy bàn phím.");
    }

    public static VerifyResult Touchpad(InventorySnapshot s)
    {
        var p = s.Array("pointing");
        return p.Count > 0
            ? VerifyResult.RequiresPhysicalCheck($"Thấy {InventorySnapshot.Str(p[0], "name")}. Cần di, nhấp và cuộn hai ngón.")
            : VerifyResult.Attention("Không thấy touchpad hoặc chuột.");
    }

    public static VerifyResult Ports(InventorySnapshot s) =>
        VerifyResult.RequiresPhysicalCheck("Ứng dụng không tự kiểm tra được cổng. Cần cắm thử USB, sạc và HDMI.");

    public static VerifyResult Drivers(InventorySnapshot s)
    {
        if (s.ErrorFor("devices") is not null)
        {
            return VerifyResult.Inconclusive("Không đọc được danh sách thiết bị.");
        }

        var bad = s.Array("problemDevices");
        if (bad.Count == 0)
        {
            return VerifyResult.Passed("Không có thiết bị nào báo lỗi.");
        }

        var names = bad.Take(3).Select(d => $"{InventorySnapshot.Str(d, "name") ?? "Thiết bị không tên"} (mã {InventorySnapshot.Num(d, "err")})");
        return VerifyResult.Attention($"{bad.Count} thiết bị báo lỗi: " + string.Join("; ", names) + ".");
    }

    public static VerifyResult Battery(InventorySnapshot s)
    {
        var bats = s.Array("battery");
        if (bats.Count == 0)
        {
            return s.ErrorFor("battery") is null
                ? VerifyResult.NotSupported("Máy không có pin.")
                : VerifyResult.Inconclusive("Không đọc được thông tin pin.");
        }

        var charge = InventorySnapshot.Num(bats[0], "charge");
        var text = $"Đang ở {charge}%" + (InventorySnapshot.Num(bats[0], "status") == 2 ? " · đang cắm sạc" : "");
        if (s.IsQuick)
        {
            return VerifyResult.Passed(text + ". Độ chai pin đọc khi nạp xong dữ liệu kiểm tra.");
        }

        if (s.Obj("batteryCapacity") is { } cap &&
            InventorySnapshot.Num(cap, "design") is { } design && design > 0 &&
            InventorySnapshot.Num(cap, "full") is { } full)
        {
            var health = full * 100.0 / design;
            text = string.Create(Vi, $"Sức khỏe {health:0}% · {text}");
            return health < 70
                ? VerifyResult.Attention(text + ". Pin đã chai đáng kể.")
                : VerifyResult.Passed(text + ".");
        }

        return VerifyResult.Passed(text + ". Không đọc được độ chai (cần quyền quản trị).");
    }

    public static VerifyResult Brightness(InventorySnapshot s) =>
        s.Array("brightness").Count > 0
            ? VerifyResult.RequiresPhysicalCheck("Màn hình hỗ trợ chỉnh độ sáng. Cần thử phím độ sáng.")
            : VerifyResult.NotSupported("Màn hình không báo hỗ trợ chỉnh độ sáng qua Windows.");

    public static VerifyResult BitLocker(InventorySnapshot s)
    {
        if (s.ErrorFor("bitlocker") is not null)
        {
            return VerifyResult.Inconclusive("Cần quyền quản trị để đọc trạng thái BitLocker.");
        }

        var system = s.Str("systemDrive") ?? "C:";
        var vol = s.Array("bitlocker").FirstOrDefault(v => string.Equals(InventorySnapshot.Str(v, "drive"), system, StringComparison.OrdinalIgnoreCase));
        if (vol.ValueKind == JsonValueKind.Undefined)
        {
            return VerifyResult.Inconclusive("Không thấy thông tin mã hóa của ổ hệ thống.");
        }

        return InventorySnapshot.Num(vol, "protection") switch
        {
            1 => VerifyResult.Attention($"BitLocker đang bật trên {system}. Bảo đảm khách hàng biết nơi lấy khóa khôi phục."),
            0 => VerifyResult.Passed($"Ổ {system} không mã hóa."),
            _ => VerifyResult.Inconclusive("Trạng thái mã hóa chưa xác định."),
        };
    }

    public static VerifyResult DiskHealth(InventorySnapshot s)
    {
        var system = s.Str("systemDrive") ?? "C:";
        var vol = s.Array("volumes").FirstOrDefault(v => string.Equals(InventorySnapshot.Str(v, "drive"), system, StringComparison.OrdinalIgnoreCase));
        if (vol.ValueKind == JsonValueKind.Undefined)
        {
            return VerifyResult.Inconclusive("Không đọc được ổ hệ thống.");
        }

        var free = InventorySnapshot.Num(vol, "free") ?? 0;
        var size = InventorySnapshot.Num(vol, "size") ?? 0;
        var unhealthy = s.Array("physicalDisks").Where(d => !string.Equals(InventorySnapshot.Str(d, "health"), "Healthy", StringComparison.OrdinalIgnoreCase)).ToList();
        var text = $"{system} còn trống {Cleanup.CleanupTask.FormatBytes(free)} / {Cleanup.CleanupTask.FormatBytes(size)}";

        if (unhealthy.Count > 0)
        {
            return VerifyResult.Attention(text + ". Ổ báo không khỏe: " + string.Join(", ", unhealthy.Select(d => InventorySnapshot.Str(d, "name"))) + ".");
        }

        return free < 20L * (1L << 30) || (size > 0 && free * 10 < size)
            ? VerifyResult.Attention(text + ". Dung lượng trống thấp.")
            : VerifyResult.Passed(text + ".");
    }

    public static VerifyResult StartupApps(InventorySnapshot s)
    {
        if (s.ErrorFor("startup") is not null)
        {
            return VerifyResult.Inconclusive("Không đọc được danh sách ứng dụng khởi động.");
        }

        var items = s.Array("startup").Select(x => InventorySnapshot.Str(x, "name")).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().ToList();
        return VerifyResult.Passed(items.Count == 0
            ? "Không có ứng dụng khởi động cùng Windows."
            : $"{items.Count} ứng dụng: " + string.Join(", ", items.Take(8)) + (items.Count > 8 ? "…" : "") + ".");
    }

    public static VerifyResult TrialSoftware(InventorySnapshot s)
    {
        var found = s.Array("apps")
            .Select(a => InventorySnapshot.Str(a, "name") ?? "")
            .Where(n => TrialPatterns.Any(p => n.Contains(p, StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return found.Count == 0
            ? VerifyResult.Passed("Không phát hiện phần mềm dùng thử phổ biến.")
            : VerifyResult.Attention("Phát hiện: " + string.Join(", ", found) + ". Gỡ trong Cài đặt › Ứng dụng hoặc bằng công cụ gỡ chính thức của hãng.");
    }

    /// <summary>Đếm thiết bị âm thanh đang bật qua registry MMDevices (không phụ thuộc ngôn ngữ Windows).</summary>
    private static int? CountActiveEndpoints(string flow)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\{flow}", writable: false);
            if (key is null)
            {
                return 0;
            }

            var count = 0;
            foreach (var name in key.GetSubKeyNames())
            {
                using var dev = key.OpenSubKey(name, writable: false);
                if (dev?.GetValue("DeviceState") is int state && state == 1)
                {
                    count++;
                }
            }

            return count;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    private static VerifyResult DeviceClass(InventorySnapshot s, string cls, string label, bool manual)
    {
        if (s.ErrorFor("devices") is not null)
        {
            return VerifyResult.Inconclusive($"Không đọc được danh sách thiết bị {label}.");
        }

        var devices = s.Array("devices").Where(d => string.Equals(InventorySnapshot.Str(d, "cls"), cls, StringComparison.OrdinalIgnoreCase)).ToList();
        if (devices.Count == 0)
        {
            return VerifyResult.Attention($"Không thấy thiết bị {label}.");
        }

        var broken = devices.FirstOrDefault(d => InventorySnapshot.Num(d, "err") is not (null or 0));
        if (broken.ValueKind != JsonValueKind.Undefined)
        {
            return VerifyResult.Attention($"{InventorySnapshot.Str(broken, "name")} báo lỗi (mã {InventorySnapshot.Num(broken, "err")}).");
        }

        var name = InventorySnapshot.Str(devices[0], "name");
        return manual
            ? VerifyResult.RequiresPhysicalCheck($"Thấy {name}. Cần mở thử để xác nhận.")
            : VerifyResult.Passed($"Đã phát hiện {name}, không báo lỗi.");
    }

    private static string Capitalize(string text) =>
        text.Length == 0 ? text : char.ToUpper(text[0], Vi) + text[1..];
}
