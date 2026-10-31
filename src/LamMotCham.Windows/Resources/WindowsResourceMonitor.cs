using System.Runtime.InteropServices;
using LamMotCham.Core.Resources;
using LamMotCham.Windows.Native;

namespace LamMotCham.Windows.Resources;

/// <summary>Đọc RAM khả dụng và CPU toàn hệ thống bằng Windows API (không tốn tài nguyên đáng kể).</summary>
public sealed class WindowsResourceMonitor : IResourceMonitor
{
    private readonly Lock _gate = new();
    private ulong _lastIdle;
    private ulong _lastTotal;

    public ResourceSnapshot Read()
    {
        var memory = ReadMemory();
        return new ResourceSnapshot(
            memory is { } m ? m.AvailableBytes * 100.0 / m.TotalBytes : null,
            ReadCpuPercent(),
            memory?.AvailableBytes);
    }

    public static (ulong TotalBytes, ulong AvailableBytes)? ReadMemory()
    {
        var status = new NativeMethods.MemoryStatusEx { Length = (uint)Marshal.SizeOf<NativeMethods.MemoryStatusEx>() };
        return NativeMethods.GlobalMemoryStatusEx(ref status) && status.TotalPhys > 0
            ? (status.TotalPhys, status.AvailPhys)
            : null;
    }

    /// <summary>CPU trung bình giữa hai lần đọc liên tiếp. Lần đọc đầu tiên trả về null.</summary>
    private double? ReadCpuPercent()
    {
        if (!NativeMethods.GetSystemTimes(out var idle, out var kernel, out var user))
        {
            return null;
        }

        // Kernel time bao gồm cả idle time.
        var idleNow = idle.Value;
        var totalNow = kernel.Value + user.Value;

        lock (_gate)
        {
            var hadPrevious = _lastTotal != 0;
            var idleDelta = idleNow - _lastIdle;
            var totalDelta = totalNow - _lastTotal;
            _lastIdle = idleNow;
            _lastTotal = totalNow;

            if (!hadPrevious || totalDelta == 0)
            {
                return null;
            }

            return Math.Clamp(100.0 * (totalDelta - idleDelta) / totalDelta, 0, 100);
        }
    }
}
