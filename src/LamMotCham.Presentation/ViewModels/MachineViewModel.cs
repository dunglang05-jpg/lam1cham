using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using LamMotCham.Presentation.Mvvm;
using LamMotCham.Core.Logging;
using LamMotCham.Core.Tasks;
using LamMotCham.Windows.Cleanup;
using LamMotCham.Windows.Probe;
using LamMotCham.Windows.Resources;
using LamMotCham.Windows.Software;

namespace LamMotCham.Presentation.ViewModels;

public sealed record InfoRow(string Label, string Value);

public sealed class MetricViewModel(string iconKey, string label) : ObservableObject
{
    private string _value = "—";
    private string _unit = "";
    private string _sub = "Chưa đọc";
    private double _percent;
    private string _time = "Chưa kiểm tra";

    public string IconKey { get; } = iconKey;

    public string Label { get; } = label;

    public string Value { get => _value; set => Set(ref _value, value); }

    public string Unit { get => _unit; set => Set(ref _unit, value); }

    public string Sub { get => _sub; set => Set(ref _sub, value); }

    public double Percent { get => _percent; set => Set(ref _percent, value); }

    public string Time { get => _time; set => Set(ref _time, value); }
}

/// <summary>Thông tin máy và tài nguyên. Không giả định đã kiểm tra khi chưa đọc được dữ liệu.</summary>
public sealed class MachineViewModel : ObservableObject
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    private readonly MainViewModel _main;
    private InventorySnapshot? _snapshot;
    private bool _isChecking;
    private bool _checked;
    private string _checkedAt = "";
    private string? _winGetVersion;
    private bool _winGetChecked;
    private int _tick;

    public MachineViewModel(MainViewModel main)
    {
        _main = main;
        CheckCommand = new AsyncCommand(CheckAsync, () => !_isChecking && !main.IsRunning);
    }

    public AsyncCommand CheckCommand { get; }

    public InventorySnapshot? Snapshot
    {
        get => _snapshot;
        private set => Set(ref _snapshot, value);
    }

    public bool IsChecking
    {
        get => _isChecking;
        private set
        {
            if (Set(ref _isChecking, value))
            {
                OnPropertyChanged(nameof(CheckButtonText));
            }
        }
    }

    public bool Checked
    {
        get => _checked;
        private set
        {
            if (Set(ref _checked, value))
            {
                OnPropertyChanged(nameof(Unchecked));
                OnPropertyChanged(nameof(CheckButtonText));
            }
        }
    }

    public bool Unchecked => !_checked;

    public string CheckButtonText => _isChecking ? "Đang kiểm tra…" : (_checked ? "Kiểm tra lại" : "Kiểm tra máy");

    public string CheckedAt
    {
        get => _checkedAt;
        private set => Set(ref _checkedAt, value);
    }

    public bool WinGetAvailable => _winGetVersion is not null;

    public bool WinGetMissing => _winGetChecked && _winGetVersion is null;

    public string? WinGetVersion => _winGetVersion;

    public ObservableCollection<InfoRow> Status { get; } = [];

    public MetricViewModel Ram { get; } = new("memory", "RAM còn trống");

    public MetricViewModel SystemDisk { get; } = new("disk", "Ổ hệ thống còn trống");

    public MetricViewModel Cpu { get; } = new("cpu", "CPU đang dùng");

    public string ComputerName => _snapshot?.Str("computerName") ?? Environment.MachineName;

    public string WindowsShort =>
        _snapshot?.Str("osCaption")?.Replace("Microsoft ", "", StringComparison.Ordinal) is { } c
            ? c + (_snapshot.Str("osDisplayVersion") is { Length: > 0 } dv ? " " + dv : "")
            : "Windows";

    public async Task CheckAsync()
    {
        if (IsChecking || _main.IsRunning)
        {
            return;
        }

        IsChecking = true;
        try
        {
            _main.S.Inventory.Invalidate();
            Snapshot = await Task.Run(() => _main.S.Inventory.GetAsync(CancellationToken.None));
            _winGetVersion = await Task.Run(() => _main.S.WinGet.GetVersionAsync(CancellationToken.None));
            _winGetChecked = true;
            OnPropertyChanged(nameof(WinGetAvailable));
            OnPropertyChanged(nameof(WinGetMissing));
            OnPropertyChanged(nameof(WinGetVersion));
            OnPropertyChanged(nameof(WindowsShort));
            _main.NotifyWindowsLabel();
            CheckedAt = DateTimeOffset.Now.ToString("HH:mm · dd/MM/yyyy", Vi);
            Checked = true;
            BuildStatus();
            RefreshResources(force: true);
            foreach (var err in Snapshot.Errors)
            {
                _main.S.Log.Warning("Thu thập thông tin: " + err);
            }
        }
        finally
        {
            IsChecking = false;
        }

        // Chạy toàn bộ kiểm tra chỉ đọc (dùng chung dữ liệu vừa thu thập) và đọc trạng thái hiện tại.
        var checks = _main.Rows.Values.Where(r => r.IsReadOnly).ToList();
        await _main.RunAsync(checks, "Kiểm tra máy");
        await RefreshCurrentStatesAsync();
    }

    public async Task RefreshCurrentStatesAsync()
    {
        foreach (var row in _main.Rows.Values)
        {
            if (row.Entry.Module is not IDescribesCurrentState describer)
            {
                continue;
            }

            if (row.Entry.Module is WinGetInstallTask && !WinGetAvailable)
            {
                row.CurrentText = "Chưa kiểm tra";
                continue;
            }

            try
            {
                row.CurrentText = await Task.Run(() => describer.DescribeCurrentStateAsync(CancellationToken.None));
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _main.S.Log.Warning($"Không đọc được trạng thái hiện tại của {row.Id}: {ex.Message}");
                row.CurrentText = "Chưa kiểm tra";
            }
        }
    }

    public void RefreshResources(bool force = false)
    {
        _tick++;
        var resources = _main.S.Monitor.Read();
        var now = DateTimeOffset.Now.ToString("HH:mm:ss", Vi);
        if (WindowsResourceMonitor.ReadMemory() is { } mem)
        {
            Ram.Value = (mem.AvailableBytes / (double)(1L << 30)).ToString("0.0", Vi);
            Ram.Unit = "GB";
            Ram.Sub = $"trên tổng {(mem.TotalBytes / (double)(1L << 30)).ToString("0", Vi)} GB";
            Ram.Percent = 100 - mem.AvailableBytes * 100.0 / mem.TotalBytes;
            Ram.Time = "Cập nhật " + now;
        }

        if (resources.CpuPercent is { } cpu)
        {
            Cpu.Value = cpu.ToString("0", Vi);
            Cpu.Unit = "%";
            Cpu.Sub = _snapshot?.Str("cpu") ?? Environment.ProcessorCount + " luồng xử lý";
            Cpu.Percent = cpu;
            Cpu.Time = "Trung bình giữa hai lần đọc";
        }

        if (force || _tick % 10 == 0)
        {
            try
            {
                var drive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\");
                SystemDisk.Value = (drive.AvailableFreeSpace / (double)(1L << 30)).ToString("0", Vi);
                SystemDisk.Unit = "GB";
                SystemDisk.Sub = $"{drive.Name.TrimEnd('\\')} · trên tổng {CleanupTask.FormatBytes(drive.TotalSize)}";
                SystemDisk.Percent = 100 - drive.AvailableFreeSpace * 100.0 / drive.TotalSize;
                SystemDisk.Time = "Cập nhật " + now;
            }
            catch (IOException)
            {
                SystemDisk.Sub = "Không đọc được";
            }
        }
    }

    /// <summary>Thông tin không nhạy cảm cho báo cáo (UI/UX Handoff mục 12).</summary>
    public IReadOnlyList<KeyValuePair<string, string>> ReportFacts()
    {
        var s = _snapshot;
        var list = new List<KeyValuePair<string, string>>
        {
            new("Tên máy", Environment.MachineName),
            new("Hãng / model", s is null ? "Chưa đọc" : $"{s.Str("manufacturer")} {s.Str("model")}".Trim()),
            new("Số serial", s?.Str("serialNumber") ?? "Chưa đọc"),
            new("CPU", s?.Str("cpu") ?? "Chưa đọc"),
            new("RAM", s?.Num("totalRamBytes") is { } ram ? CleanupTask.FormatBytes(ram) : "Chưa đọc"),
            new("Windows", s is null ? "Chưa đọc" : $"{s.Str("osCaption")} {s.Str("osDisplayVersion")} (build {s.Num("osBuild")}.{s.Num("osUbr")})"),
            new("WinGet", _winGetVersion ?? "Không khả dụng"),
            new("Quyền khi chạy", _main.S.IsElevated ? "Quản trị viên" : "Người dùng"),
        };
        return list;
    }

    private void BuildStatus()
    {
        Status.Clear();
        var s = _snapshot!;
        Status.Add(new InfoRow("Windows", $"{s.Str("osCaption")?.Replace("Microsoft ", "", StringComparison.Ordinal)} {s.Str("osDisplayVersion")} · build {s.Num("osBuild")}.{s.Num("osUbr")}"));
        Status.Add(new InfoRow("Kiến trúc", RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()));
        Status.Add(new InfoRow("Quyền", _main.S.IsElevated ? "Quản trị viên" : "Người dùng — cần nâng quyền khi cài"));
        Status.Add(new InfoRow("Mạng", NetworkInterface.GetIsNetworkAvailable() ? "Đã kết nối" : "Không có kết nối"));
        Status.Add(new InfoRow("WinGet", _winGetVersion is null ? "Không khả dụng" : _winGetVersion + " · sẵn sàng"));
        Status.Add(new InfoRow("Máy", $"{s.Str("manufacturer")} {s.Str("model")}".Trim()));
    }
}
