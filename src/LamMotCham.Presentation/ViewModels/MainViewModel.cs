using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using LamMotCham.Presentation.Mvvm;
using LamMotCham.Presentation.Services;
using LamMotCham.Core.Logging;
using LamMotCham.Core.Orchestration;
using LamMotCham.Core.Reporting;
using LamMotCham.Core.Tasks;
using LamMotCham.Windows.Infrastructure;
using LamMotCham.Windows.Probe;

namespace LamMotCham.Presentation.ViewModels;

public enum Page
{
    Overview,
    Software,
    Windows,
    Disk,
    Cleanup,
    Handover,
}

public sealed class NavItem(MainViewModel owner, Page page, string label, string iconKey) : ObservableObject
{
    public Page Page { get; } = page;

    public string Label { get; } = label;

    public string IconKey { get; } = iconKey;

    public bool IsSelected
    {
        get => owner.CurrentPage == Page;
        set
        {
            if (value)
            {
                owner.CurrentPage = Page;
            }
        }
    }

    internal void Refresh() => OnPropertyChanged(nameof(IsSelected));
}

public sealed record RecentItem(string Name, string Message, string Time, string StateKey);

/// <summary>Các thao tác cần nền tảng giao diện (WPF cung cấp).</summary>
public interface IUiPlatform
{
    void Shutdown();

    void SetClipboard(string text);
}

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    private readonly IUiPlatform _ui;
    private Page _page = Page.Overview;
    private object? _dialog;
    private bool _isRunning;
    private CancellationTokenSource? _runCts;
    private DateTimeOffset _runStarted;
    private string _dockTitle = "";
    private string _dockStep = "";
    private string _dockSummary = "";
    private bool _dockVisible;
    private bool _logOpen;
    private string _logText = "";
    private int _runTotal;
    private int _runDone;
    private bool _cancelRequested;

    public MainViewModel(AppServices services, IUiPlatform ui)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(ui);
        S = services;
        _ui = ui;
        Rows = services.Catalog.Select(e => new TaskRowViewModel(e)).ToDictionary(r => r.Id, StringComparer.Ordinal);
        Nav =
        [
            new(this, Page.Overview, "Tổng quan", "home"),
            new(this, Page.Software, "Phần mềm thiết yếu", "download"),
            new(this, Page.Windows, "Tối ưu Windows", "gear"),
            new(this, Page.Disk, "Ổ đĩa & tài khoản", "disk"),
            new(this, Page.Cleanup, "Dọn dẹp & hiệu năng", "sparkle"),
            new(this, Page.Handover, "Kiểm tra & bàn giao", "clipboard"),
        ];

        Machine = new MachineViewModel(this);
        Overview = new OverviewViewModel(this);
        Software = new SoftwareViewModel(this);
        Windows = new WindowsViewModel(this);
        Disk = new DiskViewModel(this);
        Cleanup = new CleanupViewModel(this);
        Handover = new HandoverViewModel(this);

        NavigateCommand = new RelayCommand(p =>
        {
            if (p is Page page)
            {
                CurrentPage = page;
            }
        });
        ElevateCommand = new RelayCommand(Elevate, () => !S.IsElevated && !IsRunning);
        OpenLogsCommand = new RelayCommand(OpenLogs);
        OpenReportsCommand = new RelayCommand(() => ShellActions.OpenFolder(WindowsEnvironment.ReportsDirectory));
        HelpCommand = new RelayCommand(() => ShowDialog(new ListDialogViewModel(this, "Trợ giúp", "info",
        [
            new DialogItem("Quy trình", "Kiểm tra máy → chọn phần mềm → cài đặt → tác vụ Windows → kiểm tra bàn giao."),
            new DialogItem("Trạng thái", "Thành công chỉ hiện khi ứng dụng đã đọc lại và xác minh. Cần chú ý nghĩa là cần người xem."),
            new DialogItem("An toàn", "Ứng dụng không tắt Defender, Tường lửa, Windows Update; không tự phân vùng ổ đĩa hay khởi động lại."),
            new DialogItem("Nhật ký", WindowsEnvironment.LogsDirectory),
            new DialogItem("Báo cáo", WindowsEnvironment.ReportsDirectory),
            new DialogItem("Phiên bản", S.Version + " · bản thử nghiệm nội bộ"),
        ])));
        ToggleLogCommand = new RelayCommand(() => LogOpen = !LogOpen);
        CancelRunCommand = new RelayCommand(AskCancel, () => IsRunning && !_cancelRequested);
        HideDockCommand = new RelayCommand(() => DockVisible = false, () => !IsRunning);

        AsyncCommand.ErrorHandler = ex =>
        {
            S.Log.Error("Lỗi không mong đợi: " + ex);
            ShowDialog(new ErrorDialogViewModel(this, "Có lỗi không mong đợi", ex.Message,
                "Thao tác vừa rồi chưa hoàn tất.", "Mở log để xem chi tiết, rồi thử lại.", ex.GetType().FullName + ": " + ex.Message));
        };
    }

    public AppServices S { get; }

    public IReadOnlyDictionary<string, TaskRowViewModel> Rows { get; }

    public ObservableCollection<NavItem> Nav { get; }

    public ObservableCollection<RecentItem> Recent { get; } = [];

    public MachineViewModel Machine { get; }

    public OverviewViewModel Overview { get; }

    public SoftwareViewModel Software { get; }

    public WindowsViewModel Windows { get; }

    public DiskViewModel Disk { get; }

    public CleanupViewModel Cleanup { get; }

    public HandoverViewModel Handover { get; }

    public RelayCommand NavigateCommand { get; }

    public RelayCommand ElevateCommand { get; }

    public RelayCommand OpenLogsCommand { get; }

    public RelayCommand OpenReportsCommand { get; }

    public RelayCommand HelpCommand { get; }

    public RelayCommand ToggleLogCommand { get; }

    public RelayCommand CancelRunCommand { get; }

    public RelayCommand HideDockCommand { get; }

    public string AppVersion => "v" + S.Version;

    public bool IsElevated => S.IsElevated;

    public string WindowsLabel => Machine.WindowsShort;

    public IUiPlatform Ui => _ui;

    public Page CurrentPage
    {
        get => _page;
        set
        {
            if (Set(ref _page, value))
            {
                OnPropertyChanged(nameof(CurrentPageViewModel));
                foreach (var n in Nav)
                {
                    n.Refresh();
                }
            }
        }
    }

    public object CurrentPageViewModel => _page switch
    {
        Page.Software => Software,
        Page.Windows => Windows,
        Page.Disk => Disk,
        Page.Cleanup => Cleanup,
        Page.Handover => Handover,
        _ => Overview,
    };

    public object? Dialog
    {
        get => _dialog;
        private set
        {
            if (Set(ref _dialog, value))
            {
                OnPropertyChanged(nameof(HasDialog));
            }
        }
    }

    public bool HasDialog => _dialog is not null;

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (Set(ref _isRunning, value))
            {
                CommandManagerRefresh();
                Handover.Refresh();
            }
        }
    }

    public bool DockVisible
    {
        get => _dockVisible;
        set => Set(ref _dockVisible, value);
    }

    public string DockTitle
    {
        get => _dockTitle;
        private set => Set(ref _dockTitle, value);
    }

    public string DockStep
    {
        get => _dockStep;
        private set => Set(ref _dockStep, value);
    }

    public string DockSummary
    {
        get => _dockSummary;
        private set => Set(ref _dockSummary, value);
    }

    public bool LogOpen
    {
        get => _logOpen;
        set
        {
            if (Set(ref _logOpen, value))
            {
                RefreshLog();
            }
        }
    }

    public string LogText
    {
        get => _logText;
        private set => Set(ref _logText, value);
    }

    public string? LastRunReportPath { get; private set; }

    public string? LastHandoverReportPath { get; private set; }

    public DateTimeOffset? LastHandoverReportAt { get; private set; }

    public void ShowDialog(DialogViewModel dialog) => Dialog = dialog;

    public void CloseDialog(DialogViewModel dialog)
    {
        if (ReferenceEquals(Dialog, dialog))
        {
            Dialog = null;
        }
    }

    public void OpenLogs()
    {
        S.Log.Info("Mở thư mục log.");
        ShellActions.OpenFolder(WindowsEnvironment.LogsDirectory);
    }

    public void RestartComputer()
    {
        S.Log.Info("Người dùng chọn khởi động lại máy.");
        ShellActions.RestartComputer();
    }

    public IEnumerable<TaskRowViewModel> RowsIn(TaskCategory category) =>
        Rows.Values.Where(r => r.Entry.Category == category);

    /// <summary>
    /// Chạy các task đã chọn qua orchestrator. Trả về báo cáo, hoặc null nếu đang có lượt khác.
    /// </summary>
    public async Task<RunReport?> RunAsync(IReadOnlyList<TaskRowViewModel> rows, string title)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (IsRunning || rows.Count == 0)
        {
            return null;
        }

        IsRunning = true;
        _cancelRequested = false;
        _runCts = new CancellationTokenSource();
        _runStarted = DateTimeOffset.Now;
        _runTotal = rows.Count;
        _runDone = 0;
        DockVisible = true;
        DockTitle = title;
        DockSummary = "";
        DockStep = $"Bước 1/{_runTotal}";
        foreach (var r in rows)
        {
            r.ResetForRun();
        }

        S.Inventory.Invalidate();
        var byId = rows.ToDictionary(r => r.Id, StringComparer.Ordinal);
        var progress = new Progress<TaskStatusUpdate>(u =>
        {
            if (!byId.TryGetValue(u.TaskId, out var row))
            {
                return;
            }

            row.Apply(u);
            if (u.State == TaskState.Running)
            {
                DockTitle = (row.Entry.Category == TaskCategory.Software ? "Đang cài " : "Đang chạy: ") + row.Name;
            }
            else if (u.State.IsTerminal())
            {
                _runDone = rows.Count(x => x.State is { } s && s.IsTerminal());
            }

            DockStep = $"Bước {Math.Min(_runDone + 1, _runTotal)}/{_runTotal}";
        });

        RunReport? report = null;
        try
        {
            var modules = rows.Select(r => r.Entry.Module).ToList();
            var token = _runCts.Token;
            report = await Task.Run(() => S.Orchestrator.RunAsync(modules, progress, token));
            foreach (var outcome in report.Tasks)
            {
                if (byId.TryGetValue(outcome.TaskId, out var row))
                {
                    row.Apply(outcome);
                }
            }

            LastRunReportPath = WriteReport(report, "lượt chạy");
            AddRecent(report);
            DockSummary = Summarize(report);
        }
        catch (QueueValidationException ex)
        {
            ShowDialog(new ErrorDialogViewModel(this, "Không thể bắt đầu", "Danh sách tác vụ không hợp lệ.",
                "Chưa có tác vụ nào được chạy.", "Bỏ chọn các mục liên quan rồi thử lại.", string.Join(Environment.NewLine, ex.Errors)));
        }
        finally
        {
            IsRunning = false;
            DockTitle = report is null ? "Không chạy được" : "Đã xử lý xong";
            DockStep = $"{_runTotal}/{_runTotal} tác vụ · {(DateTimeOffset.Now - _runStarted).ToString(@"mm\:ss", CultureInfo.InvariantCulture)}";
            _runCts.Dispose();
            _runCts = null;
            RefreshLog();
            Handover.Refresh();
            Software.RefreshQueue();
        }

        if (report?.HaltReason == RunHaltReason.RebootRequired)
        {
            var waiting = report.Tasks.FirstOrDefault(t => t.State == TaskState.WaitingForReboot);
            ShowDialog(new RestartDialogViewModel(this, waiting?.Reason ?? "Một tác vụ cần khởi động lại để hoàn tất.",
                report.Count(TaskState.Pending)));
        }
        else if (report?.HaltReason == RunHaltReason.UnresponsiveTask)
        {
            var stuck = report.Tasks.First(t => t.State == TaskState.NeedsAttention);
            ShowDialog(new ErrorDialogViewModel(this, "Hàng đợi đã dừng", stuck.Reason ?? "Một tác vụ không kết thúc đúng hạn.",
                "Các tác vụ phía sau chưa chạy để tránh chạy chồng.", "Kiểm tra máy (trình cài đặt có đang mở?), sau đó chạy lại.",
                $"{stuck.DisplayName} ({stuck.TaskId})", "attention"));
        }

        return report;
    }

    public void ShowRowError(TaskRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var sw = row.Entry.Category == TaskCategory.Software;
        ShowDialog(new ErrorDialogViewModel(
            this,
            (sw ? "Không cài được " : "Không hoàn tất: ") + row.Name,
            row.Message ?? "Tác vụ không thành công.",
            (sw ? row.Name + " chưa được cài." : "Thay đổi này chưa được áp dụng.") + " Các tác vụ khác vẫn tiếp tục.",
            row.State == TaskState.NeedsAttention
                ? "Kiểm tra lại thủ công trên máy, sau đó chạy lại mục này."
                : "Kiểm tra kết nối mạng và quyền quản trị, sau đó chạy lại mục này.",
            $"Tác vụ: {row.Id}{Environment.NewLine}Trạng thái: {row.State}{Environment.NewLine}Số lần thử: {row.Attempts}{Environment.NewLine}Chi tiết: {row.Message}{Environment.NewLine}Log: {WindowsEnvironment.LogsDirectory}",
            row.StateKey));
    }

    /// <summary>Tạo báo cáo bàn giao từ trạng thái hiện tại của mọi mục đã kiểm tra.</summary>
    public string? CreateHandoverReport()
    {
        var outcomes = Rows.Values.Where(r => r.HasRun || r.ManualConfirmed).Select(r => r.ToOutcome()).ToList();
        var now = DateTimeOffset.Now;
        var report = new RunReport { StartedAt = now, FinishedAt = now, Tasks = outcomes };
        var path = WriteReport(report, "bàn giao");
        if (path is not null)
        {
            LastHandoverReportPath = path;
            LastHandoverReportAt = now;
            ShellActions.OpenFile(path);
        }

        Handover.Refresh();
        return path;
    }

    public void Elevate()
    {
        if (!ShellActions.RestartElevated())
        {
            ShowDialog(new ErrorDialogViewModel(this, "Chưa nâng quyền", "Windows chưa cấp quyền quản trị (UAC bị từ chối).",
                "Ứng dụng tiếp tục chạy với quyền người dùng; một số tác vụ có thể hỏi quyền riêng hoặc bị bỏ qua.",
                "Bấm “Nâng quyền…” lại và chọn Yes trên hộp thoại của Windows.", null, "attention"));
            return;
        }

        _ui.Shutdown();
    }

    private void AskCancel()
    {
        ShowDialog(new ConfirmDialogViewModel(this, "Dừng lượt chạy?", "alert", "Dừng sau bước hiện tại", () =>
        {
            _cancelRequested = true;
            _runCts?.Cancel();
            DockSummary = "Đang dừng sau bước hiện tại…";
            return Task.CompletedTask;
        }, "Tiếp tục chạy")
        {
            Lead = "Tác vụ đang chạy sẽ được hoàn tất trước. Trình cài đặt bên ngoài không bị cưỡng chế dừng; các tác vụ còn lại sẽ ở trạng thái Đã hủy.",
        });
    }

    private string? WriteReport(RunReport report, string kind)
    {
        try
        {
            var files = ReportWriter.Write(WindowsEnvironment.ReportsDirectory, report, Machine.ReportFacts(), S.Version);
            S.Log.Info($"Đã lưu báo cáo {kind}: {files.HtmlPath}");
            return files.HtmlPath;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            S.Log.Error("Không lưu được báo cáo: " + ex.Message);
            return null;
        }
    }

    private void AddRecent(RunReport report)
    {
        foreach (var t in report.Tasks.Where(t => t.State != TaskState.Pending).Reverse().Take(6))
        {
            Recent.Insert(0, new RecentItem(t.DisplayName, t.Reason ?? "", (t.FinishedAt ?? DateTimeOffset.Now).ToLocalTime().ToString("HH:mm · dd/MM", Vi), StateKeyOf(t.State, t.Kind)));
        }

        while (Recent.Count > 8)
        {
            Recent.RemoveAt(Recent.Count - 1);
        }
    }

    private static string StateKeyOf(TaskState state, TaskKind kind) => state switch
    {
        TaskState.Success => kind == TaskKind.Check ? "pass" : "success",
        TaskState.Failed => "failed",
        TaskState.NeedsAttention => "attention",
        TaskState.Skipped => "skipped",
        TaskState.Cancelled => "cancelled",
        TaskState.WaitingForReboot => "reboot",
        TaskState.NotTested => "manual",
        TaskState.NotSupported => "notsupported",
        _ => "pending",
    };

    private static string Summarize(RunReport r)
    {
        var parts = new List<string>();
        void Add(TaskState s, string label)
        {
            var n = r.Count(s);
            if (n > 0)
            {
                parts.Add($"{n} {label}");
            }
        }

        Add(TaskState.Success, "thành công");
        Add(TaskState.NeedsAttention, "cần chú ý");
        Add(TaskState.Failed, "thất bại");
        Add(TaskState.NotTested, "cần kiểm tra tay");
        Add(TaskState.Skipped, "bỏ qua");
        Add(TaskState.Cancelled, "đã hủy");
        Add(TaskState.NotSupported, "không hỗ trợ");
        Add(TaskState.WaitingForReboot, "chờ khởi động lại");
        return string.Join(" · ", parts);
    }

    private void OnTick()
    {
        Machine.RefreshResources();
        if (!IsRunning)
        {
            return;
        }

        foreach (var r in Rows.Values.Where(r => r.State == TaskState.Running))
        {
            r.Tick();
        }

        DockStep = $"Bước {Math.Min(_runDone + 1, _runTotal)}/{_runTotal} · {(DateTimeOffset.Now - _runStarted).ToString(@"mm\:ss", CultureInfo.InvariantCulture)}";
        if (LogOpen)
        {
            RefreshLog();
        }
    }

    private void RefreshLog()
    {
        if (!LogOpen)
        {
            return;
        }

        var sb = new StringBuilder();
        foreach (var e in S.Memory.Snapshot().TakeLast(200))
        {
            sb.Append(e.Timestamp.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture)).Append("  ")
              .Append((e.TaskId ?? "app").PadRight(18)).Append(' ')
              .Append(e.Level.ToString().ToUpperInvariant().PadRight(8)).Append(' ')
              .AppendLine(e.Message);
        }

        LogText = sb.ToString();
    }

    private static void CommandManagerRefresh() => Commands.InvalidateAll();

    public void Dispose()
    {
        _runCts?.Dispose();
        S.Dispose();
    }

    public void NotifyWindowsLabel() => OnPropertyChanged(nameof(WindowsLabel));

    /// <summary>Cập nhật tài nguyên và thời gian chạy theo nhịp; tiếp tục trên luồng giao diện đã gọi.</summary>
    public async void StartTicker()
    {
        while (true)
        {
            await Task.Delay(700);
            OnTick();
        }
    }
}
