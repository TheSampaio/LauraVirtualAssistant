using System.Runtime.InteropServices;

namespace Laura.Platform.Windows.System;

/// <summary>Identifies the hardware adapter and its full-width dedicated memory capacity through DXGI.</summary>
internal sealed record WindowsGpuAdapter(string Name, string CounterId, ulong DedicatedMemory)
{
    /// <summary>Selects the hardware GPU with the largest dedicated memory, excluding software adapters.</summary>
    public static unsafe WindowsGpuAdapter? Discover()
    {
        Guid factoryId = new("770aae78-f26f-4dba-a829-253c83d1b387");
        if (CreateDXGIFactory1(ref factoryId, out nint factory) < 0) { return null; }
        try
        {
            WindowsGpuAdapter? selected = null;
            var enumerate = (delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)(*(nint**)factory)[12];
            for (uint index = 0; ; index++)
            {
                nint adapter = 0;
                if (enumerate(factory, index, &adapter) < 0) { break; }
                try
                {
                    AdapterDescription description = default;
                    var describe = (delegate* unmanaged[Stdcall]<nint, AdapterDescription*, int>)(*(nint**)adapter)[10];
                    if (describe(adapter, &description) < 0 || (description.Flags & 2) != 0) { continue; }
                    var candidate = new WindowsGpuAdapter(new string(description.Description, 0, 128).TrimEnd('\0'),
                        $"luid_0x{description.LuidHigh:x8}_0x{description.LuidLow:x8}", (ulong)description.DedicatedVideoMemory);
                    if (selected is null || candidate.DedicatedMemory > selected.DedicatedMemory) { selected = candidate; }
                }
                finally { Release(adapter); }
            }
            return selected;
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
