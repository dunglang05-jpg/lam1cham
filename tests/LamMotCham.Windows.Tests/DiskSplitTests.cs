using LamMotCham.Core.Tasks;
using LamMotCham.Windows.Disk;
using Xunit;

namespace LamMotCham.Windows.Tests;

public sealed class DiskSplitTests
{
    private const long GiB = DiskSplitPlanner.GiB;

    private static DiskSplitProbe Probe(long size = 476 * GiB, long min = 80 * GiB, long free = 400 * GiB) => new()
    {
        DriveLetter = 'C',
        PartitionSize = size,
        SupportedMin = min,
        FreeBytes = free,
        FileSystem = "NTFS",
        DiskNumber = 0,
        PartitionStyle = "GPT",
        PartitionCount = 4,
        ConversionStatus = 0,
        FreeLetter = 'D',
    };

    [Fact]
    public void Valid_plan_keeps_old_drive_and_computes_sizes()
    {
        var plan = DiskSplitPlanner.Plan(Probe(), new DiskSplitRequest('C', 200, "DATA"));

        Assert.True(plan.IsValid, string.Join(" ", plan.Errors));
        Assert.Equal(200 * GiB, plan.NewPartitionBytes);
        Assert.Equal(276 * GiB, plan.TargetSourceSize);
        Assert.Equal('D', plan.NewLetter);
    }

    [Fact]
    public void Old_drive_always_keeps_free_space()
    {
        // 476 GB, trống 100 GB: phải chừa max(30 GB, 15% = 71.4 GB) → tách tối đa 28 GB.
        var probe = Probe(free: 100 * GiB);
        var max = DiskSplitPlanner.MaxNewBytes(probe);

        Assert.Equal(28 * GiB, max);
        Assert.False(DiskSplitPlanner.Plan(probe, new DiskSplitRequest('C', 50, "DATA")).IsValid);
        Assert.True(DiskSplitPlanner.Plan(probe, new DiskSplitRequest('C', 20, "DATA")).IsValid);
    }

    [Fact]
    public void Respects_windows_shrink_limit()
    {
        // Windows chỉ cho thu nhỏ đến 450 GB (file không di chuyển được) → tách tối đa 24 GB.
        var probe = Probe(min: 450 * GiB);

        Assert.Equal(24 * GiB, DiskSplitPlanner.MaxNewBytes(probe));
        Assert.False(DiskSplitPlanner.Plan(probe, new DiskSplitRequest('C', 30, "DATA")).IsValid);
    }

    [Theory]
    [InlineData("FAT32", "GPT", 3, 0, 'D', "DATA")]
    [InlineData("NTFS", "MBR", 4, 0, 'D', "DATA")]
    [InlineData("NTFS", "RAW", 0, 0, 'D', "DATA")]
    [InlineData("NTFS", "GPT", 3, 2, 'D', "DATA")]
    [InlineData("NTFS", "GPT", 3, 0, null, "DATA")]
    [InlineData("NTFS", "GPT", 3, 0, 'D', "DATA'; Format-Volume C")]
    [InlineData("NTFS", "GPT", 3, 0, 'D', "")]
    public void Blocks_unsafe_situations(string fs, string style, int parts, int conversion, char? letter, string label)
    {
        var probe = Probe() with { FileSystem = fs, PartitionStyle = style, PartitionCount = parts, ConversionStatus = conversion, FreeLetter = letter };

        Assert.False(DiskSplitPlanner.Plan(probe, new DiskSplitRequest('C', 100, label)).IsValid);
    }

    [Fact]
    public void New_drive_must_be_at_least_ten_gigabytes()
    {
        Assert.False(DiskSplitPlanner.Plan(Probe(), new DiskSplitRequest('C', 5, "DATA")).IsValid);
        Assert.False(DiskSplitPlanner.Plan(Probe(), new DiskSplitRequest('C', double.NaN, "DATA")).IsValid);
    }

    [Fact]
    public void Encrypted_drive_is_allowed_with_warning()
    {
        var plan = DiskSplitPlanner.Plan(Probe() with { ConversionStatus = 1 }, new DiskSplitRequest('C', 100, "DATA"));

        Assert.True(plan.IsValid);
        Assert.NotEmpty(plan.Warnings);
    }

    [Fact]
    public void Split_task_is_destructive_needs_confirmation_and_cannot_be_force_stopped()
    {
        var task = new DiskSplitTask(DiskSplitPlanner.Plan(Probe(), new DiskSplitRequest('C', 100, "DATA")));

        Assert.Equal(RiskLevel.Destructive, task.Descriptor.Risk);
        Assert.True(task.Descriptor.RequiresConfirmation);
        Assert.False(task.Descriptor.AllowForcedStop);
        Assert.Empty(task.Descriptor.Validate());
        Assert.Throws<ArgumentException>(() => new DiskSplitTask(DiskSplitPlanner.Plan(Probe(), new DiskSplitRequest('C', 1, "DATA"))));
    }

    [Fact]
    public void Split_task_is_never_part_of_a_quick_pack()
    {
        var entry = WindowsCatalog.SplitDisk(DiskSplitPlanner.Plan(Probe(), new DiskSplitRequest('C', 100, "DATA")));

        Assert.All(Enum.GetValues<TaskPack>(), p => Assert.False(entry.IsInPack(p)));
    }
}
