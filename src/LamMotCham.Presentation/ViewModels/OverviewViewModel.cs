using System.Collections.ObjectModel;
using LamMotCham.Presentation.Mvvm;
using LamMotCham.Presentation.Services;
using LamMotCham.Core.Tasks;

namespace LamMotCham.Presentation.ViewModels;

public sealed record PackOption(TaskPack Pack, string Label);

/// <summary>Một bước trong thẻ "Một chạm": icon, số lượng thật từ danh mục, mô tả.</summary>
public sealed record SetupLine(string IconKey, string Count, string Text);

/// <summary>Thẻ gói thiết lập nhanh trên Tổng quan (thiết kế v3).</summary>
public sealed class PackCardViewModel : ObservableObject
{
    private readonly OverviewViewModel _owner;

    internal PackCardViewModel(OverviewViewModel owner, PackOption option, string description, string tag, string iconKey,
        IReadOnlyList<TaskRowViewModel> apps, string extra, string accent)
    {
        _owner = owner;
        Option = option;
        Description = description;
        Tag = tag;
        IconKey = iconKey;
        Apps = apps;
        Extra = extra;
        Accent = accent;
        StartCommand = new RelayCommand(() => owner.StartPack(option), () => owner.CanStart);
    }

    /// <summary>Màu ô icon: blue | green | amber | purple.</summary>
    public string Accent { get; }

    /// <summary>Chọn gói này và mở hộp xác nhận chạy.</summary>
    public RelayCommand StartCommand { get; }

    public PackOption Option { get; }

    public string Title => Option.Label;

    public string Description { get; }

    public string Tag { get; }

    public string IconKey { get; }

    /// <summary>Ứng dụng thuộc gói (hiện logo).</summary>
    public IReadOnlyList<TaskRowViewModel> Apps { get; }

    public bool HasApps => Apps.Count > 0;

    public string Extra { get; }

    public bool IsSelected
    {
        get => ReferenceEquals(_owner.SelectedPack, Option);
        set
        {
            if (value)
            {
                _owner.SelectedPack = Option;
            }
        }
    }

    internal void Refresh() => OnPropertyChanged(nameof(IsSelected));
}

public sealed class StepViewModel(string number, string title, Page target) : ObservableObject
{
    private string _desc = "";
    private string _stateKey = "pending";
    private string? _stateText;

    public string Number { get; } = number;

    public string Title { get; } = title;

    public Page Target { get; } = target;

    public string Description { get => _desc; set => Set(ref _desc, value); }

    public string StateKey { get => _stateKey; set => Set(ref _stateKey, value); }

    public string? StateText { get => _stateText; set => Set(ref _stateText, value); }
}

/// <summary>Màn hình A — Tổng quan (UI/UX Handoff mục 7).</summary>
public sealed class OverviewViewModel : ObservableObject
{
    /// <summary>Mô-đun của trang này (QĐ-002).</summary>
    public LamMotCham.Presentation.Modules.AppModule Module { get; } = LamMotCham.Presentation.Modules.ModuleCatalog.Overview;

    private readonly MainViewModel _main;
    private PackOption _pack;

    public OverviewViewModel(MainViewModel main)
    {
        _main = main;
        Packs = [.. Enum.GetValues<TaskPack>().Select(p => new PackOption(p, p.DisplayName()))];
        _pack = Packs[0];
        PackCards = [.. Packs.Select(BuildCard)];
        Steps =
        [
            new("1", "Kiểm tra máy", Page.Overview),
            new("2", "Chọn phần mềm", Page.Software),
            new("3", "Cài đặt", Page.Software),
            new("4", "Tác vụ Windows", Page.Windows),
            new("5", "Kiểm tra bàn giao", Page.Handover),
        ];
        StartPackCommand = new RelayCommand(AskStartPack, () => CanStart);
        StartFullCommand = new RelayCommand(() => StartPack(Packs.First(p => p.Pack == TaskPack.FullSetup)), () => CanStart);
        var full = main.Rows.Values.Where(r => r.Entry.IsInPack(TaskPack.FullSetup)).ToList();
        FullSetupLines =
        [
            new("download", full.Count(r => r.Entry.Category == TaskCategory.Software).ToString(System.Globalization.CultureInfo.InvariantCulture), "phần mềm thiết yếu, xác minh sau cài"),
            new("gear", full.Count(r => r.Entry.Category == TaskCategory.WindowsSettings && !r.IsReadOnly).ToString(System.Globalization.CultureInfo.InvariantCulture), "thiết lập Windows, lưu để hoàn tác"),
            new("sparkle", full.Count(r => r.Entry.Category == TaskCategory.Cleanup).ToString(System.Globalization.CultureInfo.InvariantCulture), "nhóm file tạm được dọn an toàn"),
            new("clipboard", full.Count(r => r.IsReadOnly).ToString(System.Globalization.CultureInfo.InvariantCulture), "mục kiểm tra và báo cáo bàn giao"),
        ];
        FullSetupApps = [.. full.Where(r => r.Entry.Category == TaskCategory.Software)];
        OpenLastReportCommand = new RelayCommand(
            () => ShellActions.OpenFile(main.LastHandoverReportPath ?? main.LastRunReportPath ?? ""),
            () => (main.LastHandoverReportPath ?? main.LastRunReportPath) is not null);
        GoCommand = new RelayCommand(p =>
        {
            if (p is StepViewModel step)
            {
                main.CurrentPage = step.Target;
            }
        });
        main.Machine.PropertyChanged += (_, _) => RefreshSteps();
        foreach (var row in main.Rows.Values)
        {
            row.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(TaskRowViewModel.State) or nameof(TaskRowViewModel.IsSelected))
                {
                    RefreshSteps();
                }
            };
        }

        RefreshSteps();
    }

    public MainViewModel Main => _main;

    public MachineViewModel Machine => _main.Machine;

    public IReadOnlyList<PackOption> Packs { get; }

    public IReadOnlyList<PackCardViewModel> PackCards { get; }

    public PackCardViewModel SelectedCard => PackCards.First(c => ReferenceEquals(c.Option, _pack));

    /// <summary>Dòng chú thích dưới nút Bắt đầu.</summary>
    public string StartNote
    {
        get
        {
            var n = _main.Rows.Values.Count(r => r.IsSelected);
            return n == 0
                ? "Chưa chọn tác vụ nào. Chọn gói hoặc chọn ở từng trang."
                : $"{n} tác vụ chạy lần lượt theo thứ tự an toàn, xác minh từng bước.";
        }
    }

    public ObservableCollection<StepViewModel> Steps { get; }

    public RelayCommand StartPackCommand { get; }

    /// <summary>Thẻ xanh "Một chạm": chạy gói Thiết lập đầy đủ.</summary>
    public RelayCommand StartFullCommand { get; }

    /// <summary>Nội dung thật của gói Thiết lập đầy đủ (số lượng từ danh mục).</summary>
    public IReadOnlyList<SetupLine> FullSetupLines { get; }

    /// <summary>Ứng dụng sẽ cài trong gói Thiết lập đầy đủ (hiện logo).</summary>
    public IReadOnlyList<TaskRowViewModel> FullSetupApps { get; }

    /// <summary>Các gói còn lại (Tác vụ nhanh); gói đầy đủ đã có thẻ Một chạm.</summary>
    public IReadOnlyList<PackCardViewModel> OtherPacks => [.. PackCards.Where(c => c.Option.Pack != TaskPack.FullSetup)];

    internal bool CanStart => _main.Machine.Checked && !_main.IsRunning;

    /// <summary>Chọn gói rồi mở hộp xác nhận (không chạy khi chưa xác nhận).</summary>
    internal void StartPack(PackOption option)
    {
        SelectedPack = option;
        AskStartPack();
    }

    public RelayCommand OpenLastReportCommand { get; }

    public RelayCommand GoCommand { get; }

    public PackOption SelectedPack
    {
        get => _pack;
        set
        {
            if (value is not null && Set(ref _pack, value))
            {
                OnPropertyChanged(nameof(SelectedCard));
                foreach (var row in _main.Rows.Values)
                {
                    row.IsSelected = row.Entry.IsInPack(value.Pack);
                }

                foreach (var card in PackCards)
                {
                    card.Refresh();
                }
            }
        }
    }

    public string StartLabel { get; } = "Bắt đầu thực hiện (1 chạm)";

    private PackCardViewModel BuildCard(PackOption option)
    {
        var rows = _main.Rows.Values.Where(r => r.Entry.IsInPack(option.Pack)).ToList();
        var apps = rows.Where(r => r.Entry.Category == TaskCategory.Software).ToList();
        var changes = rows.Count(r => !r.IsReadOnly && r.Entry.Category is TaskCategory.WindowsSettings or TaskCategory.Cleanup);
        var checks = rows.Count(r => r.IsReadOnly);
        return option.Pack switch
        {
            TaskPack.FullSetup => new(this, option, "Phần mềm + tối ưu Windows + kiểm tra", "Phù hợp laptop mới", "bolt", apps,
                $"+ {changes} thiết lập Windows + {checks} mục kiểm tra", "blue"),
            TaskPack.SoftwareOnly => new(this, option, "Cài các ứng dụng cơ bản", "Khi máy đã ổn định", "download", apps,
                "Ứng dụng đã có trên máy sẽ được bỏ qua", "green"),
            TaskPack.WindowsOnly => new(this, option, "Cấu hình hệ thống và dọn dẹp", "Khi đã cài sẵn phần mềm", "gear", apps,
                $"{changes} thiết lập và dọn dẹp — thiết lập lưu giá trị cũ để hoàn tác", "amber"),
            _ => new(this, option, "Kiểm tra và lập báo cáo bàn giao", "Trước khi giao máy", "clipboard", apps,
                $"{checks} mục chỉ đọc, không thay đổi gì trên máy", "purple"),
        };
    }

    private void AskStartPack()
    {
        // Chạy mọi mục đang được chọn: gói đã chọn sẵn các mục, người dùng có thể bỏ bớt ở từng trang.
        var rows = _main.Rows.Values.Where(r => r.IsSelected).ToList();
        if (rows.Count == 0)
        {
            return;
        }

        var installs = rows.Count(r => r.Entry.Category == TaskCategory.Software);
        var changes = rows.Count(r => !r.IsReadOnly && r.Entry.Category != TaskCategory.Software);
        var checks = rows.Count(r => r.IsReadOnly);
        var bitLocker = WindowsViewModel.HasBitLockerOff(rows);
        const string wingetTerms = "Đồng ý để Làm 1 Chạm chấp nhận điều khoản của nguồn WinGet và của từng gói thay cho người dùng máy này.";

        // Mục người dùng tự chọn thêm ở các trang khác (không thuộc gói) phải hiện rõ trong hộp xác nhận,
        // nhất là thao tác không hoàn tác được như làm trống Thùng rác.
        var extras = ExtrasOutsidePack(rows, _pack.Pack);
        var irreversibleExtras = extras.Where(IsIrreversibleChange).ToList();
        var warnings = new List<string>();
        if (irreversibleExtras.Count > 0)
        {
            warnings.Add("Có mục chọn thêm ngoài gói không hoàn tác được: " + string.Join(", ", irreversibleExtras.Select(r => r.Name)) +
                         ". Bỏ chọn ở trang tương ứng nếu không muốn chạy.");
        }

        if (bitLocker)
        {
            warnings.Add(WindowsViewModel.BitLockerWarning);
        }

        var agreements = new List<string>();
        if (irreversibleExtras.Count > 0)
        {
            agreements.Add("Tôi đã xem các mục chọn thêm ngoài gói và đồng ý chạy cả thao tác không hoàn tác được.");
        }

        if (bitLocker)
        {
            agreements.Add(WindowsViewModel.BitLockerAgreement);
        }

        if (installs > 0)
        {
            agreements.Add(wingetTerms);
        }

        _main.ShowDialog(new ConfirmDialogViewModel(_main, "Xác nhận trước khi chạy", "shield", "Bắt đầu chạy",
            async () => await _main.RunAsync(rows, _pack.Label, confirmed: true))
        {
            Lead = $"{rows.Count} tác vụ sẽ chạy tuần tự. Mỗi tác vụ được xác minh lại trước khi báo thành công." +
                   (extras.Count > 0 ? $" Gồm {extras.Count} mục chọn thêm ngoài gói “{_pack.Label}”:" : ""),
            Items = [.. extras.Select(r => new DialogItem(r.Name, IsIrreversibleChange(r) ? "Chọn thêm ngoài gói — không hoàn tác được" : "Chọn thêm ngoài gói", IsIrreversibleChange(r) ? "review" : r.ImpactKey))],
            Facts =
            [
                new DialogFact("Phần mềm", installs == 0 ? "Không cài phần mềm." : $"{installs} ứng dụng, cài im lặng bằng link chính thức có chữ ký số hoặc WinGet; ứng dụng đã có sẽ được bỏ qua."),
                new DialogFact("Thay đổi Windows", changes == 0 ? "Không thay đổi cấu hình." : bitLocker
                    ? $"{changes} thay đổi, gồm tắt BitLocker. Thiết lập lưu giá trị cũ để hoàn tác (trừ dọn file và BitLocker)."
                    : $"{changes} thay đổi, đều lưu giá trị cũ để hoàn tác (trừ dọn file)."),
                new DialogFact("Kiểm tra", $"{checks} mục chỉ đọc."),
                new DialogFact("Quyền", _main.IsElevated ? "Đang chạy quyền quản trị." : "Không có quyền quản trị; một số tác vụ sẽ không chạy được."),
                new DialogFact("Khởi động lại", "Ứng dụng không tự khởi động lại. Nếu cần, hàng đợi dừng và hỏi bạn."),
            ],
            Warning = warnings.Count == 0 ? null : string.Join(" ", warnings),
            WarningIsDanger = warnings.Count > 0,
            RequireAgreement = agreements.Count > 0,
            AgreementText = agreements.Count == 0 ? null : string.Join(" ", agreements),
        });
    }

    /// <summary>Các mục đang chọn nhưng không thuộc gói (người dùng tự chọn thêm ở trang khác).</summary>
    internal static List<TaskRowViewModel> ExtrasOutsidePack(IEnumerable<TaskRowViewModel> selected, TaskPack pack) =>
        [.. selected.Where(r => !r.Entry.IsInPack(pack))];

    /// <summary>Thay đổi máy mà không hoàn tác được (xóa file, Thùng rác...).</summary>
    internal static bool IsIrreversibleChange(TaskRowViewModel row) =>
        !row.IsReadOnly && (row.Entry.Module.Descriptor.Rollback == RollbackSupport.NotPossible ||
                            row.Entry.Module.Descriptor.Risk == RiskLevel.Destructive);

    private void RefreshSteps()
    {
        var m = _main.Machine;
        var sw = _main.RowsIn(TaskCategory.Software).ToList();
        var swSelected = sw.Count(r => r.IsSelected);
        var swRun = sw.Where(r => r.HasRun).ToList();
        var win = _main.RowsIn(TaskCategory.WindowsSettings).Where(r => !r.IsReadOnly && r.HasRun).ToList();
        var ho = _main.RowsIn(TaskCategory.Handover).Where(r => r.HasRun).ToList();

        Steps[0].Description = m.IsChecking ? "Đang đọc thông tin máy…" : (m.Checked ? "Đã đọc lúc " + m.CheckedAt : "Đọc Windows, quyền, mạng, WinGet");
        Steps[0].StateKey = m.IsChecking ? "running" : (m.Checked ? "success" : "pending");

        Steps[1].Description = $"Đã chọn {swSelected} ứng dụng";
        Steps[1].StateKey = "pending";
        Steps[1].StateText = swRun.Count > 0 ? "Đã chạy" : "Chưa xác nhận";

        Steps[2].Description = swRun.Count == 0 ? "Chạy hàng đợi, xác minh từng gói" : $"{swRun.Count(r => r.State == TaskState.Success)}/{swRun.Count} gói đã xác minh";
        Steps[2].StateKey = Aggregate(swRun);
        Steps[3].Description = win.Count == 0 ? "Múi giờ, File Explorer, nguồn điện" : $"{win.Count(r => r.State == TaskState.Success)}/{win.Count} thay đổi đã xác minh";
        Steps[3].StateKey = Aggregate(win);
        Steps[4].Description = ho.Count == 0 ? "Checklist và báo cáo" : $"{ho.Count(r => r.StateKey == "pass")}/{ho.Count} mục đạt";
        Steps[4].StateKey = Aggregate(ho);

        OnPropertyChanged(nameof(StartNote));
        Commands.InvalidateAll();
    }

    private static string Aggregate(List<TaskRowViewModel> rows)
    {
        if (rows.Count == 0)
        {
            return "pending";
        }

        if (rows.Any(r => r.State is TaskState.Running or TaskState.Pending))
        {
            return "running";
        }

        if (rows.Any(r => r.State == TaskState.Failed))
        {
            return "failed";
        }

        return rows.Any(r => r.State is TaskState.NeedsAttention or TaskState.NotTested or TaskState.WaitingForReboot) ? "attention" : "success";
    }
}
