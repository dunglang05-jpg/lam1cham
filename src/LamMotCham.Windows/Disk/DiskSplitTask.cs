using System.Globalization;
using System.Text.Json;
using LamMotCham.Core.Logging;
using LamMotCham.Core.Tasks;
using LamMotCham.Windows.Infrastructure;

namespace LamMotCham.Windows.Disk;

/// <summary>Đọc thông tin phân vùng để xem trước khi chia ổ. Chỉ đọc; cần quyền quản trị.</summary>
public static class DiskSplitProbeReader
{
    private const string Script = """
        $ErrorActionPreference = 'Stop'
        $L = '__L__'
        $r = [ordered]@{ ok = $true }
        try {
          $p = Get-Partition -DriveLetter $L
          $d = Get-Disk -Number $p.DiskNumber
          $v = Get-Volume -DriveLetter $L
          $s = Get-PartitionSupportedSize -DriveLetter $L
          $r.partitionSize = [int64]$p.Size
          $r.sizeMin = [int64]$s.SizeMin
          $r.free = [int64]$v.SizeRemaining
          $r.fileSystem = [string]$v.FileSystem
          $r.diskNumber = [int]$p.DiskNumber
          $r.partitionStyle = [string]$d.PartitionStyle
          $r.partitionCount = [int]@(Get-Partition -DiskNumber $p.DiskNumber).Count
          $used = @(Get-CimInstance Win32_LogicalDisk | ForEach-Object { ([string]$_.DeviceID).Substring(0,1).ToUpperInvariant() })
          $r.freeLetter = [string](@('D','E','F','G','H','I','J','K','L','M','N','O','P','Q','R','S','T','U','V','W','X','Y','Z') | Where-Object { $used -notcontains $_ } | Select-Object -First 1)
          try {
            $ev = Get-CimInstance -Namespace 'root\cimv2\Security\MicrosoftVolumeEncryption' -ClassName Win32_EncryptableVolume -Filter "DriveLetter='__L__:'"
            if ($ev) { $r.conversionStatus = [int](Invoke-CimMethod -InputObject $ev -MethodName GetConversionStatus).ConversionStatus } else { $r.conversionStatus = 0 }
          } catch { $r.conversionStatus = $null }
        } catch { $r.ok = $false; $r.error = [string]$_.Exception.Message }
        $r | ConvertTo-Json -Compress
        """;

    /// <summary>Trả về thông tin ổ, hoặc thông báo lỗi dễ hiểu.</summary>
    public static async Task<(DiskSplitProbe? Probe, string? Error)> ReadAsync(char letter, CancellationToken cancellationToken)
    {
        letter = char.ToUpperInvariant(letter);
        if (!DiskSplitPlanner.IsValidLetter(letter))
        {
            return (null, "Ký tự ổ không hợp lệ.");
        }

        using var doc = await PowerShellRunner.RunJsonAsync(Script.Replace("__L__", letter.ToString(), StringComparison.Ordinal), cancellationToken).ConfigureAwait(false);
        var root = doc.RootElement;
        if (!root.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
        {
            var error = root.TryGetProperty("error", out var e) ? e.GetString() : null;
            return (null, error is { Length: > 0 } && error.Contains("Access", StringComparison.OrdinalIgnoreCase)
                ? "Cần quyền quản trị để đọc phân vùng. Bấm “Nâng quyền quản trị…”."
                : "Không đọc được phân vùng: " + error);
        }

        var freeLetter = root.TryGetProperty("freeLetter", out var fl) && fl.GetString() is { Length: 1 } f ? f[0] : (char?)null;
        int? conversion = root.TryGetProperty("conversionStatus", out var cs) && cs.ValueKind == JsonValueKind.Number ? cs.GetInt32() : null;
        return (new DiskSplitProbe
        {
            DriveLetter = letter,
            PartitionSize = root.GetProperty("partitionSize").GetInt64(),
            SupportedMin = root.GetProperty("sizeMin").GetInt64(),
            FreeBytes = root.GetProperty("free").GetInt64(),
            FileSystem = root.GetProperty("fileSystem").GetString() ?? "",
            DiskNumber = root.GetProperty("diskNumber").GetInt32(),
            PartitionStyle = root.GetProperty("partitionStyle").GetString() ?? "",
            PartitionCount = root.GetProperty("partitionCount").GetInt32(),
            ConversionStatus = conversion,
            FreeLetter = freeLetter,
        }, null);
    }
}

/// <summary>
/// Chia ổ (mức R3): thu nhỏ ổ nguồn bằng Resize-Partition rồi tạo ổ NTFS mới từ vùng trống vừa tách.
/// Không xóa hay định dạng phân vùng có sẵn: chỉ định dạng đúng phân vùng vừa tạo. Không tự hoàn tác.
/// </summary>
public sealed class DiskSplitTask : ITaskModule
{
    private const string ExecuteScript = """
        $ErrorActionPreference = 'Stop'
        $r = [ordered]@{ ok = $false; stage = 'resize' }
        try {
          $before = @(Get-Partition -DiskNumber __DISK__ | ForEach-Object { $_.PartitionNumber })
          Resize-Partition -DriveLetter '__L__' -Size __TARGET__
          $r.stage = 'create'
          $p = New-Partition -DiskNumber __DISK__ -UseMaximumSize -DriveLetter '__N__'
          if ($before -contains $p.PartitionNumber) { throw 'Phân vùng trả về không phải phân vùng mới.' }
          $r.stage = 'format'
          Format-Volume -Partition $p -FileSystem NTFS -NewFileSystemLabel '__LABEL__' -Confirm:$false | Out-Null
          $r.ok = $true
          $r.stage = 'done'
        } catch { $r.error = [string]$_.Exception.Message }
        $r | ConvertTo-Json -Compress
        """;

    private const string VerifyScript = """
        $ErrorActionPreference = 'Stop'
        $r = [ordered]@{ ok = $true }
        try {
          $v = Get-Volume -DriveLetter '__N__'
          $r.newSize = [int64]$v.Size
          $r.newFs = [string]$v.FileSystem
          $r.newLabel = [string]$v.FileSystemLabel
          $r.sourceSize = [int64](Get-Partition -DriveLetter '__L__').Size
        } catch { $r.ok = $false; $r.error = [string]$_.Exception.Message }
        $r | ConvertTo-Json -Compress
        """;

    private DiskSplitPlan _plan;

    public DiskSplitTask(DiskSplitPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!plan.IsValid)
        {
            throw new ArgumentException("Kế hoạch chia ổ chưa hợp lệ.", nameof(plan));
        }

        _plan = plan;
        Descriptor = new TaskDescriptor
        {
            Id = "disk.split",
            DisplayName = $"Chia ổ {plan.Probe.DriveLetter}: — tạo ổ {plan.NewLetter}: {DiskSplitPlanner.Gb(plan.NewPartitionBytes)}",
            Version = 1,
            Risk = RiskLevel.Destructive,
            RequiresConfirmation = true,
            Privilege = PrivilegeLevel.Administrator,
            Kind = TaskKind.Configuration,
            Timeout = TimeSpan.FromMinutes(30),
            AllowForcedStop = false,
            Rollback = RollbackSupport.NotPossible,
            RollbackNote = "Không tự hoàn tác. Muốn gộp lại: sao lưu dữ liệu trên ổ mới, xóa ổ mới trong Disk Management rồi chọn Extend Volume cho ổ cũ.",
        };
    }

    public TaskDescriptor Descriptor { get; }

    public DiskSplitPlan Plan => _plan;

    public async Task<PreconditionResult> CheckPreconditionsAsync(TaskContext context, CancellationToken cancellationToken)
    {
        // Đọc lại ngay trước khi làm: máy có thể đã thay đổi kể từ lúc xem trước.
        var (probe, error) = await DiskSplitProbeReader.ReadAsync(_plan.Probe.DriveLetter, cancellationToken).ConfigureAwait(false);
        if (probe is null)
        {
            return PreconditionResult.NotMet(error ?? "Không đọc được phân vùng.");
        }

        var fresh = DiskSplitPlanner.Plan(probe, _plan.Request);
        if (!fresh.IsValid)
        {
            return PreconditionResult.NotMet("Điều kiện chia ổ đã thay đổi: " + string.Join(" ", fresh.Errors));
        }

        if (fresh.NewLetter != _plan.NewLetter || fresh.Probe.DiskNumber != _plan.Probe.DiskNumber)
        {
            return PreconditionResult.NotMet("Ổ đĩa đã thay đổi kể từ lúc xem trước. Hãy xem trước lại.");
        }

        _plan = fresh;
        return PreconditionResult.Met();
    }

    public async Task<ExecuteResult> ExecuteAsync(TaskContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var p = _plan;
        context.Log.Info($"Chia ổ {p.Probe.DriveLetter}: còn {DiskSplitPlanner.Gb(p.TargetSourceSize)}, tạo ổ {p.NewLetter}: khoảng {DiskSplitPlanner.Gb(p.NewPartitionBytes)} (NTFS).", Descriptor.Id);
        var script = ExecuteScript
            .Replace("__DISK__", p.Probe.DiskNumber.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("__L__", p.Probe.DriveLetter.ToString(), StringComparison.Ordinal)
            .Replace("__TARGET__", p.TargetSourceSize.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("__N__", p.NewLetter.ToString(), StringComparison.Ordinal)
            .Replace("__LABEL__", p.Request.Label, StringComparison.Ordinal);

        using var doc = await PowerShellRunner.RunJsonAsync(script, cancellationToken).ConfigureAwait(false);
        var root = doc.RootElement;
        if (root.GetProperty("ok").GetBoolean())
        {
            return ExecuteResult.Completed($"Đã chia ổ {p.Probe.DriveLetter}: và tạo ổ {p.NewLetter}:.");
        }

        var stage = root.TryGetProperty("stage", out var s) ? s.GetString() : null;
        var error = root.TryGetProperty("error", out var e) ? e.GetString() : null;
        return ExecuteResult.Failed(stage switch
        {
            "resize" => $"Windows không thu nhỏ được ổ {p.Probe.DriveLetter}: ({error}). Ổ đĩa giữ nguyên.",
            _ => $"Ổ {p.Probe.DriveLetter}: đã được thu nhỏ nhưng chưa tạo xong ổ mới ({error}). Mở Disk Management (diskmgmt.msc), chuột phải vùng Unallocated → New Simple Volume, hoặc Extend Volume cho ổ {p.Probe.DriveLetter}:.",
        });
    }

    public async Task<VerifyResult> VerifyAsync(TaskContext context, CancellationToken cancellationToken)
    {
        var p = _plan;
        var script = VerifyScript
            .Replace("__N__", p.NewLetter.ToString(), StringComparison.Ordinal)
            .Replace("__L__", p.Probe.DriveLetter.ToString(), StringComparison.Ordinal);
        using var doc = await PowerShellRunner.RunJsonAsync(script, cancellationToken).ConfigureAwait(false);
        var root = doc.RootElement;
        if (!root.GetProperty("ok").GetBoolean())
        {
            return VerifyResult.Failed($"Không tìm thấy ổ {p.NewLetter}: sau khi chia.");
        }

        var newSize = root.GetProperty("newSize").GetInt64();
        var fs = root.GetProperty("newFs").GetString();
        var sourceSize = root.GetProperty("sourceSize").GetInt64();
        var sizeOk = newSize >= p.NewPartitionBytes * 0.9;
        var fsOk = string.Equals(fs, "NTFS", StringComparison.OrdinalIgnoreCase);
        var sourceOk = sourceSize <= p.TargetSourceSize + (64L << 20);
        return sizeOk && fsOk && sourceOk
            ? VerifyResult.Passed($"Ổ {p.NewLetter}: {DiskSplitPlanner.Gb(newSize)} NTFS; ổ {p.Probe.DriveLetter}: còn {DiskSplitPlanner.Gb(sourceSize)}.")
            : VerifyResult.Attention($"Ổ {p.NewLetter}: {DiskSplitPlanner.Gb(newSize)} ({fs}); ổ {p.Probe.DriveLetter}: {DiskSplitPlanner.Gb(sourceSize)}. Kích thước khác dự kiến, hãy kiểm tra trong Disk Management.");
    }

    public Task<VerifyResult> RollbackAsync(TaskContext context, CancellationToken cancellationToken) =>
        Task.FromResult(VerifyResult.Inconclusive(Descriptor.RollbackNote!));
}
