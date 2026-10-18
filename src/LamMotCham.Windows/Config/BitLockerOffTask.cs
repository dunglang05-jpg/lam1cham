using System.Globalization;
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
                ? "Cần quyền quản trị để đọc BitLocker. Bấm “Nâng quyền quản trị…”."
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
/// Tắt BitLocker (giải mã) một ổ. Chỉ chạy sau khi kỹ thuật viên xác nhận riêng; không bao giờ nằm trong gói nhanh.
/// Giải mã chạy nền trong Windows, có thể mất nhiều phút; tác vụ xác minh rằng Windows đã bắt đầu giải mã.
/// Không đọc, hiển thị hay ghi log khóa khôi phục.
/// </summary>
public sealed class BitLockerOffTask : ITaskModule
{
    private const string StatusScript = """
        $ErrorActionPreference = 'Stop'
        $r = [ordered]@{ ok = $true }
        try {
          $ev = Get-CimInstance -Namespace 'root\cimv2\Security\MicrosoftVolumeEncryption' -ClassName Win32_EncryptableVolume -Filter "DriveLetter='__L__:'"
          if (-not $ev) { throw 'Không tìm thấy ổ.' }
          $cs = Invoke-CimMethod -InputObject $ev -MethodName GetConversionStatus
          $r.conversion = [int]$cs.ConversionStatus
          $r.percent = [double]$cs.EncryptionPercentage
        } catch { $r.ok = $false; $r.error = [string]$_.Exception.Message }
        $r | ConvertTo-Json -Compress
        """;

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

    public BitLockerOffTask(char letter, bool isOsVolume)
    {
        letter = char.ToUpperInvariant(letter);
        if (!DiskSplitPlanner.IsValidLetter(letter))
        {
            throw new ArgumentException("Ký tự ổ không hợp lệ.", nameof(letter));
        }

        Letter = letter;
        IsOsVolume = isOsVolume;
        Descriptor = new TaskDescriptor
        {
            Id = "win.bitlocker.off." + char.ToLowerInvariant(letter),
            DisplayName = $"Tắt BitLocker ổ {letter}:",
            Version = 1,
            Risk = RiskLevel.Reversible,
            RequiresConfirmation = true,
            Privilege = PrivilegeLevel.Administrator,
            Kind = TaskKind.Configuration,
            Timeout = TimeSpan.FromMinutes(2),
            Rollback = RollbackSupport.NotPossible,
            RollbackNote = "Không tự bật lại. Bật lại trong Settings › Privacy & security › Device encryption, hoặc Control Panel › BitLocker Drive Encryption.",
        };
    }

    public char Letter { get; }

    public bool IsOsVolume { get; }

    public TaskDescriptor Descriptor { get; }

    public async Task<PreconditionResult> CheckPreconditionsAsync(TaskContext context, CancellationToken cancellationToken)
    {
        var (conversion, percent, error) = await ReadStatusAsync(cancellationToken).ConfigureAwait(false);
        return conversion switch
        {
            null => PreconditionResult.NotMet(error ?? "Không đọc được trạng thái BitLocker."),
            0 => PreconditionResult.AlreadyInDesiredState($"Ổ {Letter}: không mã hóa."),
            3 => PreconditionResult.AlreadyInDesiredState($"Ổ {Letter}: đang giải mã (còn {percent:0}%)."),
            5 => PreconditionResult.NotMet($"Ổ {Letter}: giải mã đang tạm dừng. Tiếp tục trong Control Panel › BitLocker."),
            _ => PreconditionResult.Met(),
        };
    }

    public async Task<ExecuteResult> ExecuteAsync(TaskContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Log.Info($"Tắt BitLocker ổ {Letter}: (bắt đầu giải mã).", Descriptor.Id);
        using var doc = await PowerShellRunner.RunJsonAsync(Script(DecryptScript), cancellationToken).ConfigureAwait(false);
        var root = doc.RootElement;
        if (root.GetProperty("ok").GetBoolean())
        {
            return ExecuteResult.Completed($"Windows đã bắt đầu giải mã ổ {Letter}:.");
        }

        var code = root.TryGetProperty("code", out var c) ? c.GetInt64() : -1;
        var error = root.TryGetProperty("error", out var e) ? e.GetString() : null;
        return ExecuteResult.Failed(
            (code switch
            {
                -2 => $"Ổ dữ liệu vẫn đang mã hóa hoặc đang giải mã. Chờ các ổ dữ liệu giải mã xong rồi bấm “Tắt BitLocker…” lại để tắt ổ {Letter}:.",
                0x80310029 => "Ổ Windows còn khóa tự mở khóa của ổ dữ liệu. Tắt BitLocker ổ dữ liệu, chờ giải mã xong rồi thử lại.",
                0x80310001 => "Ổ chưa được bật BitLocker.",
                _ => $"Windows không tắt được BitLocker ổ {Letter}:" + (error is null ? $" (mã 0x{code:X8})." : $" ({error})."),
            }),
            errorCode: code.ToString("X", CultureInfo.InvariantCulture));
    }

    public async Task<VerifyResult> VerifyAsync(TaskContext context, CancellationToken cancellationToken)
    {
        var (conversion, percent, error) = await ReadStatusAsync(cancellationToken).ConfigureAwait(false);
        return conversion switch
        {
            0 => VerifyResult.Passed($"Ổ {Letter}: đã giải mã xong."),
            3 => VerifyResult.Passed($"Ổ {Letter}: đang giải mã nền (còn {percent:0}%). Máy dùng bình thường; không tắt nguồn đột ngột cho đến khi xong."),
            5 => VerifyResult.Attention($"Ổ {Letter}: giải mã đang tạm dừng (còn {percent:0}%)."),
            null => VerifyResult.Inconclusive(error ?? "Không đọc lại được trạng thái BitLocker."),
            _ => VerifyResult.Failed($"Ổ {Letter}: chưa chuyển sang giải mã (trạng thái {conversion})."),
        };
    }

    public Task<VerifyResult> RollbackAsync(TaskContext context, CancellationToken cancellationToken) =>
        Task.FromResult(VerifyResult.Inconclusive(Descriptor.RollbackNote!));

    private string Script(string template) => template.Replace("__L__", Letter.ToString(), StringComparison.Ordinal);

    private async Task<(int? Conversion, double Percent, string? Error)> ReadStatusAsync(CancellationToken cancellationToken)
    {
        using var doc = await PowerShellRunner.RunJsonAsync(Script(StatusScript), cancellationToken).ConfigureAwait(false);
        var root = doc.RootElement;
        return root.GetProperty("ok").GetBoolean()
            ? (root.GetProperty("conversion").GetInt32(), root.GetProperty("percent").GetDouble(), null)
            : (null, 0, root.TryGetProperty("error", out var e) ? e.GetString() : null);
    }
}
