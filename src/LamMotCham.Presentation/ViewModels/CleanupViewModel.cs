using System.Globalization;
using LamMotCham.Presentation.Mvvm;
using LamMotCham.Core.Tasks;
using LamMotCham.Windows.Cleanup;

namespace LamMotCham.Presentation.ViewModels;

public sealed class CleanupItemViewModel(TaskRowViewModel row, CleanupGroup group) : ObservableObject
{
    private string _files = "Chưa quét";
    private string _size = "—";
    private long _bytes;
    private string? _note;

    public TaskRowViewModel Row { get; } = row;

    public CleanupGroup Group { get; } = group;

    public string Name => Group.Name;

    public string Scope => Group.Scope;

    public string IconKey => Row.IconKey;

    public string SafetyKey => Group.IsSafe ? "safe" : "review";

    public string Files { get => _files; set => Set(ref _files, value); }

    public string Size { get => _size; set => Set(ref _size, value); }

    public long Bytes { get => _bytes; set => Set(ref _bytes, value); }

    public string? Note { get => _note; set => Set(ref _note, value); }
}

/// <summary>Màn hình E — Dọn dẹp và hiệu năng (UI/UX Handoff mục 11). Luôn quét và xem trước trước khi xóa.</summary>
public sealed class CleanupViewModel : ObservableObject
{
    /// <summary>Mô-đun của trang này (QĐ-002).</summary>
    public LamMotCham.Presentation.Modules.AppModule Module { get; } = LamMotCham.Presentation.Modules.ModuleCatalog.Cleanup;

    private readonly MainViewModel _main;
    private bool _scanned;
    private bool _scanning;
    private string _scannedAt = "";

    public CleanupViewModel(MainViewModel main)
    {
        _main = main;
        Items = [.. main.RowsIn(TaskCategory.Cleanup).Select(r => new CleanupItemViewModel(r, ((CleanupTask)r.Entry.Module).Group))];
        foreach (var item in Items)
        {
            item.Row.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(TaskRowViewModel.IsSelected))
                {
                    OnPropertyChanged(nameof(TotalText));
                    OnPropertyChanged(nameof(SelectedShort));
                    OnPropertyChanged(nameof(SelectedPercent));
                    OnPropertyChanged(nameof(CleanLabel));
                    Commands.InvalidateAll();
                }
            };
        }

        ScanCommand = new AsyncCommand(ScanAsync, () => !_scanning && !main.IsRunning);
        CleanCommand = new RelayCommand(AskClean, () => _scanned && Items.Any(i => i.Row.IsSelected) && !main.IsRunning);
    }

    public IReadOnlyList<CleanupItemViewModel> Items { get; }

    public AsyncCommand ScanCommand { get; }

    public RelayCommand CleanCommand { get; }

    public bool Scanned
    {
        get => _scanned;
        private set
        {
            if (Set(ref _scanned, value))
            {
                OnPropertyChanged(nameof(NotScanned));
            }
        }
    }

    public bool NotScanned => !_scanned;

    public bool Scanning
    {
        get => _scanning;
        private set
        {
            if (Set(ref _scanning, value))
            {
                OnPropertyChanged(nameof(ScanLabel));
            }
        }
    }

    public string ScanLabel => _scanning ? "Đang quét…" : (_scanned ? "Quét lại" : "Quét và xem trước");

    public string ScannedAt { get => _scannedAt; private set => Set(ref _scannedAt, value); }

    public string TotalText => "khoảng " + CleanupTask.FormatBytes(Items.Where(i => i.Row.IsSelected).Sum(i => i.Bytes));

    /// <summary>Dung lượng các mục đang chọn (giữa vòng tròn), "—" khi chưa quét.</summary>
    public string SelectedShort => _scanned ? CleanupTask.FormatBytes(Items.Where(i => i.Row.IsSelected).Sum(i => i.Bytes)) : "—";

    /// <summary>Phần trăm dung lượng đã chọn so với tổng đã quét (0–100) cho vòng tròn.</summary>
    public double SelectedPercent
    {
        get
        {
            var total = Items.Sum(i => i.Bytes);
            return !_scanned || total <= 0 ? 0 : Items.Where(i => i.Row.IsSelected).Sum(i => i.Bytes) * 100.0 / total;
        }
    }

    public string CleanLabel => $"Dọn {Items.Count(i => i.Row.IsSelected)} mục đã chọn";

    private async Task ScanAsync()
    {
        Scanning = true;
        try
        {
            foreach (var item in Items)
            {
                var scan = await Task.Run(() => item.Group.Scan(CancellationToken.None));
                item.Bytes = scan.Bytes;
                item.Size = CleanupTask.FormatBytes(scan.Bytes);
                item.Files = scan.FileCount.ToString("N0", CultureInfo.GetCultureInfo("vi-VN")) + " file";
                item.Note = scan.Error;
            }

            ScannedAt = "Quét lúc " + DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture);
            Scanned = true;
            OnPropertyChanged(nameof(TotalText));
            OnPropertyChanged(nameof(SelectedShort));
            OnPropertyChanged(nameof(SelectedPercent));
        }
        finally
        {
            Scanning = false;
        }
    }

    private void AskClean()
    {
        var picked = Items.Where(i => i.Row.IsSelected).ToList();
        var recycle = picked.Any(i => !i.Group.IsSafe);
        _main.ShowDialog(new ConfirmDialogViewModel(_main, "Xác nhận dọn dẹp", "broom", "Dọn dẹp", async () =>
        {
            await _main.RunAsync([.. picked.Select(i => i.Row)], "Dọn dẹp", confirmed: true);
            await ScanAsync();
        }, "Hủy")
        {
            Items = [.. picked.Select(i => new DialogItem(i.Name, i.Scope, null, null, i.Size))],
            Lead = $"Tổng {TotalText} chỉ là con số ước tính. File đang được dùng sẽ được bỏ qua. Không file nào ngoài phạm vi trên bị xóa.",
            Warning = recycle ? "File trong Thùng rác sẽ bị xóa vĩnh viễn, không khôi phục được." : null,
            WarningIsDanger = recycle,
        });
    }
}
