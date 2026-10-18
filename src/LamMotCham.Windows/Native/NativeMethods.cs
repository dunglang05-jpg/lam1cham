using System.Runtime.InteropServices;

namespace LamMotCham.Windows.Native;

/// <summary>Các hàm Windows API dùng trực tiếp. Mọi lời gọi được bọc trong lớp có kiểm tra lỗi.</summary>
internal static class NativeMethods
{
    internal const int ComputerNamePhysicalDnsHostname = 5;
    internal const int ShcneAssocChanged = 0x08000000;
    internal const uint ShcnfIdList = 0x0000;
    internal const uint ErrorSuccess = 0;

    [StructLayout(LayoutKind.Sequential)]
    internal struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct FileTime
    {
        public uint Low;
        public uint High;

        public readonly ulong Value => ((ulong)High << 32) | Low;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SystemPowerStatus
    {
        public byte AcLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetComputerNameExW(int nameType, string buffer);

    [DllImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern IntPtr LocalFree(IntPtr handle);

    [DllImport("shell32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

    [DllImport("powrprof.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);

    [DllImport("powrprof.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern uint PowerSetActiveScheme(IntPtr userRootPowerKey, ref Guid schemeGuid);

    [DllImport("powrprof.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern uint PowerReadACValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subGroupGuid, ref Guid settingGuid, out uint value);

    [DllImport("powrprof.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern uint PowerReadDCValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subGroupGuid, ref Guid settingGuid, out uint value);

    [DllImport("powrprof.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern uint PowerWriteACValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subGroupGuid, ref Guid settingGuid, uint value);

    [DllImport("powrprof.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern uint PowerWriteDCValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subGroupGuid, ref Guid settingGuid, uint value);

    internal const uint SherbNoConfirmation = 0x1;
    internal const uint SherbNoProgressUi = 0x2;
    internal const uint SherbNoSound = 0x4;

    // Bản win-x64: căn lề mặc định 8 byte (struct dài 24 byte).
    [StructLayout(LayoutKind.Sequential)]
    internal struct ShQueryRbInfo
    {
        public uint Size;
        public long SizeBytes;
        public long NumItems;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int SHQueryRecycleBinW(string? rootPath, ref ShQueryRbInfo info);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int SHEmptyRecycleBinW(IntPtr hwnd, string? rootPath, uint flags);

    /// <summary>Báo cho Explorer làm mới sau khi đổi thiết lập hiển thị.</summary>
    internal static void NotifyShellSettingsChanged() =>
        SHChangeNotify(ShcneAssocChanged, ShcnfIdList, IntPtr.Zero, IntPtr.Zero);

    // ---- Tài khoản cục bộ (netapi32 / advapi32). Mật khẩu chỉ truyền qua con trỏ bộ nhớ, xóa ngay sau khi dùng. ----
    internal const uint NerrSuccess = 0;
    internal const uint NerrUserNotFound = 2221;
    internal const uint NerrUserExists = 2224;
    internal const uint NerrPasswordTooShort = 2245;
    internal const uint ErrorAccessDenied = 5;
    internal const int Logon32LogonNetwork = 3;
    internal const int Logon32ProviderDefault = 0;

    [StructLayout(LayoutKind.Sequential)]
    internal struct UserInfo0
    {
        public IntPtr Name;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct UserInfo10
    {
        public IntPtr Name;
        public IntPtr Comment;
        public IntPtr UserComment;
        public IntPtr FullName;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct UserInfo1003
    {
        public IntPtr Password;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct UserInfo1011
    {
        public IntPtr FullName;
    }

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern uint NetUserGetInfo(string? serverName, string userName, uint level, out IntPtr buffer);

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern uint NetUserSetInfo(string? serverName, string userName, uint level, ref UserInfo0 buffer, out uint parmError);

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern uint NetUserSetInfo(string? serverName, string userName, uint level, ref UserInfo1003 buffer, out uint parmError);

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern uint NetUserSetInfo(string? serverName, string userName, uint level, ref UserInfo1011 buffer, out uint parmError);

    [DllImport("netapi32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern uint NetApiBufferFree(IntPtr buffer);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool LogonUserW(string userName, string domain, IntPtr password, int logonType, int logonProvider, out IntPtr token);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(IntPtr handle);
}
