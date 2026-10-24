using LamMotCham.Core.Account;
using LamMotCham.Windows.Account;
using Xunit;

namespace LamMotCham.Windows.Tests;

public sealed class MachineIdentityTests
{
    [Fact]
    public void Machine_id_is_stable_hashed_and_accepted_by_server()
    {
        var a = MachineIdentity.FromSource("4c4c4544-0042-3510-8051-b4c04f4e4d32");
        var b = MachineIdentity.FromSource("  4C4C4544-0042-3510-8051-B4C04F4E4D32 ");

        Assert.Equal(a, b);
        Assert.StartsWith("lmc-", a, StringComparison.Ordinal);
        Assert.Equal(24, a.Length);
        Assert.DoesNotContain("4c4c4544", a, StringComparison.OrdinalIgnoreCase);
        Assert.True(AccountValidation.IsValidMachineId(a));
    }

    [Fact]
    public void Different_machines_get_different_ids() =>
        Assert.NotEqual(MachineIdentity.FromSource("may-a"), MachineIdentity.FromSource("may-b"));

    [Fact]
    public void Current_machine_id_is_valid() =>
        Assert.True(AccountValidation.IsValidMachineId(MachineIdentity.Get()));
}
