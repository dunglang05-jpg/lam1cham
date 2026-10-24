using System.Text.Json;
using LamMotCham.Core.Orchestration;
using LamMotCham.Core.Tasks;
using LamMotCham.Windows;
using LamMotCham.Windows.Checks;
using LamMotCham.Windows.Cleanup;
using LamMotCham.Windows.Config;
using LamMotCham.Windows.Infrastructure;
using LamMotCham.Windows.Probe;
using LamMotCham.Windows.Software;
using Xunit;

namespace LamMotCham.Windows.Tests;

public sealed class CatalogTests
{
    private static IReadOnlyList<TaskCatalogEntry> Catalog() =>
        WindowsCatalog.Build(new CatalogServices(
            new InventoryProvider(),
            new WinGetClient(() => null),
            new RollbackStore(Path.Combine(Path.GetTempPath(), "lmc-rb-" + Guid.NewGuid().ToString("N")))));

    [Fact]
    public void Every_task_in_catalog_has_a_valid_declaration_and_unique_id()
    {
        var modules = Catalog().Select(e => e.Module).Append(WindowsCatalog.Rename("STORE-001").Module).ToList();
        var errors = TaskOrchestrator.ValidateQueue(modules);

        Assert.Empty(errors);
    }

    [Fact]
    public void Software_entries_carry_logo_keys_only_for_bundled_logos()
    {
        var sw = Catalog().Where(e => e.Category == TaskCategory.Software).ToList();
        var logos = sw.Where(e => e.LogoKey is not null).Select(e => e.LogoKey!).ToList();

        Assert.Equal(8, logos.Count);
        Assert.Equal(logos.Count, logos.Distinct(StringComparer.Ordinal).Count());
        Assert.Null(SoftwareCatalog.LogoKeyFor("unikey"));
        Assert.Null(SoftwareCatalog.LogoKeyFor("sumatra"));
        Assert.Equal("chrome", SoftwareCatalog.LogoKeyFor("chrome"));
        Assert.Matches(@"^\d{4}\.\d{2}\.\d{2}$", SoftwareCatalog.DataVersion);
    }

    [Fact]
    public void Every_windows_change_explains_six_points()
    {
        var changes = Catalog().Where(e => e.Category == TaskCategory.WindowsSettings && !e.IsReadOnly).ToList();

        Assert.NotEmpty(changes);
        Assert.True(changes.All(e => e.Details is not null));
    }

    [Fact]
    public void Installers_are_never_force_stopped()
    {
        var installers = Catalog().Where(e => e.Module.Descriptor.Kind == TaskKind.Installer).ToList();

        Assert.Equal(SoftwareCatalog.All.Count, installers.Count);
        Assert.True(installers.All(e => !e.Module.Descriptor.AllowForcedStop));
    }

    [Fact]
    public void Risky_items_are_not_selected_by_default()
    {
        var recycle = Catalog().Single(e => e.Id == "clean.recycle");

        Assert.False(recycle.SelectedByDefault);
        Assert.False(recycle.IsInPack(TaskPack.FullSetup));
    }

    [Fact]
    public void Check_only_pack_contains_only_read_only_tasks()
    {
        var picked = Catalog().Where(e => e.IsInPack(TaskPack.CheckOnly)).ToList();

        Assert.NotEmpty(picked);
        Assert.True(picked.All(e => e.Module.Descriptor.Risk == RiskLevel.ReadOnly));
    }
}

public sealed class WinGetTests
{
    [Fact]
    public void Catalog_ids_are_safe_and_injection_is_rejected()
    {
        Assert.True(SoftwareCatalog.All.All(p => SoftwareCatalog.IsSafeId(p.WinGetId)));
        Assert.False(SoftwareCatalog.IsSafeId("Google.Chrome & calc"));
        Assert.False(SoftwareCatalog.IsSafeId("Google.Chrome\" --override \"x"));
        Assert.False(SoftwareCatalog.IsSafeId("../evil"));
        Assert.False(SoftwareCatalog.IsSafeId(""));
    }

    [Fact]
    public async Task Package_outside_catalog_is_refused_before_running_anything()
    {
        var client = new WinGetClient(() => @"C:\Fake\winget.exe");
        await Assert.ThrowsAsync<ArgumentException>(() => client.InstallAsync("Evil.Package", machineScope: true, CancellationToken.None));
    }

    [Fact]
    public void Exit_codes_are_mapped_to_actionable_meaning()
    {
        Assert.True(WinGetClient.Describe(unchecked((int)0x8A150008)).IsTransient);
        Assert.True(WinGetClient.Describe(unchecked((int)0x8A150109)).NeedsReboot);
        Assert.True(WinGetClient.Describe(unchecked((int)0x8A150061)).CountsAsInstalled);
        Assert.False(WinGetClient.Describe(unchecked((int)0x8A15010F)).IsTransient);
        Assert.Contains("không xác định", WinGetClient.Describe(12345).Message);
    }

    [Fact]
    public void Parses_winget_version()
    {
        Assert.Equal(new Version(1, 11, 430), WinGetClient.ParseVersion("v1.11.430"));
        Assert.Null(WinGetClient.ParseVersion("không phải phiên bản"));
    }
}

public sealed class ConfigTests
{
    [Fact]
    public void Computer_name_rules()
    {
        Assert.True(RenameComputerTask.IsValidName("STORE-001"));
        Assert.True(RenameComputerTask.IsValidName("A"));
        Assert.False(RenameComputerTask.IsValidName("12345"));
        Assert.False(RenameComputerTask.IsValidName("-STORE"));
        Assert.False(RenameComputerTask.IsValidName("CUA HANG"));
        Assert.False(RenameComputerTask.IsValidName("TEN-MAY-QUA-DAI-16"));
        Assert.False(RenameComputerTask.IsValidName("MÁY1"));
        Assert.Throws<ArgumentException>(() => _ = new RenameComputerTask("x;y"));
    }

    [Fact]
    public void Power_timeouts_describe_in_minutes()
    {
        Assert.Equal("cắm sạc 15 phút/30 phút, pin 5 phút/15 phút", PowerTimeouts.Default.ToString());
    }

    [Fact]
    public void Formats_sizes_in_vietnamese()
    {
        Assert.Equal("1,5 GB", CleanupTask.FormatBytes(1610612736));
        Assert.Equal("214 MB", CleanupTask.FormatBytes(214L * 1024 * 1024));
    }
}

public sealed class InventoryCheckTests
{
    private static InventorySnapshot Snap(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return new InventorySnapshot(doc.RootElement, DateTimeOffset.UtcNow);
    }

    [Fact]
    public void Windows_10_needs_attention()
    {
        var r = InventoryChecks.WindowsVersion(Snap("""{"osBuild":19045,"osCaption":"Windows 10 Pro","osDisplayVersion":"22H2","osUbr":5000,"errors":[]}"""));
        Assert.Equal(VerifyOutcome.Attention, r.Outcome);
    }

    [Fact]
    public void Missing_data_is_never_a_pass()
    {
        var empty = Snap("""{"errors":["activation: access denied","bitlocker: access denied"]}""");

        Assert.Equal(VerifyOutcome.Inconclusive, InventoryChecks.WindowsVersion(empty).Outcome);
        Assert.Equal(VerifyOutcome.Inconclusive, InventoryChecks.Activation(empty).Outcome);
        Assert.Equal(VerifyOutcome.Inconclusive, InventoryChecks.BitLocker(empty).Outcome);
        Assert.Equal(VerifyOutcome.Inconclusive, InventoryChecks.Firewall(empty).Outcome);
        Assert.Equal(VerifyOutcome.Inconclusive, InventoryChecks.Security(empty).Outcome);
    }

    [Fact]
    public void Activation_and_firewall_are_evaluated()
    {
        var s = Snap("""
            {"activation":{"name":"Windows(R), Professional edition","status":1},
             "firewall":[{"name":"Domain","enabled":true},{"name":"Private","enabled":true},{"name":"Public","enabled":false}],
             "errors":[]}
            """);

        Assert.Equal(VerifyOutcome.Passed, InventoryChecks.Activation(s).Outcome);
        var fw = InventoryChecks.Firewall(s);
        Assert.Equal(VerifyOutcome.Attention, fw.Outcome);
        Assert.Contains("Public", fw.Detail);
    }

    [Fact]
    public void Third_party_antivirus_counts_as_protected()
    {
        var s = Snap("""{"antivirus":[{"name":"ESET Security","state":266240}],"defender":{"realTime":false,"antivirus":false},"errors":[]}""");
        Assert.Equal(VerifyOutcome.Passed, InventoryChecks.Security(s).Outcome);
    }

    [Fact]
    public void Defender_off_without_other_antivirus_needs_attention()
    {
        var s = Snap("""{"antivirus":[{"name":"Windows Defender","state":393472}],"defender":{"realTime":false,"antivirus":true},"errors":[]}""");
        Assert.Equal(VerifyOutcome.Attention, InventoryChecks.Security(s).Outcome);
    }

    [Fact]
    public void Battery_rules()
    {
        Assert.Equal(VerifyOutcome.NotSupported, InventoryChecks.Battery(Snap("""{"battery":[],"errors":[]}""")).Outcome);
        Assert.Equal(VerifyOutcome.Attention, InventoryChecks.Battery(Snap(
            """{"battery":[{"charge":80}],"batteryCapacity":{"design":50000,"full":30000},"errors":[]}""")).Outcome);
        Assert.Equal(VerifyOutcome.Passed, InventoryChecks.Battery(Snap(
            """{"battery":[{"charge":80}],"batteryCapacity":{"design":50000,"full":47000},"errors":[]}""")).Outcome);
    }

    [Fact]
    public void Bitlocker_on_system_drive_needs_attention()
    {
        var s = Snap("""{"systemDrive":"C:","bitlocker":[{"drive":"C:","protection":1},{"drive":"D:","protection":0}],"errors":[]}""");
        Assert.Equal(VerifyOutcome.Attention, InventoryChecks.BitLocker(s).Outcome);
    }

    [Fact]
    public void Low_disk_space_and_unhealthy_disks_need_attention()
    {
        var low = Snap("""{"systemDrive":"C:","volumes":[{"drive":"C:","size":256000000000,"free":10000000000}],"physicalDisks":[{"name":"SSD","health":"Healthy"}],"errors":[]}""");
        var bad = Snap("""{"systemDrive":"C:","volumes":[{"drive":"C:","size":512000000000,"free":400000000000}],"physicalDisks":[{"name":"SSD","health":"Warning"}],"errors":[]}""");
        var ok = Snap("""{"systemDrive":"C:","volumes":[{"drive":"C:","size":512000000000,"free":400000000000}],"physicalDisks":[{"name":"SSD","health":"Healthy"}],"errors":[]}""");

        Assert.Equal(VerifyOutcome.Attention, InventoryChecks.DiskHealth(low).Outcome);
        Assert.Equal(VerifyOutcome.Attention, InventoryChecks.DiskHealth(bad).Outcome);
        Assert.Equal(VerifyOutcome.Passed, InventoryChecks.DiskHealth(ok).Outcome);
    }

    [Fact]
    public void Trial_software_and_driver_problems_are_reported()
    {
        var s = Snap("""
            {"apps":[{"name":"McAfee LiveSafe","publisher":"McAfee, LLC"},{"name":"7-Zip 24.08","publisher":"Igor Pavlov"}],
             "problemDevices":[{"cls":"Unknown","name":"PCI Data Acquisition","err":28}],"errors":[]}
            """);

        var trial = InventoryChecks.TrialSoftware(s);
        Assert.Equal(VerifyOutcome.Attention, trial.Outcome);
        Assert.Contains("McAfee", trial.Detail);
        Assert.DoesNotContain("7-Zip", trial.Detail);
        Assert.Equal(VerifyOutcome.Attention, InventoryChecks.Drivers(s).Outcome);
    }

    [Fact]
    public void Devices_needing_a_human_are_marked_not_tested()
    {
        var s = Snap("""{"devices":[{"cls":"Camera","name":"Integrated Camera","err":0}],"keyboards":[{"name":"Standard PS/2 Keyboard"}],"errors":[]}""");

        Assert.Equal(VerifyOutcome.RequiresPhysicalCheck, InventoryChecks.Camera(s).Outcome);
        Assert.Equal(VerifyOutcome.RequiresPhysicalCheck, InventoryChecks.Keyboard(s).Outcome);
        Assert.Equal(VerifyOutcome.RequiresPhysicalCheck, InventoryChecks.Ports(s).Outcome);
    }
}
