using LamMotCham.Presentation.Mvvm;
using LamMotCham.Core.Logging;
using LamMotCham.Core.Tasks;
using System.Collections.ObjectModel;
using LamMotCham.Windows;
using LamMotCham.Windows.Config;

namespace LamMotCham.Presentation.ViewModels;

public sealed record SettingGroup(string Title, string Description, string IconKey, IReadOnlyList<TaskRowViewModel> Rows);

/// <summary>Màn hình C — Tối ưu Windows (UI/UX Handoff mục 9).</summary>
public sealed class WindowsViewModel : ObservableObject
{
    /// <summary>Mô-đun của trang này (QĐ-002).</summary>
    public LamMotCham.Presentation.Modules.AppModule Module { get; } = LamMotCham.Presentation.Modules.ModuleCatalog.Windows;

    private readonly MainViewModel _main;
    private readonly List<TaskRowViewModel> _rows;
    private bool _statesLoaded;
    private bool _bitLockerReading;
    private string _bitLockerMessage = "Mở trang với quyền quản trị để đọc trạng thái BitLocker.";

    public WindowsViewModel(MainViewModel main)
    {
        _main = main;
        _rows = [.. main.RowsIn(TaskCategory.WindowsSettings)];

        // Đọc giá trị hiện tại khi mở trang lần đầu (không đọc lúc khởi động để mở app nhanh).
        main.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.CurrentPage) && main.CurrentPage == Page.Windows && !_statesLoaded && !main.IsRunning)
            {
                _statesLoaded = true;
                _ = main.Machine.RefreshCurrentStatesAsync(TaskCategory.WindowsSettings);
                _ = ReadBitLockerAsync();
            }
        };
        ReadBitLockerCommand = new AsyncCommand(ReadBitLockerAsync, () => !_bitLockerReading && !main.IsRunning);
        TurnOffBitLockerCommand = new RelayCommand(AskTurnOffBitLocker,
            () => !_bitLockerReading && !main.IsRunning && BitLockerVolumes.Any(v => v.CanTurnOff));
        (string Title, string Desc, string Icon)[] groups =
        [
            (WindowsCatalog.GroupMaintenance, "Giờ hệ thống và trạng thái cập nhật.", "refresh"),
            (WindowsCatalog.GroupExperience, "File Explorer, desktop và taskbar.", "monitor"),
            (WindowsCatalog.GroupPrivacy, "Giảm quảng cáo và gợi ý của Windows.", "lock"),
            (WindowsCatalog.GroupPerformance, "Nguồn điện và ứng dụng chạy cùng Windows.", "bolt"),
        ];
        Groups = [.. groups.Select(g => new SettingGroup(g.Title, g.Desc, g.Icon, [.. _rows.Where(r => r.Group == g.Title)]))];
        if (_rows.FirstOrDefault() is { } first)
        {
            first.IsExpanded = true;
        }

        foreach (var r in _rows)
        {
            r.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(TaskRowViewModel.IsSelected))
                {
                    OnPropertyChanged(nameof(SelectedCount));
                    OnPropertyChanged(nameof(ApplyLabel));
                    Commands.InvalidateAll();
                }
            };
        }

        ApplyCommand = new RelayCommand(() => Ask(_rows.Where(r => r.IsSelected && r.IsSelectable).ToList()), () => SelectedCount > 0 && !main.IsRunning);
        ApplyOneCommand = new RelayCommand(p =>
        {
            if (p is TaskRowViewModel row)
            {
                Ask([row]);
            }
        }, _ => !main.IsRunning);
        UndoCommand = new RelayCommand(AskUndo, () => !main.IsRunning);
        ShowErrorCommand = new RelayCommand(p =>
        {
            if (p is TaskRowViewModel row)
            {
                main.ShowRowError(row);
            }
        });
    }

    public IReadOnlyList<SettingGroup> Groups { get; }

    public RelayCommand ApplyCommand { get; }

    public RelayCommand ApplyOneCommand { get; }

    public RelayCommand UndoCommand { get; }

    public RelayCommand ShowErrorCommand { get; }

    public AsyncCommand ReadBitLockerCommand { get; }

    public RelayCommand TurnOffBitLockerCommand { get; }

    /// <summary>Trạng thái BitLocker từng ổ.</summary>
    public ObservableCollection<BitLockerVolume> BitLockerVolumes { get; } = [];

    public string BitLockerMessage
    {
        get => _bitLockerMessage;
        private set => Set(ref _bitLockerMessage, value);
    }

    public bool BitLockerReading
    {
        get => _bitLockerReading;
        private set
        {
            if (Set(ref _bitLockerReading, value))
            {
                OnPropertyChanged(nameof(BitLockerReadLabel));
                Commands.InvalidateAll();
            }
        }
    }

    public string BitLockerReadLabel => _bitLockerReading ? "Đang đọc…" : "Đọc lại";

    private async Task ReadBitLockerAsync()
    {
        if (!_main.IsElevated)
        {
            BitLockerMessage = "Cần quyền quản trị để đọc và tắt BitLocker. Bấm “Nâng quyền quản trị…” ở thanh trên.";
            return;
        }

        BitLockerReading = true;
        try
        {
            var (volumes, error) = await Task.Run(() => BitLockerReader.ReadAsync(CancellationToken.None));
            BitLockerVolumes.Clear();
            foreach (var v in volumes)
            {
                BitLockerVolumes.Add(v);
            }

            BitLockerMessage = error
                ?? (volumes.Count == 0 ? "Không có ổ nào hỗ trợ BitLocker."
                : volumes.Any(v => v.CanTurnOff) ? "Có ổ đang được mã hóa. Chỉ tắt khi khách yêu cầu."
                : volumes.Any(v => v.Conversion is 3 or 5) ? "Đang giải mã nền. Có thể đóng ứng dụng, Windows tự giải mã tiếp."
                : "Không ổ nào đang mã hóa.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Text.Json.JsonException or System.ComponentModel.Win32Exception or IOException)
        {
            BitLockerMessage = "Không đọc được BitLocker: " + ex.Message;
        }
        finally
        {
            BitLockerReading = false;
        }
    }

    private void AskTurnOffBitLocker()
    {
        // Ổ dữ liệu trước, ổ Windows sau cùng.
        var targets = BitLockerVolumes.Where(v => v.CanTurnOff).OrderBy(v => v.IsOsVolume).ThenBy(v => v.Letter).ToList();
        if (targets.Count == 0)
        {
            return;
        }

        var names = string.Join(", ", targets.Select(v => v.Letter + ":"));
        var hasData = targets.Any(v => !v.IsOsVolume);
        var hasOs = targets.Any(v => v.IsOsVolume);
        _main.ShowDialog(new ConfirmDialogViewModel(_main, "Tắt BitLocker", "lock", "Tắt BitLocker", async () =>
        {
            var rows = targets.Select(v => new TaskRowViewModel(WindowsCatalog.TurnOffBitLocker(v)) { IsSelected = true }).ToList();
            await _main.RunAsync(rows, "Tắt BitLocker");
            await ReadBitLockerAsync();
        }, "Hủy")
        {
            Lead = $"Giải mã ổ {names}. Windows giải mã chạy nền, có thể mất từ vài phút đến hơn một giờ tùy dung lượng; máy vẫn dùng bình thường.",
            Facts =
            [
                .. targets.Select(v => new DialogFact($"Ổ {v.Letter}:" + (v.IsOsVolume ? " (Windows)" : ""), v.StateText)),
                new DialogFact("Thứ tự", hasData && hasOs
                    ? "Ổ dữ liệu trước. Ổ Windows chỉ tắt được khi ổ dữ liệu đã giải mã xong — nếu chưa, bấm lại sau."
                    : "Lần lượt từng ổ."),
                new DialogFact("Khóa khôi phục", "Ứng dụng không đọc, hiển thị hay ghi log khóa khôi phục."),
                new DialogFact("Bật lại", "Settings › Privacy & security › Device encryption, hoặc Control Panel › BitLocker."),
            ],
            Warning = "Sau khi tắt, dữ liệu trên ổ không còn được mã hóa: nếu máy bị mất, người khác có thể đọc được. Chỉ tắt khi khách yêu cầu.",
            WarningIsDanger = true,
            RequireAgreement = true,
            AgreementText = "Khách hàng đã đồng ý tắt mã hóa ổ đĩa.",
        });
    }

    public int SelectedCount => _rows.Count(r => r.IsSelected && r.IsSelectable);

    public string ApplyLabel => $"Áp dụng {SelectedCount} thay đổi";

    private void Ask(List<TaskRowViewModel> rows)
    {
        if (rows.Count == 0)
        {
            return;
        }

        var admin = rows.Count(r => r.NeedsAdmin);
        _main.ShowDialog(new ConfirmDialogViewModel(_main, "Xác nhận thay đổi Windows", "gear", "Áp dụng",
            async () => await _main.RunAsync(rows, "Tối ưu Windows"), "Hủy")
        {
            Items = [.. rows.Select(r => new DialogItem(r.Name, r.Details.Count > 0 ? r.Details[0].Value : null, r.ImpactKey))],
            Facts =
            [
                new DialogFact("Tác động", "Chỉ đổi thiết lập hiển thị, múi giờ, quyền riêng tư và nguồn điện. Không đụng đến file hay phần mềm."),
                new DialogFact("Quyền", admin == 0 ? "Không cần quyền quản trị." : $"{admin} thay đổi cần quyền quản trị; Windows có thể hỏi UAC."),
                new DialogFact("Hoàn tác", "Giá trị cũ được lưu lại; dùng “Hoàn tác thay đổi trước” để khôi phục."),
            ],
        });
    }

    private void AskUndo()
    {
        var undoable = _rows.Where(r => r.IsSelectable && _main.S.Rollback.Load(r.Id) is not null).ToList();
        if (undoable.Count == 0)
        {
            _main.ShowDialog(new ListDialogViewModel(_main, "Hoàn tác thay đổi trước", "refresh", [],
                "Chưa có thay đổi nào do ứng dụng thực hiện trên máy này."));
            return;
        }

        _main.ShowDialog(new ConfirmDialogViewModel(_main, "Hoàn tác thay đổi trước", "refresh", "Hoàn tác", () => UndoAsync(undoable), "Hủy")
        {
            Lead = "Khôi phục giá trị đã lưu trước khi ứng dụng thay đổi:",
            Items = [.. undoable.Select(r => new DialogItem(r.Name))],
        });
    }

    private async Task UndoAsync(IReadOnlyList<TaskRowViewModel> rows)
    {
        var results = new List<DialogItem>();
        foreach (var row in rows)
        {
            var context = new TaskContext(_main.S.Log, 1);
            var result = await Task.Run(() => row.Entry.Module.RollbackAsync(context, CancellationToken.None));
            _main.S.Log.Info($"Hoàn tác {row.Name}: {result.Outcome} {result.Detail}", row.Id);
            results.Add(new DialogItem(row.Name, result.Detail, result.Outcome == VerifyOutcome.Passed ? "success" : "attention"));
        }

        await _main.Machine.RefreshCurrentStatesAsync();
        _main.ShowDialog(new ListDialogViewModel(_main, "Kết quả hoàn tác", "refresh", results));
    }
}
