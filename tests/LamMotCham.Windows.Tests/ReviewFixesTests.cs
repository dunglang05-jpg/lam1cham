using System.Text.Json;
using LamMotCham.Core.Logging;
using LamMotCham.Core.Tasks;
using LamMotCham.Windows.Checks;
using LamMotCham.Windows.Cleanup;
using LamMotCham.Windows.Config;
using LamMotCham.Windows.Disk;
using LamMotCham.Windows.Infrastructure;
using LamMotCham.Windows.Probe;
using LamMotCham.Windows.Software;
using Microsoft.Win32;
using Xunit;

namespace LamMotCham.Windows.Tests;

/// <summary>Test tái hiện cho các lỗi trong báo cáo rà soát BC-002.</summary>
public sealed class ReviewFixesTests
{
    [Fact]
    public void P1_3_running_as_another_account_is_detected()
    {
        Assert.Null(UserContext.Describe(@"PC-01\khach", @"PC-01\khach"));
        Assert.Null(UserContext.Describe(@"PC-01\KHACH", @"pc-01\khach"));
        Assert.Null(UserContext.Describe(@"PC-01\admin", null));
        Assert.Null(UserContext.Describe(null, @"PC-01\khach"));

        var reason = UserContext.Describe(@"PC-01\kythuat", @"PC-01\khach");
        Assert.NotNull(reason);
        Assert.Contains(@"PC-01\kythuat", reason, StringComparison.Ordinal);
        Assert.Contains(@"PC-01\khach", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void P1_4_machine_wide_packages_request_machine_scope()
    {
        var machine = WinGetClient.InstallArguments("Google.Chrome", machineScope: true);
        var user = WinGetClient.InstallArguments("UniKey.UniKey", machineScope: false);

        Assert.Equal(new[] { "--scope", "machine" }, machine.Skip(machine.Count - 2));
        Assert.DoesNotContain("--scope", user);
        Assert.Contains("--exact", machine);
        Assert.Contains("--silent", user);
    }

    [Fact]
    public void P2_3_publisher_must_match_catalog_in_any_display_language()
    {
        const string English = "Found Google Chrome [Google.Chrome]\nVersion: 130.0.6723.70\nPublisher: Google LLC\nPublisher Url: https://www.google.com\n";
        const string Vietnamese = "Đã tìm thấy Google Chrome [Google.Chrome]\nPhiên bản: 130.0\nNhà phát hành: Google LLC\n";
        const string Hijacked = "Found Chrome [Google.Chrome]\nVersion: 1.0\nPublisher: Evil Corp\nPublisher Url: https://google.com\n";

        Assert.True(WinGetClient.PublisherMatches(English, "Google LLC"));
        Assert.True(WinGetClient.PublisherMatches(Vietnamese, "Google LLC"));
        Assert.False(WinGetClient.PublisherMatches(Hijacked, "Google LLC"));
        Assert.False(WinGetClient.PublisherMatches("", "Google LLC"));
        Assert.True(SoftwareCatalog.All.All(p => !string.IsNullOrWhiteSpace(p.ExpectedPublisher)));
    }

    [Fact]
    public void P2_2_recycle_bin_is_destructive_and_needs_confirmation()
    {
        var recycle = new CleanupTask(new RecycleBinGroup()).Descriptor;
        var temp = new CleanupTask(OldFilesGroup.UserTemp()).Descriptor;

        Assert.Equal(RiskLevel.Destructive, recycle.Risk);
        Assert.True(recycle.RequiresConfirmation);
        Assert.False(recycle.AllowForcedStop);
        Assert.Empty(recycle.Validate());

        Assert.NotEqual(RiskLevel.Destructive, temp.Risk);
        Assert.False(temp.RequiresConfirmation);
        Assert.Empty(temp.Validate());
    }

    [Fact]
    public void P2_4_wifi_and_bluetooth_are_not_passed_on_detection_alone()
    {
        var notConnected = Snap("""{"adapters":[{"media":"Native 802.11","desc":"Intel Wi-Fi 6","status":"Disconnected"}],"errors":[]}""");
        var connected = Snap("""{"adapters":[{"media":"Native 802.11","desc":"Intel Wi-Fi 6","status":"Up"}],"errors":[]}""");
        var bluetooth = Snap("""{"devices":[{"cls":"Bluetooth","name":"Intel Wireless Bluetooth","err":0}],"errors":[]}""");

        Assert.Equal(VerifyOutcome.RequiresPhysicalCheck, InventoryChecks.Wifi(notConnected).Outcome);
        Assert.Equal(VerifyOutcome.Passed, InventoryChecks.Wifi(connected).Outcome);
        Assert.Equal(VerifyOutcome.RequiresPhysicalCheck, InventoryChecks.Bluetooth(bluetooth).Outcome);
    }

    [Fact]
    public void P3_1_trailing_newline_is_rejected_in_names()
    {
        Assert.False(DiskSplitPlanner.IsValidLabel("Du lieu\n"));
        Assert.True(DiskSplitPlanner.IsValidLabel("Du lieu"));
        Assert.False(RenameComputerTask.IsValidName("STORE-001\n"));
        Assert.True(RenameComputerTask.IsValidName("STORE-001"));
        Assert.False(SoftwareCatalog.IsSafeId("Google.Chrome\n"));
    }

    [Fact]
    public void P2_1_corrupt_rollback_file_is_reported_not_crashing()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lmc-rb-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new RollbackStore(dir);
            Assert.False(store.Exists("win.fileext"));

            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "win.fileext.json"), "{không phải json");

            Assert.True(store.Exists("win.fileext"));
            Assert.Throws<InvalidDataException>(() => store.Load("win.fileext"));
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    private static InventorySnapshot Snap(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return new InventorySnapshot(doc.RootElement, DateTimeOffset.UtcNow);
    }
}

/// <summary>Các test đổi <see cref="UserContext.OverrideForTests"/> (biến tĩnh) chạy tuần tự trong một lớp.</summary>
public sealed class RegistryRollbackTests : IDisposable
{
    private readonly string _subKey = @"Software\LamMotCham.Tests\" + Guid.NewGuid().ToString("N");
    private readonly string _storeDir = Path.Combine(Path.GetTempPath(), "lmc-rb-" + Guid.NewGuid().ToString("N"));
    private readonly TaskContext _context = new(new InMemoryRunLogger(), 1);

    private RegistryValuesTask NewTask(RollbackStore store) => new(
        new TaskDescriptor
        {
            Id = "test.reg",
            DisplayName = "Test registry",
            Version = 1,
            Risk = RiskLevel.Reversible,
            Privilege = PrivilegeLevel.User,
            Kind = TaskKind.Configuration,
            Timeout = TimeSpan.FromSeconds(10),
            Rollback = RollbackSupport.Supported,
        },
        [new RegistryDword(_subKey, "A", 0), new RegistryDword(_subKey, "B", 0)],
        store,
        v => string.Join(",", v));

    [Fact]
    public async Task P1_3_per_user_setting_is_skipped_when_running_as_another_account()
    {
        UserContext.OverrideForTests = () => "chạy bằng tài khoản khác";
        try
        {
            var result = await NewTask(new RollbackStore(_storeDir)).CheckPreconditionsAsync(_context, CancellationToken.None);

            Assert.Equal(PreconditionOutcome.NotMet, result.Outcome);
            Assert.Contains("tài khoản khác", result.Reason, StringComparison.Ordinal);
        }
        finally
        {
            UserContext.OverrideForTests = null;
        }
    }

    [Fact]
    public async Task P2_1_rollback_reads_back_old_values_before_reporting_success()
    {
        UserContext.OverrideForTests = () => null;
        try
        {
            using (var key = Registry.CurrentUser.CreateSubKey(_subKey, writable: true))
            {
                key.SetValue("A", 5, RegistryValueKind.DWord);   // B chưa có: hoàn tác phải xóa B
            }

            var store = new RollbackStore(_storeDir);
            var task = NewTask(store);
            Assert.Equal(ExecuteOutcome.Completed, (await task.ExecuteAsync(_context, CancellationToken.None)).Outcome);
            Assert.Equal(VerifyOutcome.Passed, (await task.VerifyAsync(_context, CancellationToken.None)).Outcome);

            var undo = await task.RollbackAsync(_context, CancellationToken.None);

            Assert.Equal(VerifyOutcome.Passed, undo.Outcome);
            using var check = Registry.CurrentUser.OpenSubKey(_subKey, writable: false);
            Assert.Equal(5, check?.GetValue("A"));
            Assert.Null(check?.GetValue("B"));
            Assert.False(store.Exists("test.reg"));
        }
        finally
        {
            UserContext.OverrideForTests = null;
        }
    }

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(_subKey, throwOnMissingSubKey: false);
        if (Directory.Exists(_storeDir))
        {
            Directory.Delete(_storeDir, recursive: true);
        }
    }
}
