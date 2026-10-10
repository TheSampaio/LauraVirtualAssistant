using System.Runtime.InteropServices;
using Laura.Core.Models;

namespace Laura.Platform.Windows.System;

/// <summary>Reads physical memory from Windows; CPU inference is always the packaged backend.</summary>
public static class WindowsHardwareProbe
{
    /// <summary>Measures CPU count, physical RAM and free space at the chosen storage location.</summary>
    public static HardwareProfile Detect(string modelDirectory)
    {
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        bool available = GlobalMemoryStatusEx(ref memory);
        return new(Environment.ProcessorCount, available ? (long)memory.TotalPhysical : 0,
            available ? (long)memory.AvailablePhysical : 0,
            new DriveInfo(Path.GetPathRoot(Path.GetFullPath(modelDirectory))!).AvailableFreeSpace, "CPU (GPU/VRAM not probed)");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        internal uint Length;
        internal uint MemoryLoad;
        internal ulong TotalPhysical;
        internal ulong AvailablePhysical;
        internal ulong TotalPageFile;
        internal ulong AvailablePageFile;
        internal ulong TotalVirtual;
        internal ulong AvailableVirtual;
        internal ulong AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus buffer);
}
