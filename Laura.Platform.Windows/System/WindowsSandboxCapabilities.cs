using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Laura.Platform.Windows.System;

/// <summary>Owns only the fixed diagnostic registry-read capability; no network or device capability can be requested through this type.</summary>
internal sealed partial class WindowsSandboxCapabilities : IDisposable
{
    private nint _sid, _list;
    internal nint List => _list;
    internal uint Count => _sid == 0 ? 0u : 1u;

    /// <summary>Creates an empty list or derives the documented fixed registryRead diagnostic capability. Args: registryRead: Explicit compatibility experiment. Returns: Owned native memory.</summary>
    internal static WindowsSandboxCapabilities Create(bool registryRead)
    {
        var result = new WindowsSandboxCapabilities();
        if (!registryRead) { return result; }
        if (DeriveCapabilitySidsFromName("registryRead", out nint groups, out uint groupCount, out nint capabilities, out uint capabilityCount) == 0)
        { throw new Win32Exception(Marshal.GetLastPInvokeError()); }
        try
        {
            if (capabilityCount != 1) { throw new InvalidOperationException("Unexpected native registry capability count."); }
            result._sid = Marshal.ReadIntPtr(capabilities); Marshal.WriteIntPtr(capabilities, 0);
            result._list = Marshal.AllocHGlobal(Marshal.SizeOf<SidAndAttributes>());
            Marshal.StructureToPtr(new SidAndAttributes { Sid = result._sid, Attributes = 4 }, result._list, false);
            return result;
        }
        catch { result.Dispose(); throw; }
        finally { FreeArray(groups, groupCount); FreeArray(capabilities, capabilityCount); }
    }

    /// <summary>Frees each API-owned SID and its pointer array.</summary>
    private static void FreeArray(nint array, uint count)
    {
        if (array == 0) { return; }
        for (int index = 0; index < count; index++) { nint sid = Marshal.ReadIntPtr(array, checked(index * nint.Size)); if (sid != 0) { _ = LocalFree(sid); } }
        _ = LocalFree(array);
    }

    /// <inheritdoc />
    public void Dispose()
    { if (_list != 0) { Marshal.FreeHGlobal(_list); _list = 0; } if (_sid != 0) { _ = LocalFree(_sid); _sid = 0; } }

    [StructLayout(LayoutKind.Sequential)] private struct SidAndAttributes { internal nint Sid; internal uint Attributes; }
    /// <summary>Derives only the fixed capability selected by trusted diagnostic code.</summary>
    [LibraryImport("kernelbase.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)] private static partial int DeriveCapabilitySidsFromName(string name, out nint groups, out uint groupCount, out nint capabilities, out uint capabilityCount);
    /// <summary>Releases memory allocated by capability derivation.</summary>
    [LibraryImport("kernel32.dll")] private static partial nint LocalFree(nint memory);
}
