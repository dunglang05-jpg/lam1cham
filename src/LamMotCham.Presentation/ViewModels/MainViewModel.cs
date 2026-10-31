using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using LamMotCham.Presentation.Mvvm;
using LamMotCham.Presentation.Services;
using LamMotCham.Core.Account;
using LamMotCham.Core.Logging;
using LamMotCham.Core.Orchestration;
using LamMotCham.Core.Reporting;
using LamMotCham.Core.Tasks;
using LamMotCham.Windows.Infrastructure;
using LamMotCham.Windows.Probe;
using LamMotCham.Windows.Software;

namespace LamMotCham.Presentation.ViewModels;

public enum Page
{
    Overview,
    Software,
    Windows,
    Disk,
    Cleanup,
    Handover,
    Modules,
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

/// <summary>Một kết quả của ô "Tìm tính năng": bấm để mở đúng trang.</summary>
public sealed record SearchHit(string Title, string Where, Page Page);

/// <summary>Các thao tác cần nền tảng giao diện (WPF cung cấp).</summary>
public interface IUiPlatform
{
    void Shutdown();

    void SetClipboard(string text);

    /// <summary>Đăng xuất và mở lại phần mềm ở màn hình đăng nhập.</summary>
    void RestartForSignIn();
}

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    private readonly IUiPlatform _ui;
    private Page _page = Page.Overview;
    private string _searchText = "";
    private RelayCommand? _openSearchHit;
    private readonly List<(string Key, SearchHit Hit)> _searchIndex;
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
    private bool _closeAfterRun;
    private double _runPercent;
    private int _doneSuccess;
    private int _doneSkipped;
    private int _doneProblems;

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
            new(this, Page.Windows, "Cài đặt & Tối ưu", "gear"),
            new(this, Page.Disk, "Ổ đĩa & Tài khoản", "disk"),
            new(this, Page.Cleanup, "Dọn dẹp & Hiệu năng", "sparkle"),
            new(this, Page.Handover, "Kiểm tra & Bàn giao", "clipboard"),
            new(this, Page.Modules, "Mô-đun & Cập nhật", "puzzle"),
        ];
        _searchIndex = BuildSearchIndex();

        Machine = new MachineViewModel(this);
        Overview = new OverviewViewModel(this);
        Software = new SoftwareViewModel(this);
        Windows = new WindowsViewModel(this);
        Disk = new DiskViewModel(this);
        Cleanup = new CleanupViewModel(this);
        Handover = new HandoverViewModel(this);
        Modules = new ModulesViewModel(this);

        GoModulesCommand = new RelayCommand(() => CurrentPage = Page.Modules);
        OpenRunReportCommand = new RelayCommand(() => ShellActions.OpenFile(LastRunReportPath ?? ""), () => LastRunReportPath is not null && !IsRunning);
        NavigateCommand = new RelayCommand(p =>
        {
            if (p is Page page)
            {
                CurrentPage = page;
            }
        });
        OpenLogsCommand = new RelayCommand(OpenLogs);
        OpenReportsCommand = new RelayCommand(() => ShellActions.OpenFolder(WindowsEnvironment.ReportsDirectory));
        HelpCommand = new RelayCommand(() => ShowDialog(new ListDialogViewModel(this, "Trợ giúp", "info",
        [
            new DialogItem("Quy trình", "Đọc thông tin máy → chọn phần mềm → cài đặt → tác vụ Windows → kiểm tra bàn giao."),
            new DialogItem("Trạng thái", "Thành công chỉ hiện khi ứng dụng đã đọc lại và xác minh. Cần chú ý nghĩa là cần người xem."),
            new DialogItem("An toàn", "Ứng dụng không tắt Defender, Tường lửa, Windows Update; không tự phân vùng ổ đĩa hay khởi động lại."),
            new DialogItem("Nhật ký", WindowsEnvironment.LogsDirectory),
            new DialogItem("Báo cáo", WindowsEnvironment.ReportsDirectory),
            new DialogItem("Phiên bản", S.Version + " · bản thử nghiệm nội bộ"),
        ])));
        ToggleLogCommand = new RelayCommand(() => LogOpen = !LogOpen);
        CancelRunCommand = new RelayCommand(AskCancel, () => IsRunning && !_cancelRequested);
        HideDockCommand = new RelayCommand(() => DockVisible = false, () => !IsRunning);
        SignOutCommand = new RelayCommand(AskSignOut, () => !IsRunning);

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

    /// <summary>4 tác vụ gần nhất (Tổng quan › Hoạt động gần đây).</summary>
    public ObservableCollection<RecentItem> Recent { get; } = [];

    public bool HasRecent => Recent.Count > 0;

    public MachineViewModel Machine { get; }

    public OverviewViewModel Overview { get; }

    public SoftwareViewModel Software { get; }

    public WindowsViewModel Windows { get; }

    public DiskViewModel Disk { get; }

    public CleanupViewModel Cleanup { get; }

    public HandoverViewModel Handover { get; }

    public ModulesViewModel Modules { get; }

    public RelayCommand GoModulesCommand { get; }

    /// <summary>Tên trang hiện tại cho đường dẫn trên thanh trên cùng.</summary>
    public string CurrentPageTitle => Nav.FirstOrDefault(n => n.Page == _page)?.Label ?? "Tổng quan";

    /// <summary>Ô "Tìm tính năng": gõ tên tính năng/tác vụ, bấm kết quả để mở trang chứa nó.</summary>
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (Set(ref _searchText, value))
            {
                UpdateSearch();
            }
        }
    }

    public ObservableCollection<SearchHit> SearchHits { get; } = [];

    public bool HasSearchHits => SearchHits.Count > 0;

    public bool SearchNoHits => _searchText.Trim().Length > 0 && SearchHits.Count == 0;

    public bool SearchOpen => _searchText.Trim().Length > 0;

    public RelayCommand OpenSearchHitCommand => _openSearchHit ??= new RelayCommand(p =>
    {
        var hit = p as SearchHit ?? SearchHits.FirstOrDefault();
        if (hit is not null)
        {
            CurrentPage = hit.Page;
            SearchText = "";
        }
    });

    public RelayCommand OpenRunReportCommand { get; }

    public bool ModulesSelected => _page == Page.Modules;

    public string ModuleCountText { get; } = $"{LamMotCham.Presentation.Modules.ModuleCatalog.CodeModuleCount} mô-đun";

    /// <summary>Các tác vụ của lượt chạy hiện tại (hộp Đang thực hiện).</summary>
    public ObservableCollection<TaskRowViewModel> RunRows { get; } = [];

    public double RunPercent
    {
        get => _runPercent;
        private set
        {
            if (Set(ref _runPercent, value))
            {
                OnPropertyChanged(nameof(RunPercentText));
            }
        }
    }

    public string RunPercentText => RunPercent.ToString("0", CultureInfo.InvariantCulture) + "%";

    public int DoneSuccess { get => _doneSuccess; private set => Set(ref _doneSuccess, value); }

    public int DoneSkipped { get => _doneSkipped; private set => Set(ref _doneSkipped, value); }

    public int DoneProblems
    {
        get => _doneProblems;
        private set
        {
            if (Set(ref _doneProblems, value))
            {
                OnPropertyChanged(nameof(DoneClean));
                OnPropertyChanged(nameof(DoneHeadline));
            }
        }
    }

    public bool DoneClean => _doneProblems == 0;

    public string DoneHeadline => _doneProblems == 0 ? "Đã hoàn tất!" : "Đã xong — có mục cần xem";

    public RelayCommand NavigateCommand { get; }

    public RelayCommand OpenLogsCommand { get; }

    public RelayCommand OpenReportsCommand { get; }

    public RelayCommand HelpCommand { get; }

    public RelayCommand ToggleLogCommand { get; }

    public RelayCommand CancelRunCommand { get; }

    public RelayCommand HideDockCommand { get; }

    /// <summary>Mô-đun "account": đăng xuất (phiên chỉ nằm trong bộ nhớ; thoát phần mềm cũng tự đăng xuất).</summary>
    public RelayCommand SignOutCommand { get; }

    public string AccountName => S.Account.Current?.DisplayName ?? "";

    public string AccountRoleText => S.Account.Current is { } s ? s.RoleText + " · " + s.Username : "";

    public string AccountInitial => S.Account.Current?.Initial ?? "?";

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
                OnPropertyChanged(nameof(ModulesSelected));
                OnPropertyChanged(nameof(CurrentPageTitle));
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
        Page.Modules => Modules,
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

    private void AskSignOut() => ShowDialog(new ConfirmDialogViewModel(this, "Đăng xuất?", "user", "Đăng xuất",
        () =>
        {
            _ui.RestartForSignIn();
            return Task.CompletedTask;
        },
        "Ở lại")
    {
        Lead = "Phần mềm sẽ mở lại ở màn hình đăng nhập. Nhật ký và báo cáo đã lưu không bị xóa.",
    });

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
    /// <param name="rows">Các task cần chạy.</param>
    /// <param name="title">Tên lượt chạy.</param>
    /// <param name="quiet">true: tự đóng hộp kết quả nếu mọi tác vụ ổn (dùng cho lượt chỉ đọc như Kiểm tra máy).</param>
    /// <param name="confirmed">true: người dùng vừa xác nhận đúng danh sách này trong hộp xác nhận (bắt buộc với task cần xác nhận riêng).</param>
    public async Task<RunReport?> RunAsync(IReadOnlyList<TaskRowViewModel> rows, string title, bool quiet = false, bool confirmed = false)
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
        RunPercent = 0;
        DoneSuccess = 0;
        DoneSkipped = 0;
        DoneProblems = 0;
        RunRows.Clear();
        foreach (var r in rows)
        {
            r.ResetForRun();
            RunRows.Add(r);
        }

        // Lượt chỉ đọc dùng lại dữ liệu đã nạp nếu còn mới (vài phút); lượt có thay đổi máy luôn đọc lại sau đó.
        var changesMachine = rows.Any(r => !r.IsReadOnly);
        if (changesMachine)
        {
            S.Inventory.Invalidate();
        }

        var byId = rows.ToDictionary(r => r.Id, StringComparer.Ordinal);
        var progress = new Progress<TaskStatusUpdate>(u =>
        {
            if (!byId.TryGetValue(u.TaskId, out var row))
            {
                return;
            }

            // Báo tiến độ đến trễ sau khi tác vụ đã xong thì bỏ qua (không quay lại "Đang chạy").
            if (u.State == TaskState.Running && row.State is { } current && current.IsTerminal())
            {
                return;
            }

            row.Apply(u);
            if (u.State == TaskState.Running)
            {
                // Tiêu đề chỉ ghi tác vụ đang chạy; bước chi tiết nằm trên dòng tác vụ (PO: rối mắt khi ghi cả hai nơi).
                DockTitle = (row.Entry.Category == TaskCategory.Software ? "Đang cài " : "Đang chạy: ") + row.Name;

                // Thanh tiến trình chạy theo tiến độ bên trong tác vụ (tải, cài, xác minh), không chỉ khi xong cả tác vụ.
                var done = rows.Count(x => x.State is { } s && s.IsTerminal());
                RunPercent = Math.Min(99, (done + (u.Fraction ?? 0)) * 100.0 / _runTotal);
            }
            else if (u.State == TaskState.Pending && !string.IsNullOrWhiteSpace(u.Message))
            {
                // Ví dụ: "Đang chờ RAM trống: còn 0,6 GB (7%)…" — để người dùng biết vì sao chưa chạy.
                DockTitle = u.Message;
            }
            else if (u.State.IsTerminal())
            {
                _runDone = rows.Count(x => x.State is { } s && s.IsTerminal());
                RunPercent = _runDone * 100.0 / _runTotal;
            }

            DockStep = $"Bước {Math.Min(_runDone + 1, _runTotal)}/{_runTotal}";
        });

        RunReport? report = null;
        // Tải trước file cài của các app cài bằng link trong lúc app khác đang cài (PO duyệt 01/11/2026).
        var prefetch = LinkPrefetcher.Begin(rows.Select(r => r.App).OfType<AppInstallTask>(), m => S.Log.Info(m));
        try
        {
            var modules = rows.Select(r => r.Entry.Module).ToList();
            var token = _runCts.Token;
            report = await Task.Run(() => S.Orchestrator.RunAsync(modules, confirmed, progress, token));
            foreach (var outcome in report.Tasks)
            {
                if (byId.TryGetValue(outcome.TaskId, out var row))
                {
                    row.Apply(outcome);
                }
            }

            if (changesMachine)
            {
                S.Inventory.Invalidate();
            }

            LastRunReportPath = WriteReport(report, "lượt chạy");
            AddRecent(report);
            SendInstallReport(rows, report);
            DockSummary = Summarize(report);
            RunPercent = 100;
            DoneSuccess = report.Count(TaskState.Success);
            DoneSkipped = report.Count(TaskState.Skipped);
            DoneProblems = report.Count(TaskState.Failed) + report.Count(TaskState.NeedsAttention);
        }
        catch (QueueValidationException ex)
        {
            ShowDialog(new ErrorDialogViewModel(this, "Không thể bắt đầu", "Danh sách tác vụ không hợp lệ.",
                "Chưa có tác vụ nào được chạy.", "Bỏ chọn các mục liên quan rồi thử lại.", string.Join(Environment.NewLine, ex.Errors)));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Lượt chạy dừng giữa chừng vì lỗi không lường trước: không để dòng tác vụ treo ở "Đang chạy" và vẫn ghi báo cáo.
            S.Log.Error($"Lượt chạy dừng bất thường: {ex.GetType().Name}: {ex.Message}");
            foreach (var r in rows.Where(r => r.State is null or TaskState.Pending or TaskState.Running))
            {
                r.Apply(new TaskStatusUpdate(r.Id, TaskState.NeedsAttention, "Lượt chạy dừng bất thường; tác vụ này chưa được xác minh.", 0));
            }

            var partial = new RunReport { StartedAt = _runStarted, FinishedAt = DateTimeOffset.Now, Tasks = [.. rows.Select(r => r.ToOutcome())] };
            LastRunReportPath = WriteReport(partial, "lượt chạy dừng bất thường");

            // Vẫn gửi Lịch sử cài để Admin thấy ứng dụng lỗi/dở dang của lượt này (PO duyệt 30/10/2026).
            SendInstallReport(rows, partial);
            DockSummary = "Lượt chạy dừng bất thường — xem log.";
            ShowDialog(new ErrorDialogViewModel(this, "Lượt chạy dừng bất thường", ex.Message,
                "Các tác vụ chưa chạy xong được đánh dấu Cần chú ý.", "Xem log, kiểm tra máy rồi chạy lại các mục còn lại.",
                $"{ex.GetType().Name}{Environment.NewLine}Log: {WindowsEnvironment.LogsDirectory}", "attention"));
        }
        finally
        {
            LinkPrefetcher.End(prefetch);
            IsRunning = false;
            DockTitle = report is null ? "Không chạy được" : "Đã xử lý xong";
            DockStep = $"{_runTotal}/{_runTotal} tác vụ · {(DateTimeOffset.Now - _runStarted).ToString(@"mm\:ss", CultureInfo.InvariantCulture)}";
            _runCts.Dispose();
            _runCts = null;
            RefreshLog();
            Handover.Refresh();
            Software.RefreshQueue();
        }

        if (CloseIfRequested())
        {
            return report;
        }

        if (quiet && report is not null && report.HaltReason == RunHaltReason.None && DoneProblems == 0)
        {
            DockVisible = false;
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

    /// <summary>
    /// Gọi khi người dùng đóng cửa sổ. Trả về false (không đóng) khi đang chạy: hỏi lại, nếu đồng ý thì dừng sau bước
    /// hiện tại, ghi báo cáo rồi tự đóng — không bỏ dở tác vụ (cài phần mềm, chia ổ…) mà không có báo cáo.
    /// </summary>
    public bool ConfirmClose()
    {
        if (!IsRunning)
        {
            return true;
        }

        if (!_closeAfterRun)
        {
            ShowDialog(new ConfirmDialogViewModel(this, "Đang chạy tác vụ", "alert", "Dừng rồi thoát", () =>
            {
                _closeAfterRun = true;
                S.Log.Info("Người dùng đóng ứng dụng khi đang chạy: dừng sau bước hiện tại rồi thoát.");
                if (!_cancelRequested && _runCts is not null)
                {
                    _cancelRequested = true;
                    _runCts.Cancel();
                }

                DockSummary = "Đang dừng sau bước hiện tại, ghi báo cáo rồi tự đóng…";
                return Task.CompletedTask;
            }, "Tiếp tục chạy")
            {
                Lead = "Thoát ngay sẽ bỏ dở tác vụ đang chạy mà không có báo cáo. Phần mềm sẽ hoàn tất bước hiện tại " +
                       "(trình cài đặt và thao tác ổ đĩa không bị cưỡng chế dừng), ghi báo cáo rồi tự đóng.",
                Warning = "Không tắt máy hay rút sạc khi đang chia ổ đĩa hoặc cài phần mềm.",
            });
        }

        return false;
    }

    /// <summary>Chạy thao tác ngoài hàng đợi (ví dụ hoàn tác) nhưng vẫn khóa không cho lượt khác chạy chồng.</summary>
    public async Task RunExclusiveAsync(Func<Task> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        if (IsRunning)
        {
            return;
        }

        IsRunning = true;
        try
        {
            await work();
        }
        finally
        {
            IsRunning = false;
        }

        CloseIfRequested();
    }

    private bool CloseIfRequested()
    {
        if (!_closeAfterRun)
        {
            return false;
        }

        _closeAfterRun = false;
        _ui.Shutdown();
        return true;
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

    /// <summary>
    /// Gửi kết quả cài phần mềm lên mục "Lịch sử cài" của Web Admin (QĐ-006). Chạy nền; lỗi chỉ ghi log,
    /// không ảnh hưởng kết quả hay báo cáo cục bộ.
    /// </summary>
    private void SendInstallReport(IReadOnlyList<TaskRowViewModel> rows, RunReport report)
    {
        var items = new List<InstallReportItem>();
        foreach (var outcome in report.Tasks)
        {
            var row = rows.FirstOrDefault(r => r.Id == outcome.TaskId);
            if (row?.App is not { } app || outcome.State == TaskState.Pending)
            {
                continue;
            }

            var m = app.LastMetrics;
            items.Add(new InstallReportItem
            {
                Name = row.HasVariants && row.SelectedVariantLabel is { } v ? $"{row.Name} ({v})" : row.Name,
                Source = m?.Source is { Length: > 0 } s ? s : app.SourceText,
                Bytes = m?.Bytes ?? 0,
                DownloadSeconds = m?.Download.TotalSeconds ?? 0,
                WaitSeconds = m?.Wait.TotalSeconds ?? 0,
                InstallSeconds = m?.Install.TotalSeconds ?? outcome.Duration?.TotalSeconds ?? 0,
                Result = InstallReportPayload.ResultFor(outcome.State, app.WasAlreadyInstalled),
                Code = m?.ExitCode ?? 0,
                Reason = Redactor.Redact(outcome.Reason),
                Attempt = Math.Max(1, outcome.Attempts),
            });
        }

        if (items.Count == 0 || S.Account.Current is null)
        {
            return;
        }

        var payload = new InstallReportPayload(
            Environment.MachineName,
            System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            (report.FinishedAt - report.StartedAt).TotalSeconds,
            items);
        _ = Task.Run(async () =>
        {
            try
            {
                await S.Account.SendInstallReportAsync(payload);
            }
#pragma warning disable CA1031 // Gửi báo cáo là việc phụ: lỗi bất kỳ chỉ ghi log.
            catch (Exception ex) when (ex is not OutOfMemoryException)
#pragma warning restore CA1031
            {
                S.Log.Warning("Không gửi được báo cáo cài: " + ex.Message);
            }
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

        while (Recent.Count > 4)
        {
            Recent.RemoveAt(Recent.Count - 1);
        }

        OnPropertyChanged(nameof(HasRecent));
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

    private List<(string Key, SearchHit Hit)> BuildSearchIndex()
    {
        var hits = new List<SearchHit>();
        foreach (var n in Nav)
        {
            hits.Add(new SearchHit(n.Label, "Trang", n.Page));
        }

        // Tính năng không nằm trong danh mục tác vụ.
        hits.Add(new SearchHit("Chia ổ đĩa", "Ổ đĩa & Tài khoản", Page.Disk));
        hits.Add(new SearchHit("Đổi tên máy tính", "Ổ đĩa & Tài khoản", Page.Disk));
        hits.Add(new SearchHit("Khóa khôi phục BitLocker", "Ổ đĩa & Tài khoản", Page.Disk));
        hits.Add(new SearchHit("Tạo báo cáo bàn giao", "Kiểm tra & Bàn giao", Page.Handover));
        hits.Add(new SearchHit("Hoàn tác thay đổi Windows", "Cài đặt & Tối ưu", Page.Windows));
        hits.Add(new SearchHit("Thông tin máy", "Tổng quan", Page.Overview));
        hits.Add(new SearchHit("Thiết lập đầy đủ", "Tổng quan", Page.Overview));

        foreach (var row in Rows.Values)
        {
            var page = row.Entry.Category switch
            {
                TaskCategory.Software => Page.Software,
                TaskCategory.WindowsSettings => Page.Windows,
                TaskCategory.DiskAndAccount => Page.Disk,
                TaskCategory.Cleanup => Page.Cleanup,
                _ => Page.Handover,
            };
            hits.Add(new SearchHit(row.Name, Nav.First(n => n.Page == page).Label, page));
        }

        return [.. hits.Select(h => (Fold(h.Title + " " + h.Where), h))];
    }

    private void UpdateSearch()
    {
        SearchHits.Clear();
        var words = Fold(_searchText).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length > 0)
        {
            foreach (var (_, hit) in _searchIndex.Where(e => words.All(w => e.Key.Contains(w, StringComparison.Ordinal))).Take(8))
            {
                SearchHits.Add(hit);
            }
        }

        OnPropertyChanged(nameof(HasSearchHits));
        OnPropertyChanged(nameof(SearchNoHits));
        OnPropertyChanged(nameof(SearchOpen));
    }

    /// <summary>Chữ thường, bỏ dấu tiếng Việt để tìm "bitlocker", "o dia", "tat bitlocker" đều ra.</summary>
    internal static string Fold(string text)
    {
        var d = text.ToLowerInvariant().Replace('đ', 'd').Normalize(NormalizationForm.FormD);
        var b = new StringBuilder(d.Length);
        foreach (var ch in d)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                b.Append(ch);
            }
        }

        return b.ToString().Normalize(NormalizationForm.FormC);
    }
}
