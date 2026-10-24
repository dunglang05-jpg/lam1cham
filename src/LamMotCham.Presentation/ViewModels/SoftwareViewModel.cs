using System.Collections.ObjectModel;
using LamMotCham.Presentation.Mvvm;
using LamMotCham.Presentation.Services;
using LamMotCham.Core.Tasks;
using LamMotCham.Windows.Software;

namespace LamMotCham.Presentation.ViewModels;

/// <summary>Chip lọc theo nhóm ứng dụng (thiết kế v3).</summary>
public sealed class CategoryChip : ObservableObject
{
    private readonly SoftwareViewModel _owner;

    internal CategoryChip(SoftwareViewModel owner, string label)
    {
        _owner = owner;
        Label = label;
    }

    public string Label { get; }

    public bool IsSelected
    {
        get => string.Equals(_owner.Category, Label, StringComparison.Ordinal);
        set
        {
            if (value)
            {
                _owner.Category = Label;
            }
        }
    }

    internal void Refresh() => OnPropertyChanged(nameof(IsSelected));
}

/// <summary>Màn hình B — Phần mềm thiết yếu (UI/UX Handoff mục 8).</summary>
public sealed class SoftwareViewModel : ObservableObject
{
    /// <summary>Mô-đun của trang này (QĐ-002).</summary>
    public LamMotCham.Presentation.Modules.AppModule Module { get; } = LamMotCham.Presentation.Modules.ModuleCatalog.Software;

    public const string AllCategories = "Tất cả";

    private readonly MainViewModel _main;
    private readonly List<TaskRowViewModel> _all;
    private string _search = "";
    private string _category = AllCategories;
    private bool _techOpen;

    public SoftwareViewModel(MainViewModel main)
    {
        _main = main;
        _all = [.. main.RowsIn(TaskCategory.Software)];
        Categories = [AllCategories, .. SoftwareCatalog.Categories];
        CategoryChips = [.. Categories.Select(c => new CategoryChip(this, c))];
        foreach (var r in _all)
        {
            r.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(TaskRowViewModel.IsSelected) or nameof(TaskRowViewModel.CurrentText))
                {
                    OnSelectionChanged();
                }
                else if (e.PropertyName == nameof(TaskRowViewModel.State))
                {
                    OnPropertyChanged(nameof(QueueText));
                    OnPropertyChanged(nameof(InstallLabel));
                }
            };
        }

        InstallCommand = new RelayCommand(AskInstall, () => CanInstall);
        PlanCommand = new RelayCommand(ShowPlan);
        RefreshStatusCommand = new AsyncCommand(() => main.Machine.RefreshCurrentStatesAsync(TaskCategory.Software), () => !main.IsRunning);
        RecheckCommand = new AsyncCommand(main.Machine.CheckAsync, () => !main.IsRunning);
        OpenStoreCommand = new RelayCommand(() => ShellActions.OpenUri("ms-windows-store://pdp/?productid=9NBLGGH4NNS1"));
        ToggleTechCommand = new RelayCommand(() => TechOpen = !TechOpen);
        ShowErrorCommand = new RelayCommand(p =>
        {
            if (p is TaskRowViewModel row)
            {
                main.ShowRowError(row);
            }
        });
        main.Machine.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MachineViewModel.WinGetMissing) or nameof(MachineViewModel.WinGetAvailable))
            {
                OnPropertyChanged(nameof(WinGetMissing));
                OnSelectionChanged();
            }
        };
        ApplyFilter();
        RebuildSelected();
    }

    public MainViewModel Main => _main;

    public IReadOnlyList<string> Categories { get; }

    public IReadOnlyList<CategoryChip> CategoryChips { get; }

    /// <summary>Ứng dụng đang chọn (hiện logo ở khung bên phải).</summary>
    public ObservableCollection<TaskRowViewModel> SelectedRows { get; } = [];

    public string SelectedCountText => $"{SelectedCount} ứng dụng";

    public string SkipNote
    {
        get
        {
            var installed = _all.Count(r => r.IsSelected && r.CurrentText == "Đã cài");
            return SelectedCount == 0
                ? "Chưa chọn ứng dụng nào."
                : installed > 0 ? $"{installed} ứng dụng đã có sẵn sẽ được bỏ qua" : "Ứng dụng đã có trên máy sẽ được bỏ qua";
        }
    }

    public ObservableCollection<TaskRowViewModel> Visible { get; } = [];

    public ObservableCollection<TaskRowViewModel> Queue { get; } = [];

    public RelayCommand InstallCommand { get; }

    public RelayCommand PlanCommand { get; }

    public AsyncCommand RefreshStatusCommand { get; }

    public AsyncCommand RecheckCommand { get; }

    public RelayCommand OpenStoreCommand { get; }

    public RelayCommand ToggleTechCommand { get; }

    public RelayCommand ShowErrorCommand { get; }

    public bool WinGetMissing => _main.Machine.WinGetMissing;

    public bool TechOpen
    {
        get => _techOpen;
        set => Set(ref _techOpen, value);
    }

    public string SearchText
    {
        get => _search;
        set
        {
            if (Set(ref _search, value ?? ""))
            {
                ApplyFilter();
            }
        }
    }

    public string Category
    {
        get => _category;
        set
        {
            if (Set(ref _category, value ?? AllCategories))
            {
                ApplyFilter();
                foreach (var chip in CategoryChips)
                {
                    chip.Refresh();
                }
            }
        }
    }

    public bool NoResults => Visible.Count == 0;

    public int SelectedCount => _all.Count(r => r.IsSelected);

    public bool RecommendedSelected
    {
        get => _all.Where(r => r.Entry.SelectedByDefault).All(r => r.IsSelected);
        set
        {
            foreach (var r in _all.Where(r => r.Entry.SelectedByDefault))
            {
                r.IsSelected = value;
            }
        }
    }

    public bool CanInstall => SelectedCount > 0 && !_main.IsRunning && !WinGetMissing;

    public string InstallLabel => WinGetMissing
        ? "Cần WinGet để cài"
        : _main.IsRunning ? "Đang chạy…" : SelectedCount == 0 ? "Chọn ít nhất 1 ứng dụng" : $"Cài {SelectedCount} ứng dụng đã chọn";

    public bool QueueEmpty => Queue.Count == 0;

    public string QueueText
    {
        get
        {
            if (WinGetMissing)
            {
                return "Hàng đợi: chưa thể chạy vì thiếu WinGet";
            }

            if (Queue.Count == 0)
            {
                return $"Hàng đợi: {SelectedCount} gói · đang chờ xác nhận";
            }

            var done = Queue.Count(r => r.State is { } s && s.IsTerminal());
            var att = Queue.Count(r => r.IsProblem);
            return $"Hàng đợi: {done}/{Queue.Count} đã xử lý" + (att > 0 ? $" · {att} cần xem" : "");
        }
    }

    public void RefreshQueue()
    {
        Queue.Clear();
        foreach (var r in _all.Where(r => r.HasRun))
        {
            Queue.Add(r);
        }

        OnPropertyChanged(nameof(QueueEmpty));
        OnSelectionChanged();
    }

    private void AskInstall()
    {
        var picked = _all.Where(r => r.IsSelected).ToList();
        if (picked.Count == 0)
        {
            return;
        }

        var admin = picked.Count(r => r.NeedsAdmin);
        var already = picked.Where(r => r.CurrentText == "Đã cài").Select(r => r.Name).ToList();
        _main.ShowDialog(new ConfirmDialogViewModel(_main, "Xác nhận cài đặt", "download", "Xác nhận cài đặt", async () =>
        {
            Queue.Clear();
            foreach (var r in picked)
            {
                Queue.Add(r);
            }

            OnPropertyChanged(nameof(QueueEmpty));
            await _main.RunAsync(picked, "Cài phần mềm", confirmed: true);
        })
        {
            Items = [.. picked.Select((r, i) => new DialogItem($"{i + 1}. {r.Name}", null, null, null, ((WinGetInstallTask)r.Entry.Module).Package.WinGetId))],
            Facts =
            [
                new DialogFact("Nguồn", "WinGet — kho cộng đồng “winget”; chỉ các package ID đã duyệt."),
                new DialogFact("Chế độ", "Cài im lặng (silent), không mở cửa sổ trình cài đặt."),
                new DialogFact("Quyền", admin == 0 ? "Không cần quyền quản trị." : $"{admin} gói cài cho toàn máy. Windows có thể hỏi quyền quản trị (UAC)."),
                new DialogFact("Khởi động lại", "Chưa gói nào báo cần. Nếu có, hàng đợi dừng lại và hỏi bạn."),
                new DialogFact("Đã cài sẵn", already.Count == 0 ? "Gói đã có trên máy sẽ được bỏ qua." : string.Join(", ", already) + " sẽ được bỏ qua."),
            ],
            RequireAgreement = true,
            AgreementText = "Đồng ý để Làm 1 Chạm chấp nhận điều khoản của nguồn WinGet và của từng gói thay cho người dùng máy này.",
        });
    }

    private void ShowPlan()
    {
        var picked = _all.Where(r => r.IsSelected).ToList();
        _main.ShowDialog(new ListDialogViewModel(_main, "Kế hoạch cài đặt", "list",
            [.. picked.Select((r, i) => new DialogItem($"{i + 1}. {r.Name}",
                ((WinGetInstallTask)r.Entry.Module).Package.WinGetId + (r.NeedsAdmin ? " · cài cho toàn máy" : " · cài cho người dùng"),
                r.StateKey))],
            picked.Count == 0 ? "Chưa chọn ứng dụng nào." : "Các gói chạy lần lượt theo thứ tự dưới đây, mỗi lần một trình cài đặt."));
    }

    private void ApplyFilter()
    {
        var q = _search.Trim();
        Visible.Clear();
        foreach (var r in _all)
        {
            var pkg = ((WinGetInstallTask)r.Entry.Module).Package;
            var catOk = _category == AllCategories || pkg.Category == _category;
            var textOk = q.Length == 0 ||
                         pkg.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                         pkg.WinGetId.Contains(q, StringComparison.OrdinalIgnoreCase);
            if (catOk && textOk)
            {
                Visible.Add(r);
            }
        }

        OnPropertyChanged(nameof(NoResults));
    }

    private void RebuildSelected()
    {
        SelectedRows.Clear();
        foreach (var r in _all.Where(r => r.IsSelected))
        {
            SelectedRows.Add(r);
        }
    }

    private void OnSelectionChanged()
    {
        RebuildSelected();
        OnPropertyChanged(nameof(SelectedCountText));
        OnPropertyChanged(nameof(SkipNote));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(RecommendedSelected));
        OnPropertyChanged(nameof(CanInstall));
        OnPropertyChanged(nameof(InstallLabel));
        OnPropertyChanged(nameof(QueueText));
        Commands.InvalidateAll();
    }
}
