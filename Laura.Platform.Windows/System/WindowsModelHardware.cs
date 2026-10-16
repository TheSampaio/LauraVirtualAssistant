using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using Laura.Core.Models;

namespace Laura.Platform.Windows.System;

/// <summary>Reads current CPU/RAM/disk capacity without claiming an untested GPU backend or model throughput.</summary>
public static partial class WindowsModelHardware
{
    /// <summary>Samples available memory and the model storage volume. Args: storageDirectory: Model storage. Returns: Actual CPU/RAM/disk snapshot; GPU acceleration is not packaged by this adapter.</summary>
    public static HardwareProfile Read(string storageDirectory)
    {
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (GlobalMemoryStatusEx(ref memory) == 0) { throw new global::System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()); }
        return new(Environment.ProcessorCount, (long)memory.TotalPhysical, (long)memory.AvailablePhysical,
            new DriveInfo(Path.GetPathRoot(Path.GetFullPath(storageDirectory))!).AvailableFreeSpace,
            RuntimeInformation.ProcessArchitecture + " CPU · " + (Avx2.IsSupported ? "AVX2" : Avx.IsSupported ? "AVX" : "baseline"));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        internal uint Length, MemoryLoad;
        internal ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }

    /// <summary>Reads the Windows memory-status structure without WMI or administrator access.</summary>
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial int GlobalMemoryStatusEx(ref MemoryStatus status);
}
