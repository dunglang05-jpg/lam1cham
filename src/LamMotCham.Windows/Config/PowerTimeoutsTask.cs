using System.Globalization;
using System.Runtime.InteropServices;
using LamMotCham.Core.Tasks;
using LamMotCham.Windows.Infrastructure;
using LamMotCham.Windows.Native;

namespace LamMotCham.Windows.Config;

/// <summary>Thời gian tắt màn hình và ngủ (giây). Giá trị 0 nghĩa là "không bao giờ".</summary>
public sealed record PowerTimeouts(uint ScreenAc, uint ScreenDc, uint SleepAc, uint SleepDc)
{
    /// <summary>Giá trị mặc định tạm thời cho bản thử nghiệm (Q-12): 15/5 phút màn hình, 30/15 phút ngủ.</summary>
    public static PowerTimeouts Default { get; } = new(15 * 60, 5 * 60, 30 * 60, 15 * 60);

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture,
            $"cắm sạc {Min(ScreenAc)}/{Min(SleepAc)}, pin {Min(ScreenDc)}/{Min(SleepDc)}");

    private static string Min(uint seconds) => seconds == 0 ? "không bao giờ" : (seconds / 60).ToString(CultureInfo.InvariantCulture) + " phút";
}

/// <summary>Đặt thời gian tắt màn hình và ngủ trên gói nguồn đang dùng bằng Power API.</summary>
public sealed class PowerTimeoutsTask(RollbackStore store, PowerTimeouts? target = null) : ITaskModule, IDescribesCurrentState
{
    private static readonly Guid SubVideo = new("7516b95f-f776-4464-8c53-06167f40cc99");
    private static readonly Guid VideoIdle = new("3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e");
    private static readonly Guid SubSleep = new("238c9fa8-0aad-41ed-83f4-97be242c8f20");
    private static readonly Guid StandbyIdle = new("29f6c1db-86da-48c5-9fdb-f2b67b1f44da");

    private readonly PowerTimeouts _target = target ?? PowerTimeouts.Default;

    public TaskDescriptor Descriptor { get; } = new()
    {
        Id = "win.power",
        DisplayName = "Thời gian tắt màn hình và ngủ",
        Version = 1,
        Risk = RiskLevel.Reversible,
        Privilege = PrivilegeLevel.Administrator,
        Kind = TaskKind.Configuration,
        Timeout = TimeSpan.FromSeconds(30),
        Rollback = RollbackSupport.Supported,
    };

    public Task<PreconditionResult> CheckPreconditionsAsync(TaskContext context, CancellationToken cancellationToken) =>
        Task.FromResult(ReadCurrent() is { } current && current == _target
            ? PreconditionResult.AlreadyInDesiredState("Đã đúng sẵn.")
            : PreconditionResult.Met());

    public Task<ExecuteResult> ExecuteAsync(TaskContext context, CancellationToken cancellationToken)
    {
        var current = ReadCurrent();
        if (current is null)
        {
            return Task.FromResult(ExecuteResult.Failed("Không đọc được gói nguồn đang dùng."));
        }

        store.SaveIfAbsent(Descriptor.Id, new Dictionary<string, string?>
        {
            ["sa"] = Str(current.ScreenAc), ["sd"] = Str(current.ScreenDc), ["la"] = Str(current.SleepAc), ["ld"] = Str(current.SleepDc),
        });

        var error = Write(_target);
        return Task.FromResult(error == 0
            ? ExecuteResult.Completed()
            : ExecuteResult.Failed(error == 5 ? "Cần quyền quản trị để đổi thiết lập nguồn." : "Power API báo lỗi khi ghi.", errorCode: error.ToString(CultureInfo.InvariantCulture)));
    }

    public Task<VerifyResult> VerifyAsync(TaskContext context, CancellationToken cancellationToken)
    {
        var now = ReadCurrent();
        return Task.FromResult(now == _target
            ? VerifyResult.Passed($"Đọc lại khớp: {now}.")
            : VerifyResult.Failed($"Giá trị đọc lại chưa đúng: {now?.ToString() ?? "không đọc được"}."));
    }

    public Task<VerifyResult> RollbackAsync(TaskContext context, CancellationToken cancellationToken)
    {
        var s = store.Load(Descriptor.Id);
        if (s is null)
        {
            return Task.FromResult(VerifyResult.Inconclusive("Không có giá trị cũ đã lưu."));
        }

        var old = new PowerTimeouts(U(s, "sa"), U(s, "sd"), U(s, "la"), U(s, "ld"));
        var error = Write(old);
        if (error == 0)
        {
            store.Clear(Descriptor.Id);
        }

        return Task.FromResult(error == 0 ? VerifyResult.Passed("Đã khôi phục.") : VerifyResult.Failed("Không khôi phục được."));
    }

    public Task<string> DescribeCurrentStateAsync(CancellationToken cancellationToken) =>
        Task.FromResult("Hiện tại: " + (ReadCurrent()?.ToString() ?? "không đọc được"));

    private static PowerTimeouts? ReadCurrent()
    {
        if (!TryGetActiveScheme(out var scheme))
        {
            return null;
        }

        return Read(scheme, SubVideo, VideoIdle, ac: true) is { } sa &&
               Read(scheme, SubVideo, VideoIdle, ac: false) is { } sd &&
               Read(scheme, SubSleep, StandbyIdle, ac: true) is { } la &&
               Read(scheme, SubSleep, StandbyIdle, ac: false) is { } ld
            ? new PowerTimeouts(sa, sd, la, ld)
            : null;
    }

    private static uint Write(PowerTimeouts t)
    {
        if (!TryGetActiveScheme(out var scheme))
        {
            return 1;
        }

        var subVideo = SubVideo;
        var videoIdle = VideoIdle;
        var subSleep = SubSleep;
        var standby = StandbyIdle;
        var err = NativeMethods.PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref subVideo, ref videoIdle, t.ScreenAc);
        err = err != 0 ? err : NativeMethods.PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref subVideo, ref videoIdle, t.ScreenDc);
        err = err != 0 ? err : NativeMethods.PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref subSleep, ref standby, t.SleepAc);
        err = err != 0 ? err : NativeMethods.PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref subSleep, ref standby, t.SleepDc);
        return err != 0 ? err : NativeMethods.PowerSetActiveScheme(IntPtr.Zero, ref scheme);
    }

    private static uint? Read(Guid scheme, Guid sub, Guid setting, bool ac)
    {
        var err = ac
            ? NativeMethods.PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, out var value)
            : NativeMethods.PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, out value);
        return err == 0 ? value : null;
    }

    private static bool TryGetActiveScheme(out Guid scheme)
    {
        scheme = Guid.Empty;
        if (NativeMethods.PowerGetActiveScheme(IntPtr.Zero, out var ptr) != 0 || ptr == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            scheme = Marshal.PtrToStructure<Guid>(ptr);
            return true;
        }
        finally
        {
            NativeMethods.LocalFree(ptr);
        }
    }

    private static string Str(uint v) => v.ToString(CultureInfo.InvariantCulture);

    private static uint U(IReadOnlyDictionary<string, string?> s, string k) =>
        uint.TryParse(s.GetValueOrDefault(k), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
}
