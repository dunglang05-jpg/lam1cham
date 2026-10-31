using System.Globalization;
using LamMotCham.Presentation.Mvvm;
using LamMotCham.Core.Orchestration;
using LamMotCham.Core.Tasks;
using LamMotCham.Windows.Software;

namespace LamMotCham.Presentation.ViewModels;

public sealed record DetailItem(string Label, string Value);

/// <summary>Trạng thái hiển thị của một task, dùng chung cho mọi trang.</summary>
public sealed class TaskRowViewModel : ObservableObject
{
    private bool _isSelected;
    private TaskState? _state;
    private string? _message;
    private string _currentText = "Chưa kiểm tra";
    private bool _isExpanded;
    private bool _manualConfirmed;
    private int _attempts;
    private TimeSpan? _duration;
    private DateTimeOffset? _startedAt;

    public TaskRowViewModel(TaskCatalogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        Entry = entry;
        _isSelected = entry.SelectedByDefault && IsAvailable;
        Details = entry.Details is { } d
            ?
            [
                new DetailItem("SẼ THAY ĐỔI", d.Change), new DetailItem("LÝ DO", d.Reason), new DetailItem("ĐIỀU KIỆN TRƯỚC", d.Preconditions),
                new DetailItem("QUYỀN CẦN THIẾT", d.Permission), new DetailItem("ẢNH HƯỞNG", d.Effect), new DetailItem("CÁCH HOÀN TÁC", d.Undo),
            ]
            : [];
        ToggleExpandCommand = new RelayCommand(() => IsExpanded = !IsExpanded);
        ToggleManualCommand = new RelayCommand(() => ManualConfirmed = !ManualConfirmed);
    }

    public TaskCatalogEntry Entry { get; }

    public string Id => Entry.Id;

    public string Name => Entry.Module.Descriptor.DisplayName;

    public string Description => Entry.Description;

    public string IconKey => Entry.IconKey;

    public string? LogoKey => Entry.LogoKey;

    public string? Group => Entry.Group;

    /// <summary>Task cài ứng dụng (null với các task khác).</summary>
    public AppInstallTask? App => Entry.Module as AppInstallTask;

    /// <summary>Nguồn cài: mã WinGet, tên miền link tải hoặc trang web (chỉ có với task cài phần mềm).</summary>
    public string? PackageId => App?.SourceText;

    /// <summary>false: ứng dụng hiện mờ, không chọn được (QĐ-006).</summary>
    public bool IsAvailable => App?.Package.IsAvailable ?? true;

    public string? UnavailableReason => App?.Package.UnavailableReason;

    /// <summary>Hiện chữ đại diện thay logo (ứng dụng từ Web Admin chưa có logo đóng gói sẵn).</summary>
    public bool ShowInitial => App is not null && LogoKey is null && !string.IsNullOrEmpty(App.Package.Initial);

    public string Initial => App?.Package.Initial ?? "";

    public string ColorHex => App?.Package.Color ?? "#3B5BDB";

    public bool HasVariants => App?.Direct is not null && App.Package.HasVariants;

    public IReadOnlyList<string> VariantLabels => App?.Package.Variants.Select(v => v.Label).ToList() ?? [];

    /// <summary>Phiên bản đang chọn cho ứng dụng có nhiều phiên bản.</summary>
    public string? SelectedVariantLabel
    {
        get => App?.Direct?.SelectedVariant?.Label;
        set
        {
            if (App?.Direct is { } direct && value is not null && direct.SelectedVariant?.Label != value)
            {
                direct.SelectedVariant = App.Package.Variants.FirstOrDefault(v => v.Label == value) ?? direct.SelectedVariant;
                OnPropertyChanged();
            }
        }
    }

    public bool IsReadOnly => Entry.IsReadOnly;

    /// <summary>Nằm trong bộ chọn sẵn (thiết kế: cột "Đề xuất" — Khuyến nghị / Tùy chọn).</summary>
    public bool IsRecommended => Entry.SelectedByDefault;

    public bool IsSelectable => !Entry.IsReadOnly && IsAvailable;

    public bool NeedsAdmin => Entry.Module.Descriptor.Privilege == PrivilegeLevel.Administrator;

    public bool RequiresManual => Entry.RequiresManualConfirmation;

    public IReadOnlyList<DetailItem> Details { get; }

    public bool HasDetails => Details.Count > 0;

    public string ImpactKey => Entry.Impact switch
    {
        TaskImpact.ReadOnly => "readonly",
        TaskImpact.Medium => "medium",
        _ => "low",
    };

    public RelayCommand ToggleExpandCommand { get; }

    public RelayCommand ToggleManualCommand { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value && IsAvailable);
    }

    public TaskState? State
    {
        get => _state;
        private set
        {
            if (Set(ref _state, value))
            {
                OnPropertyChanged(nameof(StateKey));
                OnPropertyChanged(nameof(StateText));
                OnPropertyChanged(nameof(HasRun));
                OnPropertyChanged(nameof(IsProblem));
                OnPropertyChanged(nameof(ShowManualButton));
            }
        }
    }

    public bool HasRun => _state is not null;

    public bool IsProblem => _state is TaskState.Failed or TaskState.NeedsAttention;

    public string? Message
    {
        get => _message;
        private set
        {
            if (Set(ref _message, value))
            {
                OnPropertyChanged(nameof(Evidence));
            }
        }
    }

    /// <summary>Cột "Hiện tại" / trạng thái cài trước khi chạy.</summary>
    public string CurrentText
    {
        get => _currentText;
        set
        {
            if (Set(ref _currentText, value))
            {
                OnPropertyChanged(nameof(StateKey));
                OnPropertyChanged(nameof(StateText));
            }
        }
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (Set(ref _isExpanded, value))
            {
                OnPropertyChanged(nameof(ChevronGlyph));
            }
        }
    }

    public string ChevronGlyph => _isExpanded ? "" : "";

    public bool ManualConfirmed
    {
        get => _manualConfirmed;
        set
        {
            if (Set(ref _manualConfirmed, value))
            {
                OnPropertyChanged(nameof(StateKey));
                OnPropertyChanged(nameof(StateText));
                OnPropertyChanged(nameof(Evidence));
                OnPropertyChanged(nameof(ManualButtonText));
            }
        }
    }

    public bool ShowManualButton => RequiresManual && _state is TaskState.NotTested or TaskState.Success;

    public string ManualButtonText => _manualConfirmed ? "Bỏ xác nhận" : "Đã thử, đạt";

    public string Evidence => _manualConfirmed
        ? "Kỹ thuật viên đã thử và xác nhận đạt."
        : (_message ?? (RequiresManual ? Description : (_state is null ? "Chưa kiểm tra" : "")));

    public int Attempts
    {
        get => _attempts;
        private set => Set(ref _attempts, value);
    }

    public string MetaText
    {
        get
        {
            var parts = new List<string>();
            if (_duration is { } d)
            {
                parts.Add(d.ToString(@"mm\:ss", CultureInfo.InvariantCulture));
            }
            else if (_state == TaskState.Running && _startedAt is { } s)
            {
                // Việc đang làm (ví dụ "Đang tải 45% · 120,0/277,7 MB"); phần "· mm:ss" của bộ đếm đã có ở cột thời gian.
                if (!string.IsNullOrWhiteSpace(Message))
                {
                    parts.Add(Message.Split(" · ")[0] is var step && step.StartsWith("Đang tải", StringComparison.Ordinal) ? Message : step);
                }

                parts.Add((DateTimeOffset.Now - s).ToString(@"mm\:ss", CultureInfo.InvariantCulture));
            }

            if (_attempts > 1)
            {
                parts.Add($"{_attempts} lần thử");
            }

            if (_state == TaskState.Pending)
            {
                // Đang chờ vì lý do cụ thể (ví dụ RAM trống thấp) thì hiện lý do thay cho "Chờ đến lượt".
                parts.Add(string.IsNullOrWhiteSpace(Message) ? "Chờ đến lượt" : Message.Split(". ")[0]);
            }

            return string.Join(" · ", parts);
        }
    }

    /// <summary>Khóa cho StatusChip.</summary>
    public string StateKey => _state switch
    {
        null => Entry.Category == TaskCategory.Software
            ? _currentText switch
            {
                "Đã cài" => "installed",
                "Chưa phát hiện" => "notfound",
                _ => "unchecked",
            }
            : "unchecked",
        TaskState.Pending => "pending",
        TaskState.Running => "running",
        TaskState.Success => _manualConfirmed || IsReadOnly ? "pass" : "success",
        TaskState.Failed => "failed",
        TaskState.Skipped => "skipped",
        TaskState.NeedsAttention => "attention",
        TaskState.Cancelled => "cancelled",
        TaskState.WaitingForReboot => "reboot",
        TaskState.NotTested => _manualConfirmed ? "pass" : "manual",
        TaskState.NotSupported => "notsupported",
        _ => "pending",
    };

    /// <summary>Chữ trên chip khi cần cụ thể hơn nhãn mặc định.</summary>
    public string? StateText => _state switch
    {
        TaskState.NeedsAttention when Entry.Category == TaskCategory.Software && (_message?.Contains("chưa xác minh", StringComparison.Ordinal) ?? false)
            => "Cần chú ý — chưa xác minh",
        TaskState.Success when (_message?.StartsWith("Đã ở trạng thái mong muốn", StringComparison.Ordinal) ?? false)
            => Entry.Category == TaskCategory.Software ? "Đã cài sẵn" : "Đã đúng sẵn",
        _ => null,
    };

    public void ResetForRun()
    {
        _duration = null;
        _startedAt = null;
        Attempts = 0;
        Message = null;
        ManualConfirmed = false;
        State = TaskState.Pending;
        OnPropertyChanged(nameof(MetaText));
    }

    public void Apply(TaskStatusUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        if (update.State == TaskState.Running && _state != TaskState.Running)
        {
            _startedAt = DateTimeOffset.Now;
        }

        State = update.State;
        if (update.Message is not null || update.State != TaskState.Running)
        {
            Message = update.Message;
        }

        Attempts = update.Attempt;
        OnPropertyChanged(nameof(MetaText));
    }

    public void Apply(TaskOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        State = outcome.State;
        Message = outcome.Reason;
        Attempts = outcome.Attempts;
        _duration = outcome.Duration;
        OnPropertyChanged(nameof(MetaText));
    }

    public void Tick() => OnPropertyChanged(nameof(MetaText));

    /// <summary>Kết quả để ghi vào báo cáo bàn giao.</summary>
    public TaskOutcome ToOutcome() => new()
    {
        TaskId = Id,
        DisplayName = Name,
        Kind = Entry.Module.Descriptor.Kind,
        Risk = Entry.Module.Descriptor.Risk,
        State = _manualConfirmed ? TaskState.Success : _state ?? TaskState.NotTested,
        Reason = _manualConfirmed ? "Kỹ thuật viên đã thử và xác nhận đạt." : _message ?? "Chưa kiểm tra trong phiên này.",
        Attempts = _attempts,
    };
}
