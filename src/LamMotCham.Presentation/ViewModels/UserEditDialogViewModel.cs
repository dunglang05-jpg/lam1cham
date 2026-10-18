using LamMotCham.Presentation.Mvvm;
using LamMotCham.Windows;
using LamMotCham.Windows.Accounts;

namespace LamMotCham.Presentation.ViewModels;

/// <summary>
/// Hộp "Sửa tài khoản" (mô-đun disk): tên hiển thị, tên đăng nhập, mật khẩu mới.
/// Chỉ tài khoản cục bộ. Thay đổi chạy qua hàng đợi (xác minh sau khi đổi); mật khẩu không được ghi ra đâu cả.
/// </summary>
public sealed class UserEditDialogViewModel : DialogViewModel
{
    private string _newName;
    private string _fullName;
    private string _password = "";
    private string _passwordConfirm = "";
    private bool _agreed;

    public UserEditDialogViewModel(MainViewModel main, LocalAccount account, Func<Task> afterRun)
        : base(main)
    {
        ArgumentNullException.ThrowIfNull(account);
        Account = account;
        _newName = account.Name;
        _fullName = account.FullName;
        SaveCommand = new AsyncCommand(async () =>
        {
            var entries = BuildEntries();
            Close();
            ClearPasswords();
            await main.RunAsync([.. entries.Select(e => new TaskRowViewModel(e) { IsSelected = true })], "Sửa tài khoản " + account.Name);
            await afterRun();
        }, () => CanSave);
    }

    public LocalAccount Account { get; }

    public AsyncCommand SaveCommand { get; }

    public string NewName
    {
        get => _newName;
        set
        {
            if (Set(ref _newName, value ?? ""))
            {
                Changed();
            }
        }
    }

    public string FullName
    {
        get => _fullName;
        set
        {
            if (Set(ref _fullName, value ?? ""))
            {
                Changed();
            }
        }
    }

    /// <summary>Đặt từ ô mật khẩu (code-behind). Không thông báo giá trị ra ngoài.</summary>
    public void SetPassword(string value)
    {
        _password = value ?? "";
        Changed();
    }

    public void SetPasswordConfirm(string value)
    {
        _passwordConfirm = value ?? "";
        Changed();
    }

    public bool ChangesPassword => _password.Length > 0;

    public bool Agreed
    {
        get => _agreed;
        set
        {
            if (Set(ref _agreed, value))
            {
                Changed();
            }
        }
    }

    public bool RenamesCurrentUser => Account.IsCurrent && NameChanged;

    /// <summary>Lỗi nhập liệu hiện tại, hoặc rỗng khi hợp lệ.</summary>
    public string Problem =>
        !LocalAccounts.IsValidUserName(_newName.Trim()) ? "Tên đăng nhập: 1–20 ký tự, không chứa \" / \\ [ ] : ; | = , + * ? < > @."
        : !LocalAccounts.IsValidFullName(_fullName) ? "Tên hiển thị tối đa 256 ký tự."
        : _password.Length > 0 && !string.Equals(_password, _passwordConfirm, StringComparison.Ordinal) ? "Hai lần nhập mật khẩu chưa khớp."
        : _password.Length > 0 && !_agreed ? "Đánh dấu ô xác nhận để đặt mật khẩu mới."
        : !HasChanges ? "Chưa có thay đổi."
        : "";

    public string Summary
    {
        get
        {
            var parts = new List<string>();
            if (FullNameChanged)
            {
                parts.Add("tên hiển thị");
            }

            if (ChangesPassword)
            {
                parts.Add("mật khẩu");
            }

            if (NameChanged)
            {
                parts.Add("tên đăng nhập");
            }

            return parts.Count == 0 ? "" : "Sẽ đổi: " + string.Join(", ", parts) + ". Cần quyền quản trị; mỗi thay đổi được đọc lại để xác minh.";
        }
    }

    private bool NameChanged => !string.Equals(_newName.Trim(), Account.Name, StringComparison.Ordinal);

    private bool FullNameChanged => !string.Equals(_fullName.Trim(), Account.FullName, StringComparison.Ordinal);

    private bool HasChanges => NameChanged || FullNameChanged || ChangesPassword;

    private bool CanSave => Problem.Length == 0 && !Main.IsRunning;

    private List<LamMotCham.Core.Tasks.TaskCatalogEntry> BuildEntries()
    {
        // Đổi tên đăng nhập sau cùng: các bước trước dùng tên cũ.
        var list = new List<LamMotCham.Core.Tasks.TaskCatalogEntry>();
        if (FullNameChanged)
        {
            list.Add(WindowsCatalog.UserFullName(Account.Name, _fullName.Trim()));
        }

        if (ChangesPassword)
        {
            list.Add(WindowsCatalog.UserPassword(Account.Name, _password));
        }

        if (NameChanged)
        {
            list.Add(WindowsCatalog.UserRename(Account.Name, _newName.Trim()));
        }

        return list;
    }

    private void ClearPasswords()
    {
        _password = "";
        _passwordConfirm = "";
    }

    private void Changed()
    {
        OnPropertyChanged(nameof(Problem));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(ChangesPassword));
        OnPropertyChanged(nameof(RenamesCurrentUser));
        Commands.InvalidateAll();
    }
}
