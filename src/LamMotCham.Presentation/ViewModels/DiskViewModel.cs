using System.Collections.ObjectModel;
using LamMotCham.Presentation.Mvvm;
using LamMotCham.Presentation.Services;
using LamMotCham.Core.Logging;
using LamMotCham.Core.Tasks;
using LamMotCham.Windows;
using LamMotCham.Windows.Cleanup;
using LamMotCham.Windows.Config;
using LamMotCham.Windows.Disk;
using LamMotCham.Windows.Probe;

namespace LamMotCham.Presentation.ViewModels;

public sealed record DriveRow(string Letter, string Label, string Type, string Size, string Free, double UsedPercent, string HealthKey, string HealthText, string EncryptionKey, string EncryptionText);

/// <summary>Màn hình D — Ổ đĩa và tài khoản (UI/UX Handoff mục 10). Thay đổi: đổi tên máy, chia ổ (xem trước + xác nhận riêng).</summary>
public sealed class DiskViewModel : ObservableObject
{
    /// <summary>Mô-đun của trang này (QĐ-002).</summary>
    public LamMotCham.Presentation.Modules.AppModule Module { get; } = LamMotCham.Presentation.Modules.ModuleCatalog.Disk;

    private readonly MainViewModel _main;
    private string _newName = "";
    private string _encryptionKey = "unchecked";
    private string _encryptionText = "Chưa kiểm tra";
    private string _encryptionDetail = "Bấm “Bắt đầu kiểm tra” ở Kiểm tra & Bàn giao để đọc trạng thái mã hóa.";
    private string _splitSource = "C:";
    private string _splitSizeText = "100";
    private string _splitLabel = "DATA";
    private bool _previewing;
    private DiskSplitPlan? _plan;
    private string _planMessage = "Nhập dung lượng ổ mới rồi bấm “Xem trước”. Chưa có gì thay đổi trên máy.";

    public DiskViewModel(MainViewModel main)
    {
        _main = main;
        RenameCommand = new RelayCommand(AskRename, () => IsNameValid && !main.IsRunning);
        RecoveryHelpCommand = new RelayCommand(() => ShellActions.OpenUri("https://support.microsoft.com/windows/find-your-bitlocker-recovery-key-6b71ad27-0b89-ea08-f143-056f5ab347d6"));
        PreviewSplitCommand = new AsyncCommand(PreviewSplitAsync, () => !_previewing && !main.IsRunning);
        SplitCommand = new RelayCommand(AskSplit, () => _plan is { IsValid: true } && !_previewing && !main.IsRunning);
        OpenDiskManagementCommand = new RelayCommand(() => ShellActions.OpenUri("diskmgmt.msc"));
        main.Machine.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MachineViewModel.Snapshot))
            {
                Rebuild();
            }
        };
        if (main.Rows.TryGetValue("disk.bitlocker", out var bitlocker))
        {
            bitlocker.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(TaskRowViewModel.State) or nameof(TaskRowViewModel.Message))
                {
                    EncryptionKey = bitlocker.StateKey;
                    EncryptionText = bitlocker.State switch
                    {
                        TaskState.NeedsAttention => "BitLocker đang bật",
                        TaskState.Success => "Không mã hóa",
                        _ => "Chưa xác định",
                    };
                    EncryptionDetail = bitlocker.Message ?? "";
                }
            };
        }
    }

    public ObservableCollection<DriveRow> Drives { get; } = [];

    public ObservableCollection<InfoRow> Account { get; } = [];

    public RelayCommand RenameCommand { get; }

    public AsyncCommand PreviewSplitCommand { get; }

    public RelayCommand SplitCommand { get; }

    public RelayCommand OpenDiskManagementCommand { get; }

    /// <summary>Ổ NTFS có thể chia (ví dụ "C:").</summary>
    public ObservableCollection<string> SplitSources { get; } = ["C:"];

    public string SplitSource
    {
        get => _splitSource;
        set
        {
            if (Set(ref _splitSource, value ?? "C:"))
            {
                ClearPlan();
            }
        }
    }

    public string SplitSizeText
    {
        get => _splitSizeText;
        set
        {
            if (Set(ref _splitSizeText, (value ?? "").Trim()))
            {
                ClearPlan();
            }
        }
    }

    public string SplitLabel
    {
        get => _splitLabel;
        set
        {
            if (Set(ref _splitLabel, value ?? ""))
            {
                ClearPlan();
            }
        }
    }

    public bool Previewing
    {
        get => _previewing;
        private set
        {
            if (Set(ref _previewing, value))
            {
                OnPropertyChanged(nameof(PreviewLabel));
                Commands.InvalidateAll();
            }
        }
    }

    public string PreviewLabel => _previewing ? "Đang đọc phân vùng…" : "Xem trước";

    /// <summary>Các dòng xem trước (con số trước/sau khi chia).</summary>
    public ObservableCollection<InfoRow> PlanRows { get; } = [];

    public ObservableCollection<string> PlanErrors { get; } = [];

    public ObservableCollection<string> PlanWarnings { get; } = [];

    public bool HasPlan => _plan is not null;

    public bool PlanValid => _plan is { IsValid: true };

    public string PlanMessage
    {
        get => _planMessage;
        private set => Set(ref _planMessage, value);
    }

    public RelayCommand RecoveryHelpCommand { get; }

    public string CurrentName => _main.Machine.ComputerName;

    public string EncryptionKey { get => _encryptionKey; private set => Set(ref _encryptionKey, value); }

    public string EncryptionText { get => _encryptionText; private set => Set(ref _encryptionText, value); }

    public string EncryptionDetail { get => _encryptionDetail; private set => Set(ref _encryptionDetail, value); }

    public bool HasDrives => Drives.Count > 0;

    public string NewName
    {
        get => _newName;
        set
        {
            if (Set(ref _newName, (value ?? "").Trim()))
            {
                OnPropertyChanged(nameof(IsNameValid));
                OnPropertyChanged(nameof(NameHint));
                OnPropertyChanged(nameof(NameInvalid));
                Commands.InvalidateAll();
            }
        }
    }

    public bool IsNameValid => RenameComputerTask.IsValidName(_newName) && !string.Equals(_newName, CurrentName, StringComparison.OrdinalIgnoreCase);

    public bool NameInvalid => _newName.Length > 0 && !IsNameValid;

    public string NameHint => NameInvalid
        ? (string.Equals(_newName, CurrentName, StringComparison.OrdinalIgnoreCase) ? "Tên mới trùng tên hiện tại." : "Tên không hợp lệ: 1–15 ký tự, chỉ chữ không dấu, số và dấu gạch ngang; không toàn số.")
        : "1–15 ký tự: chữ không dấu, số, dấu gạch ngang. Cần khởi động lại để có hiệu lực.";

    private void AskRename()
    {
        var name = _newName.ToUpperInvariant();
        _main.ShowDialog(new ConfirmDialogViewModel(_main, "Đổi tên máy", "laptop", "Đổi tên", async () =>
        {
            var entry = WindowsCatalog.Rename(name);
            await _main.RunAsync([new TaskRowViewModel(entry) { IsSelected = true }], "Đổi tên máy");
        }, "Hủy")
        {
            Facts =
            [
                new DialogFact("Tên hiện tại", CurrentName),
                new DialogFact("Tên mới", name),
                new DialogFact("Quyền", "Cần quyền quản trị."),
                new DialogFact("Khởi động lại", "Tên mới có hiệu lực sau khi khởi động lại. Ứng dụng sẽ hỏi trước, không tự khởi động lại."),
            ],
        });
    }

    private void ClearPlan()
    {
        _plan = null;
        PlanRows.Clear();
        PlanErrors.Clear();
        PlanWarnings.Clear();
        PlanMessage = "Nhập dung lượng ổ mới rồi bấm “Xem trước”. Chưa có gì thay đổi trên máy.";
        OnPropertyChanged(nameof(HasPlan));
        OnPropertyChanged(nameof(PlanValid));
        Commands.InvalidateAll();
    }

    private async Task PreviewSplitAsync()
    {
        ClearPlan();
        if (!_main.IsElevated)
        {
            PlanMessage = "Cần quyền quản trị để đọc và chia phân vùng. Bấm “Nâng quyền quản trị…” ở thanh trên.";
            return;
        }

        var text = _splitSizeText.Replace(',', '.');
        if (!double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var gb) || gb <= 0)
        {
            PlanMessage = "Dung lượng ổ mới phải là số GB, ví dụ 100.";
            return;
        }

        var letter = char.ToUpperInvariant(_splitSource.FirstOrDefault('C'));
        Previewing = true;
        try
        {
            var (probe, error) = await Task.Run(() => DiskSplitProbeReader.ReadAsync(letter, CancellationToken.None));
            if (probe is null)
            {
                PlanMessage = error ?? "Không đọc được phân vùng.";
                return;
            }

            var plan = DiskSplitPlanner.Plan(probe, new DiskSplitRequest(letter, gb, _splitLabel.Trim()));
            _plan = plan;
            PlanRows.Add(new InfoRow($"Ổ {letter}: hiện tại", $"{DiskSplitPlanner.Gb(probe.PartitionSize)} · trống {DiskSplitPlanner.Gb(probe.FreeBytes)}"));
            PlanRows.Add(new InfoRow("Tách được tối đa", DiskSplitPlanner.Gb(plan.MaxNewBytes)));
            if (plan.IsValid)
            {
                PlanRows.Add(new InfoRow($"Ổ {letter}: sau khi chia", $"{DiskSplitPlanner.Gb(plan.TargetSourceSize)} · trống {DiskSplitPlanner.Gb(probe.FreeBytes - plan.NewPartitionBytes)}"));
                PlanRows.Add(new InfoRow("Ổ mới", $"{plan.NewLetter}: {DiskSplitPlanner.Gb(plan.NewPartitionBytes)} · NTFS · “{plan.Request.Label}”"));
            }

            PlanRows.Add(new InfoRow("Ổ đĩa vật lý", $"Disk {probe.DiskNumber} · {probe.PartitionStyle} · {probe.PartitionCount} phân vùng"));
            foreach (var e in plan.Errors)
            {
                PlanErrors.Add(e);
            }

            foreach (var w in plan.Warnings)
            {
                PlanWarnings.Add(w);
            }

            PlanMessage = plan.IsValid ? "Có thể chia theo kế hoạch trên. Bấm “Chia ổ…” để xác nhận." : "Chưa chia được — xem lý do bên dưới.";
            _main.S.Log.Info($"Xem trước chia ổ {letter}: {(plan.IsValid ? "hợp lệ" : string.Join(" ", plan.Errors))}");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Text.Json.JsonException or System.ComponentModel.Win32Exception or IOException)
        {
            PlanMessage = "Không đọc được phân vùng: " + ex.Message;
        }
        finally
        {
            Previewing = false;
            OnPropertyChanged(nameof(HasPlan));
            OnPropertyChanged(nameof(PlanValid));
            Commands.InvalidateAll();
        }
    }

    private void AskSplit()
    {
        if (_plan is not { IsValid: true } plan)
        {
            return;
        }

        var src = plan.Probe.DriveLetter;
        _main.ShowDialog(new ConfirmDialogViewModel(_main, "Xác nhận chia ổ đĩa", "disk", "Chia ổ", async () =>
        {
            await _main.RunAsync([new TaskRowViewModel(WindowsCatalog.SplitDisk(plan)) { IsSelected = true }], "Chia ổ đĩa");
            ClearPlan();
            await _main.Machine.CheckAsync();
        }, "Hủy")
        {
            Lead = $"Thu nhỏ ổ {src}: và tạo ổ {plan.NewLetter}: mới. Dữ liệu đang có trên ổ {src}: được giữ nguyên.",
            Facts =
            [
                .. PlanRows.Select(r => new DialogFact(r.Label, r.Value)),
                new DialogFact("Thời gian", "Vài phút đến vài chục phút. Không tắt máy, không rút sạc khi đang chia."),
                new DialogFact("Hoàn tác", "Không tự hoàn tác. Gộp lại bằng Disk Management (xóa ổ mới, Extend ổ cũ)."),
            ],
            Warning = "Chia ổ thay đổi bảng phân vùng. Mất điện hoặc tắt máy giữa chừng có thể làm hỏng dữ liệu." +
                      (plan.Warnings.Count > 0 ? " " + string.Join(" ", plan.Warnings) : ""),
            WarningIsDanger = true,
            RequireAgreement = true,
            AgreementText = "Tôi đã sao lưu dữ liệu quan trọng, máy đang cắm sạc, và đồng ý chia ổ.",
        });
    }

    private void Rebuild()
    {
        Drives.Clear();
        Account.Clear();
        if (_main.Machine.Snapshot is not { } s)
        {
            OnPropertyChanged(nameof(HasDrives));
            return;
        }

        var disks = s.Array("physicalDisks");
        var media = disks.Count == 1 ? $"{InventorySnapshot.Str(disks[0], "media")} {InventorySnapshot.Str(disks[0], "bus")}".Trim() : "";
        var allHealthy = disks.All(d => string.Equals(InventorySnapshot.Str(d, "health"), "Healthy", StringComparison.OrdinalIgnoreCase));
        var bitlocker = s.Array("bitlocker");
        var ntfs = s.Array("volumes")
            .Where(v => string.Equals(InventorySnapshot.Str(v, "fileSystem"), "NTFS", StringComparison.OrdinalIgnoreCase))
            .Select(v => InventorySnapshot.Str(v, "drive"))
            .OfType<string>()
            .ToList();
        if (ntfs.Count > 0)
        {
            SplitSources.Clear();
            foreach (var d in ntfs)
            {
                SplitSources.Add(d);
            }

            if (!SplitSources.Contains(_splitSource))
            {
                SplitSource = SplitSources[0];
            }
        }

        foreach (var v in s.Array("volumes"))
        {
            var letter = InventorySnapshot.Str(v, "drive") ?? "?";
            var size = InventorySnapshot.Num(v, "size") ?? 0;
            var free = InventorySnapshot.Num(v, "free") ?? 0;
            var enc = bitlocker.FirstOrDefault(b => string.Equals(InventorySnapshot.Str(b, "drive"), letter, StringComparison.OrdinalIgnoreCase));
            var protection = enc.ValueKind == System.Text.Json.JsonValueKind.Object ? InventorySnapshot.Num(enc, "protection") : null;
            Drives.Add(new DriveRow(
                letter,
                InventorySnapshot.Str(v, "label") is { Length: > 0 } l ? l : "(không có nhãn)",
                string.Join(" · ", new[] { media, InventorySnapshot.Str(v, "fileSystem") }.Where(x => !string.IsNullOrWhiteSpace(x))),
                CleanupTask.FormatBytes(size),
                CleanupTask.FormatBytes(free) + " trống",
                size > 0 ? 100.0 * (size - free) / size : 0,
                disks.Count == 0 ? "unchecked" : (allHealthy ? "pass" : "attention"),
                disks.Count == 0 ? "Chưa rõ" : (allHealthy ? "Ổ khỏe" : "Cần xem"),
                protection switch { 1 => "attention", 0 => "na", _ => "unchecked" },
                protection switch { 1 => "BitLocker bật", 0 => "Không mã hóa", _ => "Chưa rõ mã hóa" }));
        }

        Account.Add(new InfoRow("Quyền của phiên này", _main.IsElevated ? "Quản trị viên" : "Người dùng (chưa nâng quyền)"));
        Account.Add(new InfoRow("Tài khoản cục bộ đang bật", s.Num("localUsersEnabled")?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "Chưa đọc"));
        OnPropertyChanged(nameof(HasDrives));
    }
}
