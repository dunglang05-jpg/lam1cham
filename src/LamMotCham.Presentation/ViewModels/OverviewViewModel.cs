using System.Collections.ObjectModel;
using LamMotCham.Presentation.Mvvm;
using LamMotCham.Presentation.Services;
using LamMotCham.Core.Tasks;

namespace LamMotCham.Presentation.ViewModels;

public sealed record PackOption(TaskPack Pack, string Label);

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
    private readonly MainViewModel _main;
    private PackOption _pack;

    public OverviewViewModel(MainViewModel main)
    {
        _main = main;
        Packs = [.. Enum.GetValues<TaskPack>().Select(p => new PackOption(p, p.DisplayName()))];
        _pack = Packs[0];
        Steps =
        [
            new("1", "Kiểm tra máy", Page.Overview),
            new("2", "Chọn phần mềm", Page.Software),
            new("3", "Cài đặt", Page.Software),
            new("4", "Tác vụ Windows", Page.Windows),
            new("5", "Kiểm tra bàn giao", Page.Handover),
        ];
        StartPackCommand = new RelayCommand(AskStartPack, () => main.Machine.Checked && !main.IsRunning);
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

    public ObservableCollection<StepViewModel> Steps { get; }

    public RelayCommand StartPackCommand { get; }

    public RelayCommand OpenLastReportCommand { get; }

    public RelayCommand GoCommand { get; }

    public PackOption SelectedPack
    {
        get => _pack;
        set
        {
            if (value is not null && Set(ref _pack, value))
            {
                OnPropertyChanged(nameof(StartLabel));
                foreach (var row in _main.Rows.Values)
                {
                    row.IsSelected = row.Entry.IsInPack(value.Pack);
                }
            }
        }
    }

    public string StartLabel => "Bắt đầu: " + _pack.Label;

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
        _main.ShowDialog(new ConfirmDialogViewModel(_main, "Xác nhận trước khi chạy", "shield", "Bắt đầu chạy",
            async () => await _main.RunAsync(rows, _pack.Label))
        {
            Lead = $"{rows.Count} tác vụ sẽ chạy tuần tự. Mỗi tác vụ được xác minh lại trước khi báo thành công.",
            Facts =
            [
                new DialogFact("Phần mềm", installs == 0 ? "Không cài phần mềm." : $"{installs} gói qua WinGet, cài im lặng; gói đã có sẽ được bỏ qua."),
                new DialogFact("Thay đổi Windows", changes == 0 ? "Không thay đổi cấu hình." : $"{changes} thay đổi, đều lưu giá trị cũ để hoàn tác (trừ dọn file)."),
                new DialogFact("Kiểm tra", $"{checks} mục chỉ đọc."),
                new DialogFact("Quyền", _main.IsElevated ? "Đang chạy quyền quản trị." : "Đang chạy quyền người dùng; Windows có thể hỏi UAC cho từng gói."),
                new DialogFact("Khởi động lại", "Ứng dụng không tự khởi động lại. Nếu cần, hàng đợi dừng và hỏi bạn."),
            ],
            RequireAgreement = installs > 0,
            AgreementText = "Đồng ý để Làm 1 Chạm chấp nhận điều khoản của nguồn WinGet và của từng gói thay cho người dùng máy này.",
        });
    }

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
