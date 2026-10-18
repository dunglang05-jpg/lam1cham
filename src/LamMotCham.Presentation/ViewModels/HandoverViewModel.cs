using System.Collections.ObjectModel;
using LamMotCham.Presentation.Mvvm;
using LamMotCham.Core.Tasks;
using LamMotCham.Windows;

namespace LamMotCham.Presentation.ViewModels;

public sealed class ConditionViewModel(string title) : ObservableObject
{
    private string _stateKey = "unchecked";
    private string _evidence = "";
    private bool _canReview;

    public string Title { get; } = title;

    public string StateKey { get => _stateKey; set => Set(ref _stateKey, value); }

    public string Evidence { get => _evidence; set => Set(ref _evidence, value); }

    public bool CanReview { get => _canReview; set => Set(ref _canReview, value); }
}

/// <summary>
/// Màn hình F — Kiểm tra và bàn giao (UI/UX Handoff mục 12). Không có dữ liệu thì "Chưa kiểm tra", không tự cho "Đạt".
/// </summary>
public sealed class HandoverViewModel : ObservableObject
{
    /// <summary>Mô-đun của trang này (QĐ-002).</summary>
    public LamMotCham.Presentation.Modules.AppModule Module { get; } = LamMotCham.Presentation.Modules.ModuleCatalog.Handover;

    private static readonly string[] GuardIds = ["check.security", "check.firewall", "win.update"];

    private readonly MainViewModel _main;
    private bool _issuesReviewed;
    private double _passPercent;
    private string _passText = "0%";
    private string _checklistText = "";
    private string _summaryTitle = "Chưa kiểm tra";
    private string _summaryPass = "0 Đạt";
    private string _summaryAttention = "0 Cần chú ý";
    private string _summaryUnchecked = "0 Chưa kiểm tra";

    public HandoverViewModel(MainViewModel main)
    {
        _main = main;
        Conditions =
        [
            new("Thông tin máy đã đọc"),
            new("Phần mềm thiết yếu đã xác minh"),
            new("Windows Update, Defender, Tường lửa không bị ứng dụng tắt"),
            new("Không còn tác vụ đang chạy"),
            new("Lỗi và cảnh báo đã xem"),
            new("Khởi động lại nếu cần"),
            new("Báo cáo bàn giao đã tạo"),
        ];
        var handover = main.RowsIn(TaskCategory.Handover).ToList();
        SystemChecks = [.. handover.Where(r => r.Group == WindowsCatalog.GroupHandoverSystem)];
        Hardware = [.. handover.Where(r => r.Group == WindowsCatalog.GroupHandoverHardware)];
        foreach (var r in main.Rows.Values)
        {
            r.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(TaskRowViewModel.State) or nameof(TaskRowViewModel.ManualConfirmed))
                {
                    Refresh();
                }
            };
        }

        RecheckCommand = new AsyncCommand(
            async () => await main.RunAsync([.. main.Rows.Values.Where(r => r.IsReadOnly)], "Kiểm tra bàn giao"),
            () => !main.IsRunning);
        CreateReportCommand = new RelayCommand(() => main.CreateHandoverReport(), () => !main.IsRunning);
        ReviewIssuesCommand = new RelayCommand(ReviewIssues);
        ShowErrorCommand = new RelayCommand(p =>
        {
            if (p is TaskRowViewModel row)
            {
                main.ShowRowError(row);
            }
        });
        Refresh();
    }

    public ObservableCollection<ConditionViewModel> Conditions { get; }

    public IReadOnlyList<TaskRowViewModel> SystemChecks { get; }

    public IReadOnlyList<TaskRowViewModel> Hardware { get; }

    public AsyncCommand RecheckCommand { get; }

    public RelayCommand CreateReportCommand { get; }

    public RelayCommand ReviewIssuesCommand { get; }

    public RelayCommand ShowErrorCommand { get; }

    public string SummaryPass { get => _summaryPass; private set => Set(ref _summaryPass, value); }

    public string SummaryAttention { get => _summaryAttention; private set => Set(ref _summaryAttention, value); }

    public string SummaryUnchecked { get => _summaryUnchecked; private set => Set(ref _summaryUnchecked, value); }

    public double PassPercent { get => _passPercent; private set => Set(ref _passPercent, value); }

    public string PassText { get => _passText; private set => Set(ref _passText, value); }

    public string ChecklistText { get => _checklistText; private set => Set(ref _checklistText, value); }

    public string SummaryTitle { get => _summaryTitle; private set => Set(ref _summaryTitle, value); }

    public void Refresh()
    {
        if (Conditions is null)
        {
            return;
        }

        var m = _main.Machine;
        var c = Conditions;
        c[0].StateKey = m.Checked ? "pass" : "unchecked";
        c[0].Evidence = m.Checked ? "Đọc lúc " + m.CheckedAt : "Chưa đọc thông tin máy.";

        var sw = _main.RowsIn(TaskCategory.Software).Where(r => r.HasRun).ToList();
        var swOk = sw.Count(r => r.State == TaskState.Success);
        c[1].StateKey = sw.Count == 0 ? "unchecked" : (swOk == sw.Count ? "pass" : "attention");
        c[1].Evidence = sw.Count == 0
            ? "Chưa cài phần mềm trong phiên này."
            : $"{swOk}/{sw.Count} gói đã xác minh." + (swOk < sw.Count ? " Chưa đạt: " + string.Join(", ", sw.Where(r => r.State != TaskState.Success).Select(r => r.Name)) + "." : "");

        var guards = GuardIds
            .Select(id => _main.Rows.TryGetValue(id, out var r) ? r : null).Where(r => r is not null).Cast<TaskRowViewModel>().ToList();
        var guardsRun = guards.Where(r => r.HasRun).ToList();
        c[2].StateKey = guardsRun.Count < guards.Count ? "unchecked" : (guardsRun.All(r => r.State == TaskState.Success) ? "pass" : "attention");
        c[2].Evidence = guardsRun.Count < guards.Count
            ? "Ứng dụng không có tác vụ tắt các dịch vụ này. Chạy kiểm tra để xác nhận trạng thái."
            : string.Join(" · ", guardsRun.Select(r => r.Message ?? r.Name));

        c[3].StateKey = _main.IsRunning ? "attention" : "pass";
        c[3].Evidence = _main.IsRunning ? "Đang có tác vụ chạy." : "Hàng đợi trống.";

        var issues = _main.Rows.Values.Count(r => r.IsProblem);
        c[4].StateKey = issues == 0 ? (_main.Rows.Values.Any(r => r.HasRun) ? "pass" : "unchecked") : (_issuesReviewed ? "pass" : "unchecked");
        c[4].Evidence = issues == 0 ? "Không có lỗi hoặc cảnh báo." : (_issuesReviewed ? $"Đã xem {issues} mục." : $"{issues} mục cần chú ý chưa được mở.");
        c[4].CanReview = issues > 0;

        var waiting = _main.Rows.Values.Any(r => r.State == TaskState.WaitingForReboot);
        var pending = m.Snapshot?.Array("pendingReboot").Count ?? 0;
        c[5].StateKey = m.Snapshot is null && !waiting ? "unchecked" : (waiting || pending > 0 ? "attention" : "pass");
        c[5].Evidence = waiting ? "Một tác vụ đang chờ khởi động lại." : pending > 0 ? "Windows đang chờ khởi động lại để hoàn tất cập nhật." : (m.Snapshot is null ? "Chưa kiểm tra." : "Không có yêu cầu khởi động lại.");

        c[6].StateKey = _main.LastHandoverReportAt is null ? "unchecked" : "pass";
        c[6].Evidence = _main.LastHandoverReportAt is { } at ? "Đã lưu lúc " + at.ToString("HH:mm dd/MM", System.Globalization.CultureInfo.InvariantCulture) : "Chưa tạo.";

        var keys = c.Select(x => x.StateKey).Concat(SystemChecks.Concat(Hardware).Select(r => r.StateKey)).ToList();
        SummaryPass = keys.Count(k => k == "pass") + " Đạt";
        SummaryAttention = keys.Count(k => k is "attention" or "failed") + " Cần chú ý";
        SummaryUnchecked = keys.Count(k => k is "unchecked" or "manual" or "pending") + " Chưa kiểm tra";

        // Vòng tròn "Tóm tắt bàn giao": tỷ lệ mục đạt trên tổng (điều kiện + kiểm tra hệ thống + phần cứng).
        var pass = keys.Count(k => k == "pass");
        PassPercent = keys.Count == 0 ? 0 : pass * 100.0 / keys.Count;
        PassText = $"{PassPercent:0}%";
        ChecklistText = $"{pass}/{keys.Count} đạt";
        SummaryTitle = pass == keys.Count ? "Sẵn sàng bàn giao"
            : keys.Any(k => k is "attention" or "failed") ? "Có mục cần chú ý"
            : pass == 0 ? "Chưa kiểm tra" : "Đang kiểm tra";
    }

    private void ReviewIssues()
    {
        var items = _main.Rows.Values.Where(r => r.IsProblem)
            .Select(r => new DialogItem(r.Name, r.Message, r.StateKey, r.StateText)).ToList();
        _issuesReviewed = true;
        _main.ShowDialog(new ListDialogViewModel(_main, "Các mục cần chú ý", "alert", items,
            items.Count == 0 ? "Không có mục nào." : "Xử lý từng mục theo hướng dẫn, sau đó bấm “Kiểm tra lại”."));
        Refresh();
    }
}
