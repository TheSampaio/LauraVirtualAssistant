using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Laura.Platform.Windows.System;

/// <summary>Discovers physical disks using SetupAPI and read-only storage control requests.</summary>
internal static class WindowsDiskInventory
{
    /// <summary>Returns actual Windows disk numbers and capacities, including disks without drive letters.</summary>
    public static Dictionary<uint, long?> Discover()
    {
        Guid diskInterface = new("53f56307-b6bf-11d0-94f2-00a0c91efb8b");
        nint devices = SetupDiGetClassDevs(ref diskInterface, null, 0, 0x12);
        var disks = new Dictionary<uint, long?>();
        if (devices == -1) { return disks; }
        try
        {
            for (uint index = 0; ; index++)
            {
                var data = new DeviceInterface { Size = (uint)Marshal.SizeOf<DeviceInterface>() };
                if (!SetupDiEnumDeviceInterfaces(devices, 0, ref diskInterface, index, ref data)) { break; }
                _ = SetupDiGetDeviceInterfaceDetail(devices, ref data, 0, 0, out uint size, 0);
                if (size < 8 || size > 65536) { continue; }
                nint detail = Marshal.AllocHGlobal((int)size);
                try
                {
                    Marshal.WriteInt32(detail, nint.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetail(devices, ref data, detail, size, out _, 0)) { continue; }
                    string? path = Marshal.PtrToStringUni(detail + 4);
                    if (path is null) { continue; }
                    using SafeFileHandle handle = Open(path);
                    uint? number = DeviceNumber(handle);
                    if (number.HasValue) { disks[number.Value] = Capacity(handle); }
                }
                finally { Marshal.FreeHGlobal(detail); }
            }
        }
        finally { _ = SetupDiDestroyDeviceInfoList(devices); }
        return disks;
    }
    /// <summary>Maps a mounted volume to its physical Windows disk number without using alphabetical order.</summary>
    public static uint? VolumeDisk(string drive)
    { using SafeFileHandle handle = Open(@"\\.\" + drive.TrimEnd('\\')); return DeviceNumber(handle); }
    /// <summary>Opens only metadata access; no raw reads, writes or administrator access are requested.</summary>
    private static SafeFileHandle Open(string path) => CreateFile(path, 0, 3, 0, 3, 0, 0);
    /// <summary>Reads a disk number returned by the storage driver.</summary>
    private static uint? DeviceNumber(SafeFileHandle handle)
    {
        byte[] result = new byte[12];
        return !handle.IsInvalid && DeviceIoControl(handle, 0x2D1080, 0, 0, result, (uint)result.Length, out uint size, 0) && size >= 12
            ? BitConverter.ToUInt32(result, 4) : null;
    }
    /// <summary>Reads physical capacity from DISK_GEOMETRY_EX, retaining unavailable measurements as null.</summary>
    private static long? Capacity(SafeFileHandle handle)
    {
        byte[] result = new byte[4096];
        if (!DeviceIoControl(handle, 0x700A0, 0, 0, result, (uint)result.Length, out uint size, 0) || size < 32) { return null; }
        long bytes = BitConverter.ToInt64(result, 24);
        return bytes > 0 ? bytes : null;
    }
    [StructLayout(LayoutKind.Sequential)] private struct DeviceInterface { internal uint Size; internal Guid Class; internal uint Flags; internal nuint Reserved; }
    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetClassDevsW", CharSet = CharSet.Unicode)] private static extern nint SetupDiGetClassDevs(ref Guid guid, string? enumerator, nint parent, uint flags);
    [DllImport("setupapi.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiEnumDeviceInterfaces(nint devices, nint device, ref Guid guid, uint index, ref DeviceInterface data);
    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInterfaceDetailW")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiGetDeviceInterfaceDetail(nint devices, ref DeviceInterface data, nint detail, uint size, out uint required, nint device);
    [DllImport("setupapi.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiDestroyDeviceInfoList(nint devices);
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode)] private static extern SafeFileHandle CreateFile(string name, uint access, uint share, nint security, uint creation, uint flags, nint template);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeviceIoControl(SafeFileHandle handle, uint code, nint input, uint inputSize, [Out] byte[] output, uint outputSize, out uint returned, nint overlapped);
}
