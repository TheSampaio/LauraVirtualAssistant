using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

/// <summary>Owns only a fresh synthetic current-user registry leaf and never reads existing user values.</summary>
internal sealed partial class SandboxRegistryCanary(string path) : IDisposable
{
    internal string Path { get; } = path;
    /// <summary>Creates a private synthetic key for access checks. Args: user: Current diagnostic user. Returns: Exact owned leaf.</summary>
    internal static SandboxRegistryCanary Create(SecurityIdentifier user)
    {
        string path = "Software\\LauraSandboxProbe-" + Guid.NewGuid().ToString("N");
        int status = RegCreateKeyExW(unchecked((nint)(int)0x80000001), path, 0, 0, 0, 0x000F003F, 0, out var handle, out uint disposition);
        if (status != 0) { handle.Dispose(); throw new Win32Exception(status); }
        using (handle)
        {
            if (disposition != 1) { throw new InvalidOperationException("Synthetic registry key already existed."); }
            try
            {
                var security = new RawSecurityDescriptor("D:P(A;;KA;;;" + user.Value + ")");
                byte[] binary = new byte[security.BinaryLength]; security.GetBinaryForm(binary, 0); nint descriptor = Marshal.AllocHGlobal(binary.Length);
                try { Marshal.Copy(binary, 0, descriptor, binary.Length); status = RegSetKeySecurity(handle, 4, descriptor); if (status != 0) { throw new Win32Exception(status); } }
                finally { Marshal.FreeHGlobal(descriptor); }
                using var key = RegistryKey.FromHandle(handle); key.SetValue("SyntheticCanary", "synthetic private registry canary", RegistryValueKind.String);
            }
            catch { Registry.CurrentUser.DeleteSubKeyTree(path, false); throw; }
        }
        return new(path);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!Path.StartsWith("Software\\LauraSandboxProbe-", StringComparison.Ordinal) || !Guid.TryParseExact(Path[27..], "N", out _))
        { throw new InvalidOperationException("Cannot remove an unowned registry canary."); }
        Registry.CurrentUser.DeleteSubKeyTree(Path, false);
    }

    /// <summary>Creates only a uniquely named leaf with sufficient rights to make its DACL private.</summary>
    [LibraryImport("advapi32.dll", StringMarshalling = StringMarshalling.Utf16)] private static partial int RegCreateKeyExW(nint root, string path, uint reserved, nint @class, uint options, uint access, nint security, out SafeRegistryHandle result, out uint disposition);
    /// <summary>Applies only the synthetic key's owner-only discretionary ACL.</summary>
    [LibraryImport("advapi32.dll")] private static partial int RegSetKeySecurity(SafeRegistryHandle key, uint information, nint descriptor);
}
