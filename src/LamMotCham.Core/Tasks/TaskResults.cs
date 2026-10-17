namespace LamMotCham.Core.Tasks;

public enum PreconditionOutcome
{
    /// <summary>Đủ điều kiện chạy Execute.</summary>
    Met,

    /// <summary>Máy đã ở trạng thái mong muốn; bỏ qua Execute nhưng vẫn Verify.</summary>
    AlreadyInDesiredState,

    /// <summary>Không đủ điều kiện; task bị bỏ qua.</summary>
    NotMet,

    /// <summary>Môi trường không hỗ trợ task này.</summary>
    NotSupported,
}

public sealed record PreconditionResult(PreconditionOutcome Outcome, string? Reason = null)
{
    public static PreconditionResult Met() => new(PreconditionOutcome.Met);

    public static PreconditionResult AlreadyInDesiredState(string reason) => new(PreconditionOutcome.AlreadyInDesiredState, reason);

    public static PreconditionResult NotMet(string reason) => new(PreconditionOutcome.NotMet, reason);

    public static PreconditionResult NotSupported(string reason) => new(PreconditionOutcome.NotSupported, reason);
}

public enum ExecuteOutcome
{
    Completed,
    CompletedRebootRequired,
    Failed,
}

public sealed record ExecuteResult(ExecuteOutcome Outcome, string? Reason = null, bool IsTransient = false, string? ErrorCode = null)
{
    public static ExecuteResult Completed(string? note = null) => new(ExecuteOutcome.Completed, note);

    public static ExecuteResult RebootRequired(string reason) => new(ExecuteOutcome.CompletedRebootRequired, reason);

    /// <param name="isTransient">Chỉ đặt true cho lỗi tạm thời đã được xác định (mạng chập chờn, nguồn bận...).</param>
    public static ExecuteResult Failed(string reason, bool isTransient = false, string? errorCode = null) =>
        new(ExecuteOutcome.Failed, reason, isTransient, errorCode);
}

public enum VerifyOutcome
{
    Passed,
    Failed,

    /// <summary>Không đủ cơ sở để khẳng định đạt hay không.</summary>
    Inconclusive,

    /// <summary>Đọc được kết quả nhưng có vấn đề cần người xem (dùng cho task kiểm tra).</summary>
    Attention,

    /// <summary>Không thể tự kiểm tra; cần kiểm tra vật lý.</summary>
    RequiresPhysicalCheck,

    /// <summary>Máy không có hoặc không hỗ trợ hạng mục này.</summary>
    NotSupported,
}

public sealed record VerifyResult(VerifyOutcome Outcome, string? Detail = null)
{
    public static VerifyResult Passed(string? detail = null) => new(VerifyOutcome.Passed, detail);

    public static VerifyResult Failed(string detail) => new(VerifyOutcome.Failed, detail);

    public static VerifyResult Inconclusive(string detail) => new(VerifyOutcome.Inconclusive, detail);

    public static VerifyResult Attention(string detail) => new(VerifyOutcome.Attention, detail);

    public static VerifyResult RequiresPhysicalCheck(string detail) => new(VerifyOutcome.RequiresPhysicalCheck, detail);

    public static VerifyResult NotSupported(string detail) => new(VerifyOutcome.NotSupported, detail);
}
