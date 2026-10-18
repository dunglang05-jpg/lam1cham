using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using LamMotCham.Windows.Native;
using Microsoft.Win32;

namespace LamMotCham.Windows.Probe;

/// <summary>
/// Đọc nhanh cấu hình máy và pin bằng API Windows (registry, bộ nhớ, ổ đĩa, nguồn điện) — không chạy PowerShell,
/// thường dưới 100 ms. Dùng khi mở ứng dụng; dữ liệu kiểm tra đầy đủ được <see cref="InventoryProvider"/> nạp sau.
/// Trả về <see cref="InventorySnapshot"/> cùng tên trường với bản đầy đủ, đánh dấu <c>source = quick</c>.
/// </summary>
public static class QuickProbe
{
    public const string SourceQuick = "quick";

    /// <summary>Cấu hình: tên máy, hãng/model, CPU, RAM, Windows, ổ đĩa.</summary>
    public static JsonObject ReadSystem()
    {
        var o = new JsonObject
        {
            ["source"] = SourceQuick,
            ["computerName"] = Environment.MachineName,
            ["systemDrive"] = Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\') ?? "C:",
        };
        var errors = new JsonArray();

        try
        {
            using var bios = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
            o["manufacturer"] = (bios?.GetValue("SystemManufacturer") as string)?.Trim();
            o["model"] = (bios?.GetValue("SystemProductName") as string)?.Trim();
            using var cpu = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            o["cpu"] = (cpu?.GetValue("ProcessorNameString") as string)?.Trim();

            using var cv = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            var build = int.TryParse(cv?.GetValue("CurrentBuild") as string, NumberStyles.Integer, CultureInfo.InvariantCulture, out var b) ? b : 0;
            var product = cv?.GetValue("ProductName") as string ?? "Windows";
            if (build >= 22000)
            {
                // ProductName trong registry vẫn ghi "Windows 10" trên Windows 11.
                product = product.Replace("Windows 10", "Windows 11", StringComparison.Ordinal);
            }

            o["osCaption"] = "Microsoft " + product;
            o["osBuild"] = build;
            o["osDisplayVersion"] = cv?.GetValue("DisplayVersion") as string;
            o["osUbr"] = cv?.GetValue("UBR") is int ubr ? ubr : 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
            errors.Add("system: " + ex.Message);
        }

        var mem = new NativeMethods.MemoryStatusEx { Length = (uint)Marshal.SizeOf<NativeMethods.MemoryStatusEx>() };
        if (NativeMethods.GlobalMemoryStatusEx(ref mem))
        {
            o["totalRamBytes"] = (long)mem.TotalPhys;
        }

        var volumes = new JsonArray();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (d.DriveType != DriveType.Fixed || !d.IsReady)
                {
                    continue;
                }

                volumes.Add(new JsonObject
                {
                    ["drive"] = d.Name.TrimEnd('\\'),
                    ["size"] = d.TotalSize,
                    ["free"] = d.AvailableFreeSpace,
                    ["fileSystem"] = d.DriveFormat,
                    ["label"] = d.VolumeLabel,
                });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add($"disks: {d.Name} {ex.Message}");
            }
        }

        o["volumes"] = volumes;
        o["errors"] = errors;
        return o;
    }

    /// <summary>Pin: % còn lại và đang cắm sạc hay không (GetSystemPowerStatus). Độ chai đọc ở bản đầy đủ.</summary>
    public static void ReadBattery(JsonObject target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var batteries = new JsonArray();
        if (NativeMethods.GetSystemPowerStatus(out var status))
        {
            // 128 = không có pin, 255 = không xác định.
            if (status.BatteryFlag != 128 && status.BatteryFlag != 255 && status.BatteryLifePercent <= 100)
            {
                batteries.Add(new JsonObject
                {
                    ["name"] = "",
                    ["charge"] = (int)status.BatteryLifePercent,
                    ["status"] = status.AcLineStatus == 1 ? 2 : 1,
                });
            }
            else if (status.BatteryFlag == 255 && target["errors"] is JsonArray errs)
            {
                errs.Add("battery: Windows không xác định được trạng thái pin.");
            }
        }

        target["battery"] = batteries;
    }

    /// <summary>Đọc cả cấu hình và pin trong một lần.</summary>
    public static InventorySnapshot Read()
    {
        var o = ReadSystem();
        ReadBattery(o);
        return ToSnapshot(o);
    }

    public static InventorySnapshot ToSnapshot(JsonObject data)
    {
        ArgumentNullException.ThrowIfNull(data);
        using var doc = JsonDocument.Parse(data.ToJsonString());
        return new InventorySnapshot(doc.RootElement, DateTimeOffset.UtcNow);
    }
}
