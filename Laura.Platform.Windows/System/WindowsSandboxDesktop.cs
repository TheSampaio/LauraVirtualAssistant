using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace Laura.Platform.Windows.System;

/// <summary>Owns a non-visible diagnostic desktop with access granted only to its package, current user and SYSTEM. It never switches the input desktop.</summary>
internal sealed partial class WindowsSandboxDesktop : IDisposable
{
    private readonly DesktopHandle _handle;
    internal string Name { get; }
    internal bool Closed => _handle.NativeClosed;
    /// <summary>Retains the new desktop handle without inheriting it into the child.</summary>
    private WindowsSandboxDesktop(string name, DesktopHandle handle) { Name = name; _handle = handle; }

    /// <summary>Creates on a dedicated thread so no UI thread's desktop association can change. Args: package: Fresh owned package identity. Returns: Exclusive desktop handle.</summary>
    internal static WindowsSandboxDesktop Create(SecurityIdentifier package)
    {
        var completion = new TaskCompletionSource<WindowsSandboxDesktop>(TaskCreationOptions.RunContinuationsAsynchronously);
        var creator = new Thread(() =>
        {
            try { var result = CreateNative(package); if (!completion.TrySetResult(result)) { result.Dispose(); } }
            catch (Exception failure) { completion.TrySetException(failure); }
        }) { IsBackground = true, Name = "Laura sandbox desktop creation" };
        creator.Start();
        try { return completion.Task.WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult(); }
        catch { completion.TrySetCanceled(); throw; }
    }

    /// <summary>Creates a unique desktop with an explicit low-integrity DACL on the isolated creator thread.</summary>
    private static WindowsSandboxDesktop CreateNative(SecurityIdentifier package)
    {
        using var user = WindowsIdentity.GetCurrent();
        var descriptor = new RawSecurityDescriptor("D:P(A;;GA;;;SY)(A;;GA;;;" + user.User!.Value + ")(A;;0x000200C7;;;" + package.Value + ")S:(ML;;NW;;;LW)");
        byte[] binary = new byte[descriptor.BinaryLength]; descriptor.GetBinaryForm(binary, 0);
        nint memory = Marshal.AllocHGlobal(binary.Length);
        try
        {
            Marshal.Copy(binary, 0, memory, binary.Length);
            var attributes = new SecurityAttributes { Size = Marshal.SizeOf<SecurityAttributes>(), Descriptor = memory };
            string name = "LauraSandbox" + Guid.NewGuid().ToString("N");
            var handle = new DesktopHandle(CreateDesktopW(name, 0, 0, 0, 0x000F01FF, ref attributes));
            if (handle.IsInvalid) { int error = Marshal.GetLastPInvokeError(); handle.Dispose(); throw new Win32Exception(error); }
            return new(name, handle);
        }
        finally { Marshal.FreeHGlobal(memory); }
    }

    /// <inheritdoc />
    public void Dispose() => _handle.Dispose();

    /// <summary>Closes desktops through their dedicated API, never CloseHandle.</summary>
    private sealed class DesktopHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        /// <summary>Retains the creator's exclusive non-inheritable desktop handle.</summary>
        internal DesktopHandle(nint value) : base(true) { SetHandle(value); }
        internal bool NativeClosed { get; private set; }
        /// <inheritdoc />
        protected override bool ReleaseHandle() { NativeClosed = CloseDesktop(handle) != 0; return NativeClosed; }
    }

    [StructLayout(LayoutKind.Sequential)] private struct SecurityAttributes { internal int Size; internal nint Descriptor; internal int Inherit; }
    /// <summary>Creates only an inactive desktop in the current window station.</summary>
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)] private static partial nint CreateDesktopW(string name, nint device, nint mode, uint flags, uint access, ref SecurityAttributes attributes);
    /// <summary>Releases the exclusive desktop after every child process has exited.</summary>
    [LibraryImport("user32.dll", SetLastError = true)] private static partial int CloseDesktop(nint desktop);
}
