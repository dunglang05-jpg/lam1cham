using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using LamMotCham.Core.Tasks;
using LamMotCham.Windows.Checks;
using LamMotCham.Windows.Config;
using LamMotCham.Windows.Infrastructure;
using LamMotCham.Windows.Probe;
using LamMotCham.Windows.Software;
using Xunit;

namespace LamMotCham.Windows.Tests;

public sealed partial class InventoryGroupTests
{
    [GeneratedRegex(@"Sec '(\w+)'", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex SectionRegex();

    [Fact]
    public void Groups_cover_every_section_exactly_once()
    {
        var names = InventoryScript.Groups
            .SelectMany(g => SectionRegex().Matches(g.Script).Select(m => m.Groups[1].Value))
            .ToList();

        Assert.Equal(19, names.Count);
        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
        Assert.All(InventoryScript.Groups, g => Assert.Contains("ConvertTo-Json", g.Script, StringComparison.Ordinal));
        Assert.All(InventoryScript.Groups, g => Assert.Contains("function Sec", g.Script, StringComparison.Ordinal));
    }

    [Fact]
    public void Device_query_filters_in_wql_instead_of_reading_everything()
    {
        var devices = InventoryScript.Groups.Single(g => g.Script.Contains("Sec 'devices'", StringComparison.Ordinal)).Script;

        Assert.Contains("ConfigManagerErrorCode <> 0", devices, StringComparison.Ordinal);
        Assert.DoesNotContain("$all = @(Get-CimInstance Win32_PnPEntity -ErrorAction Stop)", devices, StringComparison.Ordinal);
    }

    [Fact]
    public void Merge_keeps_first_value_and_joins_errors()
    {
        var a = new JsonObject { ["cpu"] = "i3", ["errors"] = new JsonArray("activation: lỗi") };
        var b = new JsonObject { ["cpu"] = "khác", ["battery"] = new JsonArray(), ["errors"] = new JsonArray("devices: lỗi") };

        var merged = InventoryProvider.Merge([a, b]);
        using var doc = JsonDocument.Parse(merged.ToJsonString());
        var snap = new InventorySnapshot(doc.RootElement, DateTimeOffset.UtcNow);

        Assert.Equal("i3", snap.Str("cpu"));
        Assert.Equal(2, snap.Errors.Count);
        Assert.False(snap.IsQuick);
    }

    [Fact]
    public void Quick_battery_reports_charge_and_charging_without_claiming_health()
    {
        var o = new JsonObject
        {
            ["source"] = QuickProbe.SourceQuick,
            ["battery"] = new JsonArray(new JsonObject { ["charge"] = 76, ["status"] = 2 }),
            ["errors"] = new JsonArray(),
        };
        var snap = QuickProbe.ToSnapshot(o);
        var result = InventoryChecks.Battery(snap);

        Assert.True(snap.IsQuick);
        Assert.Equal(VerifyOutcome.Passed, result.Outcome);
        Assert.Contains("76%", result.Detail, StringComparison.Ordinal);
        Assert.Contains("đang cắm sạc", result.Detail, StringComparison.Ordinal);
    }
}

public sealed class BitLockerTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    [InlineData(4, true)]
    [InlineData(5, false)]
    public void Only_encrypted_volumes_can_be_turned_off(int conversion, bool expected)
    {
        Assert.Equal(expected, new BitLockerVolume('C', true, conversion, 100, 1).CanTurnOff);
    }

    [Fact]
    public void Turn_off_is_a_windows_setting_in_full_setup()
    {
        var catalog = WindowsCatalog.Build(new CatalogServices(
            new InventoryProvider(),
            new WinGetClient(() => null),
            new RollbackStore(Path.Combine(Path.GetTempPath(), "lmc-rb-" + Guid.NewGuid().ToString("N")))));
        var entry = Assert.Single(catalog, e => e.Module.Descriptor.Id == BitLockerOffTask.TaskId);
        var d = entry.Module.Descriptor;

        Assert.Equal(TaskCategory.WindowsSettings, entry.Category);
        Assert.Equal(WindowsCatalog.GroupPrivacy, entry.Group);
        Assert.True(entry.IsInPack(TaskPack.FullSetup));
        Assert.False(entry.IsInPack(TaskPack.SoftwareOnly));
        Assert.True(d.RequiresConfirmation);
        Assert.NotEqual(RiskLevel.Destructive, d.Risk);
        Assert.Equal(PrivilegeLevel.Administrator, d.Privilege);
        Assert.Empty(d.Validate());
        Assert.IsAssignableFrom<IDescribesCurrentState>(entry.Module);
    }
}
