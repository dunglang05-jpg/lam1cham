namespace LamMotCham.Core.Tasks;

/// <summary>
/// Trạng thái chuẩn của một task (tài liệu 04). Không thêm trạng thái mới khi chưa có ADR.
/// </summary>
public enum TaskState
{
    /// <summary>Đang chờ đến lượt chạy.</summary>
    Pending,

    /// <summary>Đang chạy.</summary>
    Running,

    /// <summary>Đã chạy và Verify xác nhận đạt. Không được gán nếu chưa verify.</summary>
    Success,

    /// <summary>Execute hoặc Verify thất bại, hoặc quá thời gian.</summary>
    Failed,

    /// <summary>Không chạy do precondition không đạt hoặc phụ thuộc không thành công.</summary>
    Skipped,

    /// <summary>Không xác định được kết quả; cần người kiểm tra.</summary>
    NeedsAttention,

    /// <summary>Bị hủy bởi người dùng.</summary>
    Cancelled,

    /// <summary>Cần khởi động lại; hàng đợi dừng tại đây và chờ xác nhận.</summary>
    WaitingForReboot,

    /// <summary>Hạng mục kiểm tra chưa được kiểm tra (ví dụ cần kiểm tra vật lý).</summary>
    NotTested,

    /// <summary>Máy hoặc môi trường không hỗ trợ task này.</summary>
    NotSupported,
}

public static class TaskStateExtensions
{
    /// <summary>Trạng thái kết thúc: task sẽ không tự chuyển sang trạng thái khác trong lượt chạy.</summary>
    public static bool IsTerminal(this TaskState state) => state is not (TaskState.Pending or TaskState.Running);

    /// <summary>Chỉ <see cref="TaskState.Success"/> cho phép task phụ thuộc được chạy.</summary>
    public static bool SatisfiesDependency(this TaskState state) => state == TaskState.Success;
}
