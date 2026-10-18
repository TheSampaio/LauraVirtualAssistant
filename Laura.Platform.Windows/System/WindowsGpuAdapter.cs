using System.Runtime.InteropServices;

namespace Laura.Platform.Windows.System;

/// <summary>Identifies the hardware adapter and its full-width dedicated memory capacity through DXGI.</summary>
internal sealed record WindowsGpuAdapter(string Name, string CounterId, ulong DedicatedMemory, ulong SharedMemoryLimit, uint VendorId, uint DeviceId)
{
    /// <summary>Selects the hardware GPU with the largest dedicated memory, excluding software adapters.</summary>
    public static WindowsGpuAdapter? Discover() => DiscoverAll()?.OrderByDescending(adapter => adapter.DedicatedMemory).FirstOrDefault();

    /// <summary>Enumerates hardware adapters once, preserving unknown enumeration separately from an empty hardware list.</summary>
    public static unsafe IReadOnlyList<WindowsGpuAdapter>? DiscoverAll()
    {
        Guid factoryId = new("770aae78-f26f-4dba-a829-253c83d1b387");
        if (CreateDXGIFactory1(ref factoryId, out nint factory) < 0) { return null; }
        try
        {
            var adapters = new List<WindowsGpuAdapter>();
            var enumerate = (delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)(*(nint**)factory)[12];
            for (uint index = 0; index < 64; index++)
            {
                nint adapter = 0;
                int result = enumerate(factory, index, &adapter);
                if (result == unchecked((int)0x887A0002)) { return adapters; }
                if (result < 0) { return null; }
                try
                {
                    AdapterDescription description = default;
                    var describe = (delegate* unmanaged[Stdcall]<nint, AdapterDescription*, int>)(*(nint**)adapter)[10];
                    if (describe(adapter, &description) < 0) { return null; }
                    if ((description.Flags & 2) != 0) { continue; }
                    var candidate = new WindowsGpuAdapter(new string(description.Description, 0, 128).TrimEnd('\0'),
                        $"luid_0x{description.LuidHigh:x8}_0x{description.LuidLow:x8}", (ulong)description.DedicatedVideoMemory,
                        (ulong)description.SharedSystemMemory, description.VendorId, description.DeviceId);
                    if (!adapters.Any(existing => existing.CounterId == candidate.CounterId)) { adapters.Add(candidate); }
                }
                finally { Release(adapter); }
            }
            return null;
        }
        finally { Release(factory); }
    }
    /// <summary>Releases a native COM reference without creating a managed COM wrapper.</summary>
    private static unsafe void Release(nint instance) => _ = ((delegate* unmanaged[Stdcall]<nint, uint>)(*(nint**)instance)[2])(instance);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private unsafe struct AdapterDescription
    {
        internal fixed char Description[128];
        internal uint VendorId, DeviceId, SubsystemId, Revision;
        internal nuint DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
        internal uint LuidLow, LuidHigh, Flags;
    }
    [DllImport("dxgi.dll")] private static extern int CreateDXGIFactory1(ref Guid id, out nint factory);
}
