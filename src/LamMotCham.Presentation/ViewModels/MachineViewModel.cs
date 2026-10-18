using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using LamMotCham.Presentation.Mvvm;
using LamMotCham.Core.Logging;
using LamMotCham.Core.Tasks;
using LamMotCham.Windows.Checks;
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

/// <summary>Một bước trên màn hình khởi động (trạng thái thật của từng bước nạp).</summary>
public sealed class StartupStep(string title) : ObservableObject
{
    private string _stateKey = "pending";
    private string _detail = "";

    public string Title { get; } = title;

    /// <summary>pending, running, success, attention.</summary>
    public string StateKey { get => _stateKey; set => Set(ref _stateKey, value); }

    public string Detail { get => _detail; set => Set(ref _detail, value); }
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
    private bool _isStarting = true;
    private double _startupPercent;
    private string _startupText = "Đang khởi động…";
    private bool _inventoryLoading;
    private string _inventoryStatus = "";

    public MachineViewModel(MainViewModel main)
    {
        _main = main;
        CheckCommand = new AsyncCommand(CheckAsync, () => !_isChecking && !_isStarting && !main.IsRunning);
        StartupSteps =
        [
            new("Nạp giao diện và mô-đun"),
            new("Đọc cấu hình máy"),
            new("Đọc pin và nguồn điện"),
            new("Kiểm tra kết nối mạng"),
            new("Kiểm tra WinGet"),
        ];
    }

    /// <summary>Đang hiện màn hình khởi động.</summary>
    public bool IsStarting
    {
        get => _isStarting;
        private set => Set(ref _isStarting, value);
    }

    public ObservableCollection<StartupStep> StartupSteps { get; }

    public double StartupPercent
    {
        get => _startupPercent;
        private set => Set(ref _startupPercent, value);
    }

    public string StartupText
    {
        get => _startupText;
        private set => Set(ref _startupText, value);
    }

    /// <summary>Đang nạp dữ liệu kiểm tra đầy đủ chạy nền.</summary>
    public bool InventoryLoading
    {
        get => _inventoryLoading;
        private set => Set(ref _inventoryLoading, value);
    }

    /// <summary>Trạng thái dữ liệu kiểm tra nền, ví dụ "Đang nạp dữ liệu kiểm tra 2/4…".</summary>
    public string InventoryStatus
    {
        get => _inventoryStatus;
        private set => Set(ref _inventoryStatus, value);
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

    public string CheckButtonText => _isChecking ? "Đang đọc…" : (_checked ? "Đọc lại" : "Đọc thông tin máy");

    public string CheckedAt
    {
        get => _checkedAt;
        private set => Set(ref _checkedAt, value);
    }

    public bool WinGetAvailable => _winGetVersion is not null;

    public bool WinGetMissing => _winGetChecked && _winGetVersion is null;

    public string? WinGetVersion => _winGetVersion;

    public ObservableCollection<InfoRow> Status { get; } = [];

    /// <summary>Thông số hiển thị dạng dải trên thẻ chào (Tổng quan): mọi dòng Status trừ "Máy" và "Quyền".</summary>
    public ObservableCollection<InfoRow> Specs { get; } = [];

    /// <summary>Hãng và model máy, hoặc lời nhắc khi chưa đọc.</summary>
    public string ModelText => Status.FirstOrDefault(r => r.Label == "Máy")?.Value ?? "Chưa đọc thông tin máy";

    public MetricViewModel Ram { get; } = new("memory", "RAM còn trống");

    public MetricViewModel SystemDisk { get; } = new("disk", "Ổ hệ thống còn trống");

    public MetricViewModel Cpu { get; } = new("cpu", "CPU đang dùng");

    public string ComputerName => _snapshot?.Str("computerName") ?? Environment.MachineName;

    public string WindowsShort =>
        _snapshot?.Str("osCaption")?.Replace("Microsoft ", "", StringComparison.Ordinal) is { } c
            ? c + (_snapshot.Str("osDisplayVersion") is { Length: > 0 } dv ? " " + dv : "")
            : "Windows";

    /// <summary>
    /// Màn hình khởi động: đọc nhanh cấu hình và pin bằng API Windows (không PowerShell), kiểm tra mạng và WinGet song song,
    /// rồi nạp dữ liệu kiểm tra đầy đủ chạy nền. Mỗi bước hiện trạng thái thật.
    /// </summary>
    public async Task StartupAsync()
    {
        var started = DateTimeOffset.Now;
        IsStarting = true;
        try
        {
            Step(0, "success", "Sẵn sàng");
            Step(1, "running", "");
            StartupText = "Đang đọc cấu hình máy…";

            // WinGet chậm nhất: bắt đầu song song ngay.
            var winget = Task.Run(() => ReadWinGetAsync());

            var data = await Task.Run(QuickProbe.ReadSystem);
            Snapshot = QuickProbe.ToSnapshot(data);
            Step(1, "success", $"{Snapshot.Str("cpu") ?? "CPU"} · {(Snapshot.Num("totalRamBytes") is { } ram ? CleanupTask.FormatBytes(ram) : "RAM?")}");

            Step(2, "running", "");
            StartupText = "Đang đọc pin…";
            await Task.Run(() => QuickProbe.ReadBattery(data));
            Snapshot = QuickProbe.ToSnapshot(data);
            Step(2, "success", InventoryChecks.Battery(Snapshot).Detail ?? "");

            Step(3, "running", "");
            var online = await Task.Run(NetworkInterface.GetIsNetworkAvailable);
            Step(3, online ? "success" : "attention", online ? "Đã kết nối" : "Không có kết nối — cài phần mềm sẽ cần mạng");

            Step(4, "running", "");
            StartupText = "Đang kiểm tra WinGet…";
            await winget;
            Step(4, WinGetAvailable ? "success" : "attention", WinGetAvailable ? _winGetVersion! : "Không khả dụng — cần App Installer để cài phần mềm");

            FinishRead();
            StartupText = $"Sẵn sàng sau {(DateTimeOffset.Now - started).TotalSeconds.ToString("0.0", Vi)} giây";
            _main.S.Log.Info($"Khởi động: đọc thông tin cơ bản mất {(DateTimeOffset.Now - started).TotalMilliseconds:0} ms.");
            await Task.Delay(350);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _main.S.Log.Error("Khởi động: " + ex.Message);
            StartupText = "Không đọc được một phần thông tin máy. Có thể bấm “Đọc lại” sau.";
            await Task.Delay(1200);
        }
        finally
        {
            IsStarting = false;
            Commands.InvalidateAll();
        }

        _ = LoadFullInventoryAsync();
    }

    /// <summary>Nút "Đọc lại": đọc nhanh cấu hình, pin, WinGet; sau đó nạp lại dữ liệu kiểm tra chạy nền.</summary>
    public async Task CheckAsync()
    {
        if (IsChecking || _main.IsRunning)
        {
            return;
        }

        IsChecking = true;
        try
        {
            var winget = Task.Run(() => ReadWinGetAsync());
            Snapshot = await Task.Run(QuickProbe.Read);
            await winget;
            FinishRead();
        }
        finally
        {
            IsChecking = false;
        }

        _main.S.Inventory.Invalidate();
        _ = LoadFullInventoryAsync();
    }

    /// <summary>
    /// Nạp dữ liệu kiểm tra đầy đủ (các nhóm PowerShell chạy song song) mà không chặn giao diện.
    /// Kiểm tra bàn giao và trang Ổ đĩa dùng lại dữ liệu này nếu còn mới.
    /// </summary>
    public async Task LoadFullInventoryAsync()
    {
        if (InventoryLoading)
        {
            return;
        }

        InventoryLoading = true;
        var total = InventoryProvider.GroupCount;
        InventoryStatus = $"Đang nạp dữ liệu kiểm tra 0/{total}…";
        var started = DateTimeOffset.Now;
        try
        {
            var progress = new Progress<int>(n => InventoryStatus = $"Đang nạp dữ liệu kiểm tra {n}/{total}…");
            var full = await Task.Run(() => _main.S.Inventory.GetAsync(progress, CancellationToken.None));
            Snapshot = full;
            BuildStatus();
            OnPropertyChanged(nameof(WindowsShort));
            _main.NotifyWindowsLabel();
            InventoryStatus = $"Dữ liệu kiểm tra sẵn sàng ({(DateTimeOffset.Now - started).TotalSeconds.ToString("0.0", Vi)} giây)";
            foreach (var err in full.Errors)
            {
                _main.S.Log.Warning("Thu thập thông tin: " + err);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            InventoryStatus = "Chưa nạp được dữ liệu kiểm tra — sẽ thử lại khi bấm Bắt đầu kiểm tra.";
            _main.S.Log.Warning("Nạp dữ liệu kiểm tra: " + ex.Message);
        }
        finally
        {
            InventoryLoading = false;
        }
    }

    private async Task ReadWinGetAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            _winGetVersion = await _main.S.WinGet.GetVersionAsync(cts.Token);
        }
        catch (Exception ex) when (ex is OperationCanceledException or InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            _winGetVersion = null;
            _main.S.Log.Warning("Không đọc được phiên bản WinGet: " + ex.Message);
        }

        _winGetChecked = true;
    }

    private void FinishRead()
    {
        OnPropertyChanged(nameof(WinGetAvailable));
        OnPropertyChanged(nameof(WinGetMissing));
        OnPropertyChanged(nameof(WinGetVersion));
        OnPropertyChanged(nameof(WindowsShort));
        _main.NotifyWindowsLabel();
        CheckedAt = DateTimeOffset.Now.ToString("HH:mm · dd/MM/yyyy", Vi);
        Checked = true;
        BuildStatus();
        RefreshResources(force: true);
    }

    private void Step(int index, string state, string detail)
    {
        StartupSteps[index].StateKey = state;
        StartupSteps[index].Detail = detail;
        var done = StartupSteps.Count(s => s.StateKey is "success" or "attention");
        StartupPercent = done * 100.0 / StartupSteps.Count;
    }

    /// <summary>Đọc trạng thái hiện tại của các mục; <paramref name="only"/> giới hạn theo nhóm.</summary>
    public async Task RefreshCurrentStatesAsync(TaskCategory? only = null)
    {
        foreach (var row in _main.Rows.Values)
        {
            if (only is { } category && row.Entry.Category != category)
            {
                continue;
            }

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
            Cpu.Sub = _snapshot?.Str("cpu") is { } name ? ShortCpu(name) : Environment.ProcessorCount + " luồng xử lý";
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
        Status.Add(new InfoRow("Máy", $"{s.Str("manufacturer")} {s.Str("model")}".Trim() is { Length: > 0 } model ? model : "Không đọc được"));
        Status.Add(new InfoRow("CPU", s.Str("cpu") is { } cpu ? ShortCpu(cpu) : "Không đọc được"));
        Status.Add(new InfoRow("RAM", s.Num("totalRamBytes") is { } ram ? CleanupTask.FormatBytes(ram) : "Không đọc được"));
        Status.Add(new InfoRow("Ổ hệ thống", SystemDriveText()));
        Status.Add(new InfoRow("Windows", $"{s.Str("osCaption")?.Replace("Microsoft ", "", StringComparison.Ordinal)} {s.Str("osDisplayVersion")} · {RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()}"));
        Status.Add(new InfoRow("Pin", InventoryChecks.Battery(s).Detail ?? "Không đọc được"));
        Status.Add(new InfoRow("WinGet", _winGetVersion is null ? "Không khả dụng" : _winGetVersion + " · sẵn sàng"));
        Status.Add(new InfoRow("Quyền", _main.S.IsElevated ? "Quản trị viên" : "Người dùng (cần nâng quyền khi cài)"));
        Status.Add(new InfoRow("Mạng", NetworkInterface.GetIsNetworkAvailable() ? "Đã kết nối" : "Không có kết nối"));
        Specs.Clear();
        foreach (var row in Status.Where(r => r.Label is not "Máy" and not "Quyền"))
        {
            Specs.Add(row);
        }

        OnPropertyChanged(nameof(ModelText));
    }

    /// <summary>Tên CPU gọn cho một dòng: bỏ (R), (TM), chữ "CPU" và khoảng trắng thừa.</summary>
    public static string ShortCpu(string cpu)
    {
        ArgumentNullException.ThrowIfNull(cpu);
        var t = cpu.Replace("(R)", "", StringComparison.OrdinalIgnoreCase)
            .Replace("(TM)", "", StringComparison.OrdinalIgnoreCase)
            .Replace(" CPU", "", StringComparison.Ordinal);
        return string.Join(' ', t.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string SystemDriveText()
    {
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\");
            return $"{drive.Name.TrimEnd('\\')} {CleanupTask.FormatBytes(drive.TotalSize)} (còn {CleanupTask.FormatBytes(drive.AvailableFreeSpace)})";
        }
        catch (IOException)
        {
            return "Không đọc được";
        }
    }
}
