using System.Text.Json;
using LamMotCham.Core.Logging;
using LamMotCham.Core.Tasks;
using LamMotCham.Windows.Disk;
using LamMotCham.Windows.Infrastructure;

namespace LamMotCham.Windows.Config;

/// <summary>Trạng thái BitLocker của một ổ (Win32_EncryptableVolume). Không đọc khóa khôi phục.</summary>
/// <param name="Letter">Ký tự ổ.</param>
/// <param name="IsOsVolume">Ổ cài Windows (VolumeType 0).</param>
/// <param name="Conversion">0 chưa mã hóa, 1 đã mã hóa, 2 đang mã hóa, 3 đang giải mã, 4/5 tạm dừng.</param>
/// <param name="EncryptedPercent">Phần trăm còn mã hóa.</param>
/// <param name="Protection">0 tắt bảo vệ, 1 bật, 2 không rõ.</param>
public sealed record BitLockerVolume(char Letter, bool IsOsVolume, int Conversion, double EncryptedPercent, int Protection)
{
    /// <summary>Tên hiển thị, ví dụ "C: (Windows)".</summary>
    public string DriveText => Letter + ":" + (IsOsVolume ? " (Windows)" : "");

    /// <summary>Đã mã hóa toàn bộ hoặc một phần, và không đang giải mã.</summary>
    public bool CanTurnOff => Conversion is 1 or 2 or 4;

    public string StateText => Conversion switch
    {
        0 => "Không mã hóa",
        1 => Protection == 1 ? "Đang bật BitLocker" : "Đã mã hóa (bảo vệ tạm dừng)",
        2 => $"Đang mã hóa ({EncryptedPercent:0}%)",
        3 => $"Đang giải mã — còn {EncryptedPercent:0}%",
        4 => $"Mã hóa tạm dừng ({EncryptedPercent:0}%)",
        5 => $"Giải mã tạm dừng — còn {EncryptedPercent:0}%",
        _ => "Không rõ",
    };
}

/// <summary>Đọc trạng thái BitLocker mọi ổ có ký tự. Cần quyền quản trị.</summary>
public static class BitLockerReader
{
    private const string Script = """
        $ErrorActionPreference = 'Stop'
        $r = [ordered]@{ ok = $true; volumes = @() }
        try {
          $r.volumes = @(Get-CimInstance -Namespace 'root\cimv2\Security\MicrosoftVolumeEncryption' -ClassName Win32_EncryptableVolume | Where-Object { $_.DriveLetter } | ForEach-Object {
            $cs = Invoke-CimMethod -InputObject $_ -MethodName GetConversionStatus
            [ordered]@{ letter = [string]$_.DriveLetter; type = [int]$_.VolumeType; conversion = [int]$cs.ConversionStatus; percent = [double]$cs.EncryptionPercentage; protection = [int]$_.ProtectionStatus } })
        } catch { $r.ok = $false; $r.error = [string]$_.Exception.Message }
        $r | ConvertTo-Json -Depth 4 -Compress
        """;

    public static async Task<(IReadOnlyList<BitLockerVolume> Volumes, string? Error)> ReadAsync(CancellationToken cancellationToken)
    {
        using var doc = await PowerShellRunner.RunJsonAsync(Script, cancellationToken).ConfigureAwait(false);
        var root = doc.RootElement;
        if (!root.GetProperty("ok").GetBoolean())
        {
            var error = root.TryGetProperty("error", out var e) ? e.GetString() : null;
            return ([], error is not null && (error.Contains("Access", StringComparison.OrdinalIgnoreCase) || error.Contains("denied", StringComparison.OrdinalIgnoreCase))
                ? "Cần quyền quản trị để đọc BitLocker. Mở lại phần mềm và chọn Yes khi Windows hỏi quyền."
                : "Không đọc được BitLocker: " + error);
        }

        var list = new List<BitLockerVolume>();
        if (root.TryGetProperty("volumes", out var vols) && vols.ValueKind == JsonValueKind.Array)
        {
            foreach (var v in vols.EnumerateArray())
            {
                var letter = (v.GetProperty("letter").GetString() ?? "").TrimEnd(':');
                if (letter.Length == 1 && DiskSplitPlanner.IsValidLetter(char.ToUpperInvariant(letter[0])))
                {
                    list.Add(new BitLockerVolume(
                        char.ToUpperInvariant(letter[0]),
                        v.GetProperty("type").GetInt32() == 0,
                        v.GetProperty("conversion").GetInt32(),
                        v.GetProperty("percent").GetDouble(),
                        v.GetProperty("protection").GetInt32()));
                }
            }
        }

        return ([.. list.OrderBy(v => v.Letter)], null);
    }
}

/// <summary>
/// Tắt BitLocker (giải mã) mọi ổ đang mã hóa — một thiết lập trong mô-đun Tối ưu Windows, nằm trong gói Thiết lập đầy đủ.
/// Chỉ chạy sau hộp xác nhận có ô "khách đồng ý tắt mã hóa". Ổ dữ liệu trước; ổ Windows chỉ khi ổ dữ liệu đã giải mã xong.
/// Giải mã chạy nền trong Windows; tác vụ xác minh Windows đã bắt đầu giải mã. Không đọc, hiển thị hay ghi log khóa khôi phục.
/// </summary>
public sealed class BitLockerOffTask : ITaskModule, IDescribesCurrentState
{
    public const string TaskId = "win.bitlocker.off";

    private const string DecryptScript = """
        $ErrorActionPreference = 'Stop'
        $r = [ordered]@{ ok = $false; code = -1 }
        try {
          $ev = Get-CimInstance -Namespace 'root\cimv2\Security\MicrosoftVolumeEncryption' -ClassName Win32_EncryptableVolume -Filter "DriveLetter='__L__:'"
          if (-not $ev) { throw 'Không tìm thấy ổ.' }
          if ([int]$ev.VolumeType -eq 0) {
            # Ổ Windows: Windows từ chối giải mã khi còn lưu khóa tự mở khóa của ổ dữ liệu.
            # Chỉ xóa các khóa này khi mọi ổ dữ liệu đã giải mã xong; nếu không, ổ dữ liệu có thể bị khóa ở lần khởi động sau.
            $ak = Invoke-CimMethod -InputObject $ev -MethodName IsAutoUnlockKeyStored
            if ($ak.IsAutoUnlockKeyStored) {
              $busy = @(Get-CimInstance -Namespace 'root\cimv2\Security\MicrosoftVolumeEncryption' -ClassName Win32_EncryptableVolume | Where-Object { [int]$_.VolumeType -ne 0 } | Where-Object { [int](Invoke-CimMethod -InputObject $_ -MethodName GetConversionStatus).ConversionStatus -ne 0 })
              if ($busy.Count -gt 0) { $r.code = -2; $r | ConvertTo-Json -Compress; return }
              [void](Invoke-CimMethod -InputObject $ev -MethodName ClearAllAutoUnlockKeys)
            }
          }
          $res = Invoke-CimMethod -InputObject $ev -MethodName Decrypt
          $r.code = [int64]$res.ReturnValue
          $r.ok = ($r.code -eq 0)
        } catch { $r.error = [string]$_.Exception.Message }
        $r | ConvertTo-Json -Compress
        """;

    private List<BitLockerVolume> _targets = [];

    public TaskDescriptor Descriptor { get; } = new()
    {
        Id = TaskId,
        DisplayName = "Tắt BitLocker (giải mã ổ đĩa)",
        Version = 2,
        Risk = RiskLevel.Reversible,
        RequiresConfirmation = true,
        Privilege = PrivilegeLevel.Administrator,
        Kind = TaskKind.Configuration,
        Timeout = TimeSpan.FromMinutes(3),
        Rollback = RollbackSupport.NotPossible,
        RollbackNote = "Không tự bật lại. Bật lại trong Settings › Privacy & security › Device encryption, hoặc Control Panel › BitLocker Drive Encryption.",
    };

    public async Task<PreconditionResult> CheckPreconditionsAsync(TaskContext context, CancellationToken cancellationToken)
    {
        var (volumes, error) = await BitLockerReader.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (error is not null)
        {
            return PreconditionResult.NotMet(error);
        }

        // Ổ dữ liệu trước, ổ Windows sau cùng.
        _targets = [.. volumes.Where(v => v.CanTurnOff).OrderBy(v => v.IsOsVolume).ThenBy(v => v.Letter)];
        if (_targets.Count > 0)
        {
            return PreconditionResult.Met();
        }

        var decrypting = volumes.Where(v => v.Conversion is 3 or 5).ToList();
        return PreconditionResult.AlreadyInDesiredState(decrypting.Count > 0
            ? "Đang giải mã nền: " + string.Join(", ", decrypting.Select(v => $"{v.Letter}: {v.StateText}")) + "."
            : "Không ổ nào đang bật BitLocker.");
    }

    public async Task<ExecuteResult> ExecuteAsync(TaskContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var started = new List<char>();
        var notes = new List<string>();
        foreach (var v in _targets)
        {
            context.Log.Info($"Tắt BitLocker ổ {v.Letter}: (bắt đầu giải mã).", Descriptor.Id);
            using var doc = await PowerShellRunner.RunJsonAsync(
                DecryptScript.Replace("__L__", v.Letter.ToString(), StringComparison.Ordinal), cancellationToken).ConfigureAwait(false);
            var root = doc.RootElement;
            if (root.GetProperty("ok").GetBoolean())
            {
                started.Add(v.Letter);
                continue;
            }

            var code = root.TryGetProperty("code", out var c) ? c.GetInt64() : -1;
            var error = root.TryGetProperty("error", out var e) ? e.GetString() : null;
            notes.Add(code switch
            {
                -2 => $"ổ {v.Letter}: chờ ổ dữ liệu giải mã xong rồi chạy lại",
                0x80310029 => $"ổ {v.Letter}: còn khóa tự mở khóa của ổ dữ liệu",
                _ => $"ổ {v.Letter}: " + (error ?? $"mã 0x{code:X8}"),
            });
        }

        return started.Count == 0 && notes.Count > 0 && !notes.TrueForAll(n => n.Contains("chờ ổ dữ liệu", StringComparison.Ordinal))
            ? ExecuteResult.Failed("Windows không tắt được BitLocker: " + string.Join("; ", notes) + ".")
            : ExecuteResult.Completed(started.Count > 0
                ? "Windows đã bắt đầu giải mã ổ " + string.Join(", ", started.Select(l => l + ":")) + "."
                : "Chưa giải mã ổ nào.");
    }

    public async Task<VerifyResult> VerifyAsync(TaskContext context, CancellationToken cancellationToken)
    {
        var (volumes, error) = await BitLockerReader.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (error is not null)
        {
            return VerifyResult.Inconclusive(error);
        }

        var states = _targets.Select(t => volumes.FirstOrDefault(v => v.Letter == t.Letter) ?? t).ToList();
        var done = states.Where(v => v.Conversion is 0 or 3).ToList();
        var left = states.Except(done).ToList();
        var summary = string.Join("; ", states.Select(v => $"{v.Letter}: {v.StateText}"));
        if (left.Count == 0)
        {
            return VerifyResult.Passed(summary + ". Giải mã chạy nền; máy dùng bình thường, không tắt nguồn đột ngột cho đến khi xong.");
        }

        return left.TrueForAll(v => v.IsOsVolume) && done.Count > 0
            ? VerifyResult.Attention(summary + ". Ổ Windows tắt được khi ổ dữ liệu giải mã xong — chạy lại mục này sau.")
            : VerifyResult.Failed(summary + ". Chưa chuyển sang giải mã.");
    }

    public Task<VerifyResult> RollbackAsync(TaskContext context, CancellationToken cancellationToken) =>
        Task.FromResult(VerifyResult.Inconclusive(Descriptor.RollbackNote!));

    public async Task<string> DescribeCurrentStateAsync(CancellationToken cancellationToken)
    {
        var (volumes, error) = await BitLockerReader.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (error is not null)
        {
            return error.StartsWith("Cần quyền", StringComparison.Ordinal) ? "Cần quyền quản trị để đọc" : "Chưa đọc được";
        }

        var encrypted = volumes.Where(v => v.Conversion != 0).ToList();
        return encrypted.Count == 0
            ? "Không ổ nào mã hóa"
            : string.Join("; ", encrypted.Select(v => $"{v.Letter}: {v.StateText}"));
    }
}
