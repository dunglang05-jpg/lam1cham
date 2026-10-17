using System.Collections.ObjectModel;
using LamMotCham.Presentation.Mvvm;
using LamMotCham.Presentation.Services;
using LamMotCham.Core.Tasks;
using LamMotCham.Windows;
using LamMotCham.Windows.Cleanup;
using LamMotCham.Windows.Config;
using LamMotCham.Windows.Probe;

namespace LamMotCham.Presentation.ViewModels;

public sealed record DriveRow(string Letter, string Label, string Type, string Size, string Free, double UsedPercent, string HealthKey, string HealthText, string EncryptionKey, string EncryptionText);

/// <summary>Màn hình D — Ổ đĩa và tài khoản (UI/UX Handoff mục 10). Chỉ đọc, trừ đổi tên máy.</summary>
public sealed class DiskViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private string _newName = "";
    private string _encryptionKey = "unchecked";
    private string _encryptionText = "Chưa kiểm tra";
    private string _encryptionDetail = "Bấm “Kiểm tra máy” ở Tổng quan để đọc trạng thái mã hóa.";

    public DiskViewModel(MainViewModel main)
    {
        _main = main;
        RenameCommand = new RelayCommand(AskRename, () => IsNameValid && !main.IsRunning);
        RecoveryHelpCommand = new RelayCommand(() => ShellActions.OpenUri("https://support.microsoft.com/windows/find-your-bitlocker-recovery-key-6b71ad27-0b89-ea08-f143-056f5ab347d6"));
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
