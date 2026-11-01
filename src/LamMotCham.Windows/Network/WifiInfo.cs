using System.Runtime.InteropServices;
using System.Text;

namespace LamMotCham.Windows.Network;

/// <summary>
/// Đọc tên (SSID) các Wi-Fi máy đang kết nối bằng Native Wifi API của Windows (wlanapi.dll), không chạy netsh.
/// Từ Windows 11 24H2, Windows chỉ trả tên Wi-Fi khi bật Vị trí (Location) cho ứng dụng desktop;
/// không đọc được thì trả danh sách rỗng và phần mềm hiện màn hình đăng nhập như thường.
/// </summary>
public static class WifiInfo
{
    private const uint ClientVersion = 2;
    private const int CurrentConnectionOpcode = 7;
    private const int StateConnected = 1;

    /// <summary>Lý do lần đọc gần nhất không ra tên Wi-Fi (để ghi log), null nếu đọc được.</summary>
    public static string? LastProblem { get; private set; }

    public static IReadOnlyList<string> ConnectedSsids()
    {
        var result = new List<string>();
        LastProblem = null;
        IntPtr client = IntPtr.Zero;
        IntPtr list = IntPtr.Zero;
        try
        {
            var error = NativeMethods.WlanOpenHandle(ClientVersion, IntPtr.Zero, out _, out client);
            if (error != 0)
            {
                LastProblem = error == 1062 ? "Máy không có Wi-Fi hoặc dịch vụ WLAN đang tắt." : $"Không mở được Wi-Fi (mã {error}).";
                return result;
            }

            error = NativeMethods.WlanEnumInterfaces(client, IntPtr.Zero, out list);
            if (error != 0)
            {
                LastProblem = $"Không đọc được card Wi-Fi (mã {error}).";
                return result;
            }

            var count = Marshal.ReadInt32(list);
            var itemSize = Marshal.SizeOf<WlanInterfaceInfo>();
            for (var i = 0; i < count; i++)
            {
                var info = Marshal.PtrToStructure<WlanInterfaceInfo>(list + 8 + (i * itemSize));
                if (info.State != StateConnected)
                {
                    continue;
                }

                var guid = info.InterfaceGuid;
                error = NativeMethods.WlanQueryInterface(client, ref guid, CurrentConnectionOpcode, IntPtr.Zero, out _, out var data, out _);
                if (error != 0)
                {
                    LastProblem = error == 5
                        ? "Windows chặn đọc tên Wi-Fi: bật Vị trí (Settings › Privacy & security › Location) cho ứng dụng desktop."
                        : $"Không đọc được Wi-Fi đang kết nối (mã {error}).";
                    continue;
                }

                try
                {
                    var attributes = Marshal.PtrToStructure<WlanConnectionAttributes>(data);
                    var ssid = attributes.AssociationAttributes.Ssid;
                    var length = (int)Math.Min(ssid.Length, 32u);
                    if (length > 0)
                    {
                        result.Add(Encoding.UTF8.GetString(ssid.Bytes, 0, length));
                    }
                    else
                    {
                        LastProblem = "Windows không trả tên Wi-Fi (thường do tắt Vị trí trên Windows 11 24H2).";
                    }
                }
                finally
                {
                    NativeMethods.WlanFreeMemory(data);
                }
            }

            if (result.Count == 0)
            {
                LastProblem ??= "Máy chưa kết nối Wi-Fi.";
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            LastProblem = "Máy không có thành phần Wi-Fi của Windows.";
        }
        finally
        {
            if (list != IntPtr.Zero)
            {
                NativeMethods.WlanFreeMemory(list);
            }

            if (client != IntPtr.Zero)
            {
                _ = NativeMethods.WlanCloseHandle(client, IntPtr.Zero);
            }
        }

        return result;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WlanInterfaceInfo
    {
        public Guid InterfaceGuid;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Description;

        public int State;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Dot11Ssid
    {
        public uint Length;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] Bytes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WlanAssociationAttributes
    {
        public Dot11Ssid Ssid;
        public int BssType;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
        public byte[] Bssid;

        public int PhyType;
        public uint PhyIndex;
        public uint SignalQuality;
        public uint RxRate;
        public uint TxRate;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WlanConnectionAttributes
    {
        public int State;
        public int Mode;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string ProfileName;

        public WlanAssociationAttributes AssociationAttributes;
    }

    private static class NativeMethods
    {
        [DllImport("wlanapi.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern uint WlanOpenHandle(uint clientVersion, IntPtr reserved, out uint negotiatedVersion, out IntPtr clientHandle);

        [DllImport("wlanapi.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern uint WlanCloseHandle(IntPtr clientHandle, IntPtr reserved);

        [DllImport("wlanapi.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern uint WlanEnumInterfaces(IntPtr clientHandle, IntPtr reserved, out IntPtr interfaceList);

        [DllImport("wlanapi.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern uint WlanQueryInterface(IntPtr clientHandle, ref Guid interfaceGuid, int opCode, IntPtr reserved, out uint dataSize, out IntPtr data, out int opcodeValueType);

        [DllImport("wlanapi.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern void WlanFreeMemory(IntPtr memory);
    }
}
