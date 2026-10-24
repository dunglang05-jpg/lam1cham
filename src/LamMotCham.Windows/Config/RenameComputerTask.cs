using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using LamMotCham.Core.Tasks;
using LamMotCham.Windows.Native;
using Microsoft.Win32;

namespace LamMotCham.Windows.Config;

/// <summary>
/// Đổi tên máy. Tên mới có hiệu lực sau khi khởi động lại; ứng dụng không tự khởi động lại.
/// </summary>
public sealed partial class RenameComputerTask : ITaskModule, IDescribesCurrentState
{
    private const string TcpipParameters = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters";

    public RenameComputerTask(string newName)
    {
        if (!IsValidName(newName))
        {
            throw new ArgumentException("Tên máy không hợp lệ.", nameof(newName));
        }

        NewName = newName.ToUpperInvariant();
        Descriptor = new TaskDescriptor
        {
            Id = "disk.rename",
            DisplayName = "Đổi tên máy thành " + NewName,
            Version = 1,
            Risk = RiskLevel.Reversible,
            Privilege = PrivilegeLevel.Administrator,
            Kind = TaskKind.Configuration,
            Timeout = TimeSpan.FromSeconds(30),
            Rollback = RollbackSupport.Supported,
            RollbackNote = "Đổi lại tên cũ (cũng cần khởi động lại).",
        };
    }

    public string NewName { get; }

    public TaskDescriptor Descriptor { get; }

    /// <summary>1–15 ký tự chữ, số, dấu gạch ngang; không toàn số; không bắt đầu/kết thúc bằng gạch ngang.</summary>
    public static bool IsValidName(string? name) =>
        name is not null && NameRegex().IsMatch(name) && !name.All(char.IsAsciiDigit);

    public Task<PreconditionResult> CheckPreconditionsAsync(TaskContext context, CancellationToken cancellationToken)
    {
        if (string.Equals(Environment.MachineName, NewName, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(PreconditionResult.AlreadyInDesiredState("Máy đã có tên này."));
        }

        return Task.FromResult(string.Equals(PendingName(), NewName, StringComparison.OrdinalIgnoreCase)
            ? PreconditionResult.NotMet("Tên này đã được đặt và đang chờ khởi động lại.")
            : PreconditionResult.Met());
    }

    public Task<ExecuteResult> ExecuteAsync(TaskContext context, CancellationToken cancellationToken)
    {
        if (!NativeMethods.SetComputerNameExW(NativeMethods.ComputerNamePhysicalDnsHostname, NewName))
        {
            var error = Marshal.GetLastPInvokeError();
            return Task.FromResult(ExecuteResult.Failed(
                error == 5 ? "Cần quyền quản trị để đổi tên máy." : "Windows không chấp nhận tên mới.",
                errorCode: error.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        // Xác minh tên đang chờ trước khi báo cần khởi động lại.
        return Task.FromResult(string.Equals(PendingName(), NewName, StringComparison.OrdinalIgnoreCase)
            ? ExecuteResult.RebootRequired($"Tên máy mới {NewName} có hiệu lực sau khi khởi động lại.")
            : ExecuteResult.Failed("Không đọc lại được tên máy mới sau khi đặt."));
    }

    public Task<VerifyResult> VerifyAsync(TaskContext context, CancellationToken cancellationToken) =>
        Task.FromResult(string.Equals(Environment.MachineName, NewName, StringComparison.OrdinalIgnoreCase)
            ? VerifyResult.Passed("Máy đang dùng tên mới.")
            : VerifyResult.Attention("Tên mới đang chờ khởi động lại."));

    public Task<VerifyResult> RollbackAsync(TaskContext context, CancellationToken cancellationToken) =>
        Task.FromResult(VerifyResult.Inconclusive("Đổi lại tên cũ bằng cách nhập tên cũ và chạy lại tác vụ."));

    public Task<string> DescribeCurrentStateAsync(CancellationToken cancellationToken) =>
        Task.FromResult("Hiện tại: " + Environment.MachineName);

    private static string? PendingName()
    {
        using var key = Registry.LocalMachine.OpenSubKey(TcpipParameters, writable: false);
        return key?.GetValue("NV Hostname") as string;
    }

    [GeneratedRegex(@"^[A-Za-z0-9](?:[A-Za-z0-9-]{0,13}[A-Za-z0-9])?\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex NameRegex();
}
