using LamMotCham.Core.Logging;
using LamMotCham.Core.Tasks;
using LamMotCham.Windows.Config;
using LamMotCham.Windows.Infrastructure;
using Xunit;

namespace LamMotCham.Windows.Tests;

/// <summary>PO 01/11/2026: đưa Word, Excel, PowerPoint ra desktop trong mô-đun Cài đặt &amp; Tối ưu.</summary>
public sealed class OfficeDesktopShortcutsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "l1c-office-" + Guid.NewGuid().ToString("N"));

    private string Desktop => Path.Combine(_root, "desktop");

    private OfficeDesktopShortcutsTask Create() => new(new RollbackStore(Path.Combine(_root, "rollback")), () => Desktop);

    private static TaskContext Context() => new(new InMemoryRunLogger(), 1);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Creates_a_real_windows_shortcut()
    {
        Directory.CreateDirectory(Desktop);
        var target = Path.Combine(Environment.SystemDirectory, "notepad.exe");
        var lnk = Path.Combine(Desktop, "Thử.lnk");

        OfficeDesktopShortcutsTask.ShellLink.Create(lnk, target);

        Assert.True(File.Exists(lnk));
        var bytes = File.ReadAllBytes(lnk);
        Assert.Equal(0x4C, bytes[0]); // Đầu file .lnk luôn là 0x4C (ShellLinkHeader).
    }

    [Fact]
    public async Task Without_office_it_reports_not_supported_and_changes_nothing()
    {
        if (OfficeDesktopShortcutsTask.FindSources().Count > 0)
        {
            return; // Máy có Office: phần này do kiểm thử đầu-cuối trên máy cài Microsoft 365 đảm nhận.
        }

        var task = Create();
        var pre = await task.CheckPreconditionsAsync(Context(), CancellationToken.None);

        Assert.Equal(PreconditionOutcome.NotSupported, pre.Outcome);
        Assert.Contains("chưa cài Microsoft Office", pre.Reason, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Desktop));
        Assert.Equal("chưa cài Office", await task.DescribeCurrentStateAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Rollback_only_removes_shortcuts_it_created()
    {
        Directory.CreateDirectory(Desktop);
        var mine = Path.Combine(Desktop, "Word.lnk");
        var theirs = Path.Combine(Desktop, "Excel.lnk");
        File.WriteAllText(mine, "x");
        File.WriteAllText(theirs, "y");
        var store = new RollbackStore(Path.Combine(_root, "rollback"));
        store.Save("win.officedesktop", new Dictionary<string, string?> { ["Word"] = mine });
        var task = new OfficeDesktopShortcutsTask(store, () => Desktop);

        var undo = await task.RollbackAsync(Context(), CancellationToken.None);

        Assert.Equal(VerifyOutcome.Passed, undo.Outcome);
        Assert.False(File.Exists(mine));
        Assert.True(File.Exists(theirs), "Lối tắt có sẵn của khách không được xóa.");
    }

    [Fact]
    public void Descriptor_is_valid_and_reversible()
    {
        var task = Create();
        Assert.Empty(task.Descriptor.Validate());
        Assert.Equal(RollbackSupport.Supported, task.Descriptor.Rollback);
    }
}
