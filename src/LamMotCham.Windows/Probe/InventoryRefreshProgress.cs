using LamMotCham.Core.Orchestration;
using LamMotCham.Core.Tasks;

namespace LamMotCham.Windows.Probe;

/// <summary>
/// Chuyển tiếp tiến độ lượt chạy, đồng thời bỏ dữ liệu máy đã đọc ngay khi một tác vụ thay đổi máy kết thúc,
/// để các tác vụ kiểm tra chạy sau đọc lại trạng thái mới.
/// 02/11/2026 (máy mới của PO): kiểm tra BitLocker dùng dữ liệu đọc trước khi "Tắt BitLocker" chạy, nên báo
/// "đang mã hóa (92%)" dù ổ đã chuyển sang giải mã. Gọi đồng bộ trên luồng của bộ điều phối, trước khi task kế tiếp bắt đầu.
/// </summary>
public sealed class InventoryRefreshProgress(IProgress<TaskStatusUpdate>? inner, IReadOnlySet<string> machineChangingTaskIds, Action invalidate)
    : IProgress<TaskStatusUpdate>
{
    public void Report(TaskStatusUpdate value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.State.IsTerminal() && machineChangingTaskIds.Contains(value.TaskId))
        {
            invalidate();
        }

        inner?.Report(value);
    }
}
