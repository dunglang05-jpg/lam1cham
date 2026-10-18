namespace LamMotCham.Core.Tasks;

/// <summary>Nhóm chức năng trên thanh điều hướng (UI/UX Handoff mục 4).</summary>
public enum TaskCategory
{
    Software,
    WindowsSettings,
    DiskAndAccount,
    Cleanup,
    Handover,
}

/// <summary>Gói thao tác nhanh (bộ quy tắc 03, nguyên tắc 4).</summary>
public enum TaskPack
{
    FullSetup,
    SoftwareOnly,
    WindowsOnly,
    CheckOnly,
}

/// <summary>Mức tác động hiển thị cho người dùng.</summary>
public enum TaskImpact
{
    ReadOnly,
    Low,
    Medium,
}

/// <summary>Sáu ý bắt buộc khi giải thích một thay đổi (UI/UX Handoff mục 9).</summary>
public sealed record TaskDetails(
    string Change,
    string Reason,
    string Preconditions,
    string Permission,
    string Effect,
    string Undo);

/// <summary>Mục trong danh mục task mà UI hiển thị và người dùng có thể chọn/bỏ chọn.</summary>
public sealed record TaskCatalogEntry
{
    public required ITaskModule Module { get; init; }

    public required TaskCategory Category { get; init; }

    /// <summary>Nhóm con trong trang (ví dụ "Quyền riêng tư").</summary>
    public string? Group { get; init; }

    public required string Description { get; init; }

    public TaskImpact Impact { get; init; } = TaskImpact.Low;

    public TaskDetails? Details { get; init; }

    public bool SelectedByDefault { get; init; } = true;

    /// <summary>Khóa icon dùng chung với bản thiết kế (ví dụ "globe", "wifi").</summary>
    public string IconKey { get; init; } = "check";

    /// <summary>Khóa logo thật của ứng dụng (file trong Assets/Logos). Null nếu chưa có logo — khi đó dùng IconKey.</summary>
    public string? LogoKey { get; init; }

    /// <summary>Mục kiểm tra cần kỹ thuật viên thử thật (loa, micro, camera...).</summary>
    public bool RequiresManualConfirmation { get; init; }

    public string Id => Module.Descriptor.Id;

    public bool IsReadOnly => Module.Descriptor.Risk == RiskLevel.ReadOnly;
}

/// <summary>Đọc trạng thái hiện tại để hiển thị cột "Hiện tại" trước khi áp dụng.</summary>
public interface IDescribesCurrentState
{
    Task<string> DescribeCurrentStateAsync(CancellationToken cancellationToken);
}

public static class TaskPacks
{
    public static string DisplayName(this TaskPack pack) => pack switch
    {
        TaskPack.FullSetup => "Thiết lập đầy đủ",
        TaskPack.SoftwareOnly => "Chỉ cài phần mềm",
        TaskPack.WindowsOnly => "Chỉ tối ưu Windows",
        TaskPack.CheckOnly => "Chỉ kiểm tra / bàn giao",
        _ => pack.ToString(),
    };

    /// <summary>Tập task được chọn sẵn khi người dùng chọn một gói. Người dùng vẫn bỏ chọn được.</summary>
    public static bool IsInPack(this TaskCatalogEntry entry, TaskPack pack)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!entry.SelectedByDefault)
        {
            return false;
        }

        return pack switch
        {
            TaskPack.FullSetup => true,
            TaskPack.SoftwareOnly => entry.Category == TaskCategory.Software,
            TaskPack.WindowsOnly => entry.Category is TaskCategory.WindowsSettings or TaskCategory.Cleanup,
            TaskPack.CheckOnly => entry.IsReadOnly,
            _ => false,
        };
    }
}
