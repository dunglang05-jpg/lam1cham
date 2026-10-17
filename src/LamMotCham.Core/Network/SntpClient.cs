using System.Buffers.Binary;
using System.Net.Sockets;

namespace LamMotCham.Core.Network;

/// <summary>
/// Client SNTP tối giản, chỉ dùng để <b>đo</b> độ lệch đồng hồ khi xác minh đồng bộ giờ.
/// Không dùng để đặt giờ hệ thống.
/// </summary>
public static class SntpClient
{
    private const int NtpPacketSize = 48;
    private static readonly DateTime NtpEpoch = new(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Trả về độ lệch (giờ máy chủ − giờ máy) hoặc null nếu không truy vấn được.</summary>
    public static async Task<TimeSpan?> TryGetClockOffsetAsync(string server, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(server);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        try
        {
            using var udp = new UdpClient();
            await udp.Client.ConnectAsync(server, 123, cts.Token).ConfigureAwait(false);

            var request = new byte[NtpPacketSize];
            request[0] = 0x1B; // LI = 0, VN = 3, Mode = 3 (client)

            var t1 = DateTime.UtcNow;
            await udp.Client.SendAsync(request, SocketFlags.None, cts.Token).ConfigureAwait(false);

            var buffer = new byte[NtpPacketSize];
            var received = await udp.Client.ReceiveAsync(buffer, SocketFlags.None, cts.Token).ConfigureAwait(false);
            var t4 = DateTime.UtcNow;

            return received < NtpPacketSize ? null : ComputeOffset(buffer, t1, t4);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (SocketException)
        {
            return null;
        }
    }

    /// <summary>Tính độ lệch theo RFC 4330: ((T2 − T1) + (T3 − T4)) / 2.</summary>
    public static TimeSpan? ComputeOffset(ReadOnlySpan<byte> response, DateTime t1, DateTime t4)
    {
        if (response.Length < NtpPacketSize)
        {
            return null;
        }

        var mode = response[0] & 0x7;
        var stratum = response[1];
        if (mode != 4 || stratum is 0 or > 15)
        {
            return null;
        }

        var t2 = ReadTimestamp(response[32..40]);
        var t3 = ReadTimestamp(response[40..48]);
        return ((t2 - t1) + (t3 - t4)) / 2;
    }

    internal static DateTime ReadTimestamp(ReadOnlySpan<byte> bytes)
    {
        var seconds = BinaryPrimitives.ReadUInt32BigEndian(bytes[..4]);
        var fraction = BinaryPrimitives.ReadUInt32BigEndian(bytes[4..8]);
        var ticks = (long)seconds * TimeSpan.TicksPerSecond + (long)(fraction * (double)TimeSpan.TicksPerSecond / 4294967296.0);
        return NtpEpoch.AddTicks(ticks);
    }
}
