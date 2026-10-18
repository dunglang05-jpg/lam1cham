using LamMotCham.Core.Tasks;
using LamMotCham.Windows.Accounts;
using Xunit;

namespace LamMotCham.Windows.Tests;

public sealed class AccountTests
{
    [Theory]
    [InlineData("KhachHang", true)]
    [InlineData("nguyen.van.a", true)]
    [InlineData("Dung 01", true)]
    [InlineData("", false)]
    [InlineData(" Dung", false)]
    [InlineData("Dung ", false)]
    [InlineData("a/b", false)]
    [InlineData("a@b", false)]
    [InlineData("...", false)]
    [InlineData("TenRatDaiQuaHaiMuoiKyTu", false)]
    public void User_name_rules_follow_windows(string name, bool expected)
    {
        Assert.Equal(expected, LocalAccounts.IsValidUserName(name));
    }

    [Fact]
    public void Account_tasks_declare_admin_and_valid_descriptors()
    {
        var modules = new ITaskModule[]
        {
            new UserFullNameTask("KhachHang", "Nguyễn Văn A"),
            new UserRenameTask("KhachHang", "NguyenVanA"),
            new UserPasswordTask("KhachHang", "Mk@123456"),
        };

        Assert.All(modules, m => Assert.Empty(m.Descriptor.Validate()));
        Assert.All(modules, m => Assert.Equal(PrivilegeLevel.Administrator, m.Descriptor.Privilege));
        Assert.All(modules, m => Assert.NotEqual(RiskLevel.Destructive, m.Descriptor.Risk));
    }

    [Fact]
    public void Password_never_appears_in_task_metadata()
    {
        const string secret = "Bi-Mat-987!";
        var entry = WindowsCatalog.UserPassword("KhachHang", secret);
        var d = entry.Module.Descriptor;

        Assert.True(d.RequiresConfirmation);
        Assert.Equal(RollbackSupport.NotPossible, d.Rollback);
        Assert.DoesNotContain(secret, d.Id, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, d.DisplayName, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, entry.Description, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, d.RollbackNote ?? "", StringComparison.Ordinal);
        Assert.False(entry.IsInPack(TaskPack.FullSetup));
    }

    [Fact]
    public void Invalid_input_is_rejected_before_any_change()
    {
        Assert.Throws<ArgumentException>(() => new UserRenameTask("KhachHang", "bad/name"));
        Assert.Throws<ArgumentException>(() => new UserFullNameTask("KhachHang", "a\nb"));
        Assert.ThrowsAny<ArgumentException>(() => new UserPasswordTask("KhachHang", ""));
    }
}
