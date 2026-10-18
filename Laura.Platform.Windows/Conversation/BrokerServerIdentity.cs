using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Laura.Platform.Windows.Conversation;

/// <summary>Authenticates the pipe server against the administrator-controlled Windows service process.</summary>
internal static partial class BrokerServerIdentity
{
    /// <summary>Rejects a pipe squatter before sending private context or any migration payload.</summary>
    internal static void Verify(SafePipeHandle pipe)
    {
        using var manager = OpenSCManagerW(null, null, 1);
        if (manager.IsInvalid) { throw new UnauthorizedAccessException("broker_identity_unavailable"); }
        using var service = OpenServiceW(manager, BrokerProtocol.ServiceName, 4);
        if (service.IsInvalid || !QueryServiceStatusEx(service, 0, out var status, Marshal.SizeOf<ServiceStatus>(), out _)
            || status.State != 4 || status.ProcessId == 0 || !GetNamedPipeServerProcessId(pipe, out uint processId) || processId != status.ProcessId)
        { throw new UnauthorizedAccessException("broker_identity_invalid"); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatus
    {
        internal uint Type, State, AcceptedControls, Win32ExitCode, ServiceExitCode, CheckPoint, WaitHint, ProcessId, Flags;
    }

    /// <summary>Releases a service-control handle.</summary>
    private sealed class ServiceHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        /// <summary>Creates the marshaler-owned native handle.</summary>
        public ServiceHandle() : base(true) { }
        /// <inheritdoc />
        protected override bool ReleaseHandle() => CloseServiceHandle(handle);
    }

    /// <summary>Opens the service manager with read-only connect access.</summary>
    [LibraryImport("advapi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial ServiceHandle OpenSCManagerW(string? machine, string? database, uint access);
    /// <summary>Opens only status-query access to the fixed broker service.</summary>
    [LibraryImport("advapi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial ServiceHandle OpenServiceW(ServiceHandle manager, string name, uint access);
    /// <summary>Reads the running SCM process identity.</summary>
    [LibraryImport("advapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryServiceStatusEx(ServiceHandle service, int level, out ServiceStatus status, int size, out int needed);
    /// <summary>Reads the actual connected pipe server process.</summary>
    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint processId);
    /// <summary>Closes the service-control handle.</summary>
    [LibraryImport("advapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseServiceHandle(nint handle);
}
