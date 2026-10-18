using System.Globalization;
using LamMotCham.Core.Tasks;

namespace LamMotCham.Windows.Accounts;

/// <summary>Đổi tên hiển thị (Full name) của tài khoản cục bộ. Lưu tên cũ để hoàn tác.</summary>
public sealed class UserFullNameTask : ITaskModule
{
    private string? _oldFullName;

    public UserFullNameTask(string userName, string fullName)
    {
        if (!LocalAccounts.IsValidUserName(userName))
        {
            throw new ArgumentException("Tên tài khoản không hợp lệ.", nameof(userName));
        }

        if (!LocalAccounts.IsValidFullName(fullName))
        {
            throw new ArgumentException("Tên hiển thị không hợp lệ.", nameof(fullName));
        }

        UserName = userName;
        FullName = fullName.Trim();
        Descriptor = new TaskDescriptor
        {
            Id = "acct.fullname." + userName.ToLowerInvariant(),
            DisplayName = $"Đổi tên hiển thị của {userName}",
            Version = 1,
            Risk = RiskLevel.Reversible,
            Privilege = PrivilegeLevel.Administrator,
            Kind = TaskKind.Configuration,
            Timeout = TimeSpan.FromSeconds(20),
            Rollback = RollbackSupport.Supported,
            RollbackNote = "Đặt lại tên hiển thị cũ.",
        };
    }

    public string UserName { get; }

    public string FullName { get; }

    public TaskDescriptor Descriptor { get; }

    public Task<PreconditionResult> CheckPreconditionsAsync(TaskContext context, CancellationToken cancellationToken)
    {
        var rc = LocalAccounts.GetFullName(UserName, out var current);
        if (rc != 0)
        {
            return Task.FromResult(PreconditionResult.NotMet(LocalAccounts.Describe(rc)));
        }

        _oldFullName = current;
        return Task.FromResult(string.Equals(current, FullName, StringComparison.Ordinal)
            ? PreconditionResult.AlreadyInDesiredState("Tên hiển thị đã đúng.")
            : PreconditionResult.Met());
    }

    public Task<ExecuteResult> ExecuteAsync(TaskContext context, CancellationToken cancellationToken)
    {
        var rc = LocalAccounts.SetFullName(UserName, FullName);
        return Task.FromResult(rc == 0
            ? ExecuteResult.Completed()
            : ExecuteResult.Failed(LocalAccounts.Describe(rc), errorCode: rc.ToString(CultureInfo.InvariantCulture)));
    }

    public Task<VerifyResult> VerifyAsync(TaskContext context, CancellationToken cancellationToken)
    {
        var rc = LocalAccounts.GetFullName(UserName, out var now);
        return Task.FromResult(rc == 0 && string.Equals(now, FullName, StringComparison.Ordinal)
            ? VerifyResult.Passed($"Tên hiển thị: {FullName}.")
            : VerifyResult.Failed("Đọc lại không thấy tên hiển thị mới."));
    }

    public Task<VerifyResult> RollbackAsync(TaskContext context, CancellationToken cancellationToken)
    {
        if (_oldFullName is null)
        {
            return Task.FromResult(VerifyResult.Inconclusive("Chưa lưu tên hiển thị cũ."));
        }

        var rc = LocalAccounts.SetFullName(UserName, _oldFullName);
        return Task.FromResult(rc == 0 ? VerifyResult.Passed("Đã đặt lại tên hiển thị cũ.") : VerifyResult.Failed(LocalAccounts.Describe(rc)));
    }
}

/// <summary>Đổi tên đăng nhập. Thư mục C:\Users\... giữ nguyên tên cũ (Windows không đổi thư mục hồ sơ).</summary>
public sealed class UserRenameTask : ITaskModule
{
    public UserRenameTask(string userName, string newName)
    {
        if (!LocalAccounts.IsValidUserName(userName))
        {
            throw new ArgumentException("Tên tài khoản không hợp lệ.", nameof(userName));
        }

        if (!LocalAccounts.IsValidUserName(newName))
        {
            throw new ArgumentException("Tên đăng nhập mới không hợp lệ.", nameof(newName));
        }

        UserName = userName;
        NewName = newName;
        Descriptor = new TaskDescriptor
        {
            Id = "acct.rename." + userName.ToLowerInvariant(),
            DisplayName = $"Đổi tên đăng nhập {userName} → {newName}",
            Version = 1,
            Risk = RiskLevel.Reversible,
            Privilege = PrivilegeLevel.Administrator,
            Kind = TaskKind.Configuration,
            Timeout = TimeSpan.FromSeconds(20),
            Rollback = RollbackSupport.Supported,
            RollbackNote = "Đổi lại tên đăng nhập cũ.",
        };
    }

    public string UserName { get; }

    public string NewName { get; }

    public TaskDescriptor Descriptor { get; }

    public Task<PreconditionResult> CheckPreconditionsAsync(TaskContext context, CancellationToken cancellationToken)
    {
        if (string.Equals(UserName, NewName, StringComparison.Ordinal))
        {
            return Task.FromResult(PreconditionResult.AlreadyInDesiredState("Tên đăng nhập đã đúng."));
        }

        if (!LocalAccounts.Exists(UserName))
        {
            return Task.FromResult(PreconditionResult.NotMet("Không tìm thấy tài khoản " + UserName + "."));
        }

        // Chỉ đổi hoa/thường của cùng một tên thì tên mới "đã tồn tại" chính là tài khoản này.
        return Task.FromResult(!string.Equals(UserName, NewName, StringComparison.OrdinalIgnoreCase) && LocalAccounts.Exists(NewName)
            ? PreconditionResult.NotMet("Đã có tài khoản khác tên " + NewName + ".")
            : PreconditionResult.Met());
    }

    public Task<ExecuteResult> ExecuteAsync(TaskContext context, CancellationToken cancellationToken)
    {
        var rc = LocalAccounts.Rename(UserName, NewName);
        return Task.FromResult(rc == 0
            ? ExecuteResult.Completed()
            : ExecuteResult.Failed(LocalAccounts.Describe(rc), errorCode: rc.ToString(CultureInfo.InvariantCulture)));
    }

    public Task<VerifyResult> VerifyAsync(TaskContext context, CancellationToken cancellationToken) =>
        Task.FromResult(LocalAccounts.Exists(NewName)
            ? VerifyResult.Passed($"Đăng nhập bằng tên {NewName}. Thư mục hồ sơ giữ tên cũ.")
            : VerifyResult.Failed("Đọc lại không thấy tài khoản với tên mới."));

    public Task<VerifyResult> RollbackAsync(TaskContext context, CancellationToken cancellationToken)
    {
        var rc = LocalAccounts.Rename(NewName, UserName);
        return Task.FromResult(rc == 0 ? VerifyResult.Passed("Đã đổi lại tên đăng nhập cũ.") : VerifyResult.Failed(LocalAccounts.Describe(rc)));
    }
}

/// <summary>
/// Đặt mật khẩu mới cho tài khoản cục bộ (đặt lại bằng quyền quản trị). Mật khẩu không xuất hiện trong tên tác vụ,
/// thông báo, log hay báo cáo; được giữ trong bộ nhớ đến khi tác vụ xong rồi bỏ đi.
/// Không hoàn tác được (không biết mật khẩu cũ).
/// </summary>
public sealed class UserPasswordTask : ITaskModule
{
    private string? _password;

    public UserPasswordTask(string userName, string password)
    {
        if (!LocalAccounts.IsValidUserName(userName))
        {
            throw new ArgumentException("Tên tài khoản không hợp lệ.", nameof(userName));
        }

        ArgumentException.ThrowIfNullOrEmpty(password);
        UserName = userName;
        _password = password;
        Descriptor = new TaskDescriptor
        {
            Id = "acct.password." + userName.ToLowerInvariant(),
            DisplayName = $"Đặt mật khẩu mới cho {userName}",
            Version = 1,
            Risk = RiskLevel.Reversible,
            RequiresConfirmation = true,
            Privilege = PrivilegeLevel.Administrator,
            Kind = TaskKind.Configuration,
            Timeout = TimeSpan.FromSeconds(30),
            Rollback = RollbackSupport.NotPossible,
            RollbackNote = "Không khôi phục được mật khẩu cũ. Đặt lại mật khẩu khác nếu cần.",
        };
    }

    public string UserName { get; }

    public TaskDescriptor Descriptor { get; }

    public Task<PreconditionResult> CheckPreconditionsAsync(TaskContext context, CancellationToken cancellationToken) =>
        Task.FromResult(_password is null
            ? PreconditionResult.NotMet("Mật khẩu đã được xóa khỏi bộ nhớ; nhập lại để chạy lại.")
            : LocalAccounts.Exists(UserName) ? PreconditionResult.Met() : PreconditionResult.NotMet("Không tìm thấy tài khoản " + UserName + "."));

    public Task<ExecuteResult> ExecuteAsync(TaskContext context, CancellationToken cancellationToken)
    {
        if (_password is null)
        {
            return Task.FromResult(ExecuteResult.Failed("Mật khẩu đã được xóa khỏi bộ nhớ."));
        }

        var rc = LocalAccounts.SetPassword(UserName, _password);
        return Task.FromResult(rc == 0
            ? ExecuteResult.Completed()
            : ExecuteResult.Failed(LocalAccounts.Describe(rc), errorCode: rc.ToString(CultureInfo.InvariantCulture)));
    }

    public Task<VerifyResult> VerifyAsync(TaskContext context, CancellationToken cancellationToken)
    {
        if (_password is null)
        {
            return Task.FromResult(VerifyResult.Inconclusive("Không còn mật khẩu để xác minh."));
        }

        try
        {
            var error = LocalAccounts.TryLogon(UserName, _password);
            return Task.FromResult(error switch
            {
                0 => VerifyResult.Passed("Đăng nhập thử bằng mật khẩu mới thành công."),
                // Windows chặn đăng nhập thử qua mạng (chính sách) — mật khẩu đã đặt nhưng không tự xác minh được.
                1327 or 1385 or 1331 or 1793 => VerifyResult.Attention("Đã đặt mật khẩu; Windows không cho đăng nhập thử để xác minh. Hãy thử đăng nhập trên máy."),
                1326 => VerifyResult.Failed("Đăng nhập thử bằng mật khẩu mới không thành công."),
                _ => VerifyResult.Inconclusive("Không xác minh được (mã " + error.ToString(CultureInfo.InvariantCulture) + ")."),
            });
        }
        finally
        {
            // Không giữ mật khẩu lâu hơn lượt chạy này.
            _password = null;
        }
    }

    public Task<VerifyResult> RollbackAsync(TaskContext context, CancellationToken cancellationToken) =>
        Task.FromResult(VerifyResult.Inconclusive(Descriptor.RollbackNote!));
}
