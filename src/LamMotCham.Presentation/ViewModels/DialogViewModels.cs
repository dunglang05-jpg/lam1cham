using LamMotCham.Presentation.Mvvm;

namespace LamMotCham.Presentation.ViewModels;

public sealed record DialogItem(string Title, string? Subtitle = null, string? StateKey = null, string? StateText = null, string? Trailing = null);

public sealed record DialogFact(string Label, string Value);

public abstract class DialogViewModel : ObservableObject
{
    protected DialogViewModel(MainViewModel main)
    {
        Main = main;
        CloseCommand = new RelayCommand(Close);
    }

    public MainViewModel Main { get; }

    public RelayCommand CloseCommand { get; }

    public void Close() => Main.CloseDialog(this);
}

/// <summary>Hộp thoại xác nhận chung: cài đặt, tác vụ Windows, dọn dẹp, đổi tên, hủy, hoàn tác.</summary>
public sealed class ConfirmDialogViewModel : DialogViewModel
{
    private bool _agreed;

    public ConfirmDialogViewModel(MainViewModel main, string title, string iconKey, string primaryText, Func<Task> onConfirm, string secondaryText = "Quay lại")
        : base(main)
    {
        Title = title;
        IconKey = iconKey;
        PrimaryText = primaryText;
        SecondaryText = secondaryText;
        ConfirmCommand = new AsyncCommand(
            async () =>
            {
                Close();
                await onConfirm();
            },
            () => !RequireAgreement || Agreed);
    }

    public string Title { get; }

    public string IconKey { get; }

    public string? Lead { get; init; }

    public IReadOnlyList<DialogItem> Items { get; init; } = [];

    public IReadOnlyList<DialogFact> Facts { get; init; } = [];

    public string? Warning { get; init; }

    public bool WarningIsDanger { get; init; }

    public bool RequireAgreement { get; init; }

    public string? AgreementText { get; init; }

    public string PrimaryText { get; }

    public string SecondaryText { get; }

    public AsyncCommand ConfirmCommand { get; }

    public bool Agreed
    {
        get => _agreed;
        set
        {
            // Tick/bỏ tick ô đồng ý phải bật/tắt ngay nút xác nhận (lỗi 1.1.1: nút mờ dù đã tick).
            Set(ref _agreed, value);
        }
    }
}

/// <summary>Thông báo lỗi theo cấu trúc: điều gì xảy ra → tác động → bước tiếp theo (UI/UX Handoff mục 14, 16).</summary>
public sealed class ErrorDialogViewModel : DialogViewModel
{
    public ErrorDialogViewModel(MainViewModel main, string title, string what, string impact, string next, string? technical, string stateKey = "failed")
        : base(main)
    {
        Title = title;
        What = what;
        Impact = impact;
        Next = next;
        Technical = technical;
        StateKey = stateKey;
        CopyCommand = new RelayCommand(() => main.Ui.SetClipboard(technical ?? ""));
        OpenLogCommand = new RelayCommand(main.OpenLogs);
    }

    public string Title { get; }

    public string What { get; }

    public string Impact { get; }

    public string Next { get; }

    public string? Technical { get; }

    public string StateKey { get; }

    public RelayCommand CopyCommand { get; }

    public RelayCommand OpenLogCommand { get; }
}

/// <summary>Không tự khởi động lại: nêu lý do và để người dùng chọn (UI/UX Handoff mục 16).</summary>
public sealed class RestartDialogViewModel : DialogViewModel
{
    public RestartDialogViewModel(MainViewModel main, string reason, int remaining)
        : base(main)
    {
        Reason = reason;
        Remaining = remaining > 0
            ? $"{remaining} tác vụ còn lại đang chờ. Mở lại Làm 1 Chạm sau khi khởi động để chạy tiếp."
            : "Không còn tác vụ nào đang chờ.";
        RestartCommand = new RelayCommand(() =>
        {
            Close();
            main.RestartComputer();
        });
    }

    public string Reason { get; }

    public string Remaining { get; }

    public RelayCommand RestartCommand { get; }
}

/// <summary>Danh sách thông tin chỉ để xem (kế hoạch cài, các mục cần chú ý).</summary>
public sealed class ListDialogViewModel : DialogViewModel
{
    public ListDialogViewModel(MainViewModel main, string title, string iconKey, IReadOnlyList<DialogItem> items, string? lead = null)
        : base(main)
    {
        Title = title;
        IconKey = iconKey;
        Items = items;
        Lead = lead;
    }

    public string Title { get; }

    public string IconKey { get; }

    public string? Lead { get; }

    public IReadOnlyList<DialogItem> Items { get; }

    public bool IsEmpty => Items.Count == 0;
}
