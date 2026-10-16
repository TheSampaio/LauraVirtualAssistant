using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using System.Buffers.Binary;
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
        IReadOnlyList<WindowsGpuAdapter>? adapters = WindowsGpuAdapter.DiscoverAll();
        return new(Environment.ProcessorCount, (long)memory.TotalPhysical, (long)memory.AvailablePhysical,
            new DriveInfo(Path.GetPathRoot(Path.GetFullPath(storageDirectory))!).AvailableFreeSpace,
            RuntimeInformation.ProcessArchitecture + " CPU · " + (Avx2.IsSupported ? "AVX2" : Avx.IsSupported ? "AVX" : "baseline"))
        {
            PhysicalCores = ReadPhysicalCores(), Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            Instructions = ReadInstructions(), GpuDiscoveryAvailable = adapters is not null,
            GpuAdapters = adapters is null ? [] : adapters.Select(adapter => new GpuHardwareInfo(adapter.Name,
                adapter.DedicatedMemory, adapter.SharedMemoryLimit, adapter.VendorId, adapter.DeviceId) { AdapterId = adapter.CounterId }).ToArray(),
        };
    }

    /// <summary>Reads only feature flags exposed by the runtime, without inferring support from CPU marketing names.</summary>
    private static List<string> ReadInstructions()
    {
        var features = new List<string>();
        if (Sse2.IsSupported) { features.Add("SSE2"); }
        if (Sse41.IsSupported) { features.Add("SSE4.1"); }
        if (Avx.IsSupported) { features.Add("AVX"); }
        if (Avx2.IsSupported) { features.Add("AVX2"); }
        if (Fma.IsSupported) { features.Add("FMA"); }
        if (Avx512F.IsSupported) { features.Add("AVX512F"); }
        if (global::System.Runtime.Intrinsics.Arm.AdvSimd.IsSupported) { features.Add("AdvSimd"); }
        return features;
    }

    /// <summary>Counts variable-length core relationship records across processor groups with bounded native buffers.</summary>
    private static unsafe int? ReadPhysicalCores()
    {
        uint length = 0;
        _ = GetLogicalProcessorInformationEx(0, null, ref length);
        if (Marshal.GetLastWin32Error() != 122 || length is < 8 or > 1024 * 1024) { return null; }
        byte[] buffer = new byte[length];
        fixed (byte* pointer = buffer)
        { if (GetLogicalProcessorInformationEx(0, pointer, ref length) == 0 || length > buffer.Length) { return null; } }
        int count = 0, offset = 0;
        while (offset <= (int)length - 8)
        {
            uint relationship = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(offset));
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(offset + 4));
            if (size < 8 || size > length - offset || relationship != 0) { return null; }
            count++; offset += (int)size;
        }
        return offset == length && count > 0 ? count : null;
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
    /// <summary>Queries Windows processor topology without WMI or administrator access.</summary>
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static unsafe partial int GetLogicalProcessorInformationEx(int relationship, byte* buffer, ref uint returnedLength);
}
