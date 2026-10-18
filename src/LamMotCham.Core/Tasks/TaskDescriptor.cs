namespace LamMotCham.Core.Tasks;

/// <summary>Mức rủi ro theo quy ước trong kế hoạch MVP.</summary>
public enum RiskLevel
{
    /// <summary>R0 — chỉ đọc.</summary>
    ReadOnly = 0,

    /// <summary>R1 — thay đổi cấu hình có thể hoàn tác.</summary>
    Reversible = 1,

    /// <summary>R2 — cài hoặc gỡ phần mềm.</summary>
    SoftwareChange = 2,

    /// <summary>R3 — có thể gây mất dữ liệu (ví dụ chia ổ đĩa). Chỉ chạy khi người dùng xác nhận riêng (<see cref="TaskDescriptor.RequiresConfirmation"/>).</summary>
    Destructive = 3,
}

public enum PrivilegeLevel
{
    User,
    Administrator,
}

public enum TaskKind
{
    /// <summary>Cài phần mềm. Mặc định chỉ một installer chạy tại một thời điểm.</summary>
    Installer,

    /// <summary>Thay đổi cấu hình Windows.</summary>
    Configuration,

    /// <summary>Kiểm tra chỉ đọc.</summary>
    Check,
}

public enum RollbackSupport
{
    /// <summary>Task chỉ đọc, không cần rollback.</summary>
    NotApplicable,

    /// <summary>Có thể khôi phục giá trị cũ.</summary>
    Supported,

    /// <summary>Không thể rollback; lý do phải ghi trong <see cref="TaskDescriptor.RollbackNote"/>.</summary>
    NotPossible,
}

/// <summary>
/// Phần khai báo của hợp đồng task (tài liệu 04, mục "Hợp đồng tác vụ").
/// </summary>
public sealed record TaskDescriptor
{
    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    public required int Version { get; init; }

    public required RiskLevel Risk { get; init; }

    public required PrivilegeLevel Privilege { get; init; }

    public required TaskKind Kind { get; init; }

    /// <summary>Thời gian tối đa cho một lần thử (precondition + execute + verify).</summary>
    public required TimeSpan Timeout { get; init; }

    public RetryPolicy Retry { get; init; } = RetryPolicy.None;

    /// <summary>
    /// false: không truyền lệnh hủy vào task (ví dụ trình cài đặt bên ngoài). Khi người dùng hủy,
    /// ứng dụng chờ bước hiện tại kết thúc; khi quá thời gian, ứng dụng ngừng chờ và dừng hàng đợi
    /// nhưng không cưỡng chế dừng tiến trình (UI/UX Handoff mục 13).
    /// </summary>
    public bool AllowForcedStop { get; init; } = true;

    public IReadOnlyList<string> DependsOn { get; init; } = [];

    public required RollbackSupport Rollback { get; init; }

    /// <summary>Bắt buộc khi <see cref="Rollback"/> là <see cref="RollbackSupport.NotPossible"/>.</summary>
    public string? RollbackNote { get; init; }

    /// <summary>
    /// Task chỉ được tạo sau khi người dùng xem trước và xác nhận riêng; không bao giờ nằm trong gói nhanh.
    /// Bắt buộc với mức <see cref="RiskLevel.Destructive"/>.
    /// </summary>
    public bool RequiresConfirmation { get; init; }

    /// <summary>Trả về danh sách lỗi khai báo; rỗng nghĩa là hợp lệ.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(Id))
        {
            errors.Add("Thiếu ID task.");
        }

        if (string.IsNullOrWhiteSpace(DisplayName))
        {
            errors.Add($"Task '{Id}': thiếu tên hiển thị.");
        }

        if (Version < 1)
        {
            errors.Add($"Task '{Id}': phiên bản phải >= 1.");
        }

        if (Timeout <= TimeSpan.Zero)
        {
            errors.Add($"Task '{Id}': timeout phải lớn hơn 0.");
        }

        if (Retry is null)
        {
            errors.Add($"Task '{Id}': thiếu retry policy.");
        }
        else
        {
            errors.AddRange(Retry.Validate().Select(e => $"Task '{Id}': {e}"));
        }

        if (Rollback == RollbackSupport.NotPossible && string.IsNullOrWhiteSpace(RollbackNote))
        {
            errors.Add($"Task '{Id}': không thể rollback thì phải ghi lý do.");
        }

        if (Risk == RiskLevel.ReadOnly && Rollback != RollbackSupport.NotApplicable)
        {
            errors.Add($"Task '{Id}': task chỉ đọc phải khai báo rollback là NotApplicable.");
        }

        if (Risk != RiskLevel.ReadOnly && Rollback == RollbackSupport.NotApplicable)
        {
            errors.Add($"Task '{Id}': task thay đổi hệ thống phải khai báo rollback hoặc lý do không thể rollback.");
        }

        if (Risk == RiskLevel.Destructive && !RequiresConfirmation)
        {
            errors.Add($"Task '{Id}': task mức R3 phải khai báo RequiresConfirmation (xem trước và xác nhận riêng).");
        }

        if (Risk == RiskLevel.Destructive && AllowForcedStop)
        {
            errors.Add($"Task '{Id}': task mức R3 không được cưỡng chế dừng giữa chừng (AllowForcedStop = false).");
        }

        if (DependsOn is null)
        {
            errors.Add($"Task '{Id}': danh sách phụ thuộc không được null.");
        }
        else if (DependsOn.Contains(Id, StringComparer.Ordinal))
        {
            errors.Add($"Task '{Id}': không được phụ thuộc vào chính nó.");
        }

        return errors;
    }
}
