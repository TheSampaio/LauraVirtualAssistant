using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Laura.Platform.Windows.System;

/// <summary>Owns a fresh profile for explicit native isolation diagnostics. No production tool or automatic permission uses this prototype.</summary>
internal sealed partial class WindowsSandboxProfile : IDisposable
{
    private readonly string _name;
    private nint _sid;
    private readonly List<string> _directories = [];
    private readonly WindowsSandboxCapabilities _capabilities;
    private WindowsSandboxDesktop? _desktop;
    internal nint Sid => _sid != 0 ? _sid : throw new ObjectDisposedException(nameof(WindowsSandboxProfile));
    internal string Directory { get; }
    internal bool LessPrivileged { get; }
    internal bool DisableWin32k { get; }
    internal nint Capabilities => _capabilities.List;
    internal uint CapabilityCount => _capabilities.Count;
    internal string? DesktopName => _desktop?.Name;
    internal bool DesktopClosed => _desktop is null || _desktop.Closed;

    /// <summary>Creates an inactive private desktop only for the explicit diagnostic alternative to win32k lockdown.</summary>
    internal void CreateDiagnosticDesktop()
    {
        if (DisableWin32k || _desktop is not null) { throw new InvalidOperationException("Private desktop diagnostics require the explicit USER32 compatibility mode."); }
        _desktop = WindowsSandboxDesktop.Create(new SecurityIdentifier(Sid));
    }

    /// <summary>Retains only a newly created profile. Args: name: Unique owned moniker; sid: Owned package SID; directory: Native profile path.</summary>
    private WindowsSandboxProfile(string name, nint sid, string directory, bool lessPrivileged, bool disableWin32k, WindowsSandboxCapabilities capabilities)
    { _name = name; _sid = sid; Directory = directory; LessPrivileged = lessPrivileged; DisableWin32k = disableWin32k; _capabilities = capabilities; }

    /// <summary>Creates a fresh per-user diagnostic profile. Args: lessPrivileged: LPAC or comparison control; disableWin32k: Creation mitigation control; registryRead: Fixed standard-registry compatibility capability. Returns: Owned identity. Raises: COMException for native setup failure.</summary>
    internal static WindowsSandboxProfile CreateForDiagnostic(bool lessPrivileged = true, bool disableWin32k = true, bool registryRead = false)
    {
        if (!Environment.GetCommandLineArgs().Contains("--sandbox-probe", StringComparer.Ordinal)) { throw new InvalidOperationException("Sandbox profiles require explicit isolated diagnostics."); }
        string name = "Laura.Sandbox.Probe." + Guid.NewGuid().ToString("N");
        var capabilities = WindowsSandboxCapabilities.Create(registryRead); nint sid = 0; bool created = false;
        try
        {
            Marshal.ThrowExceptionForHR(CreateAppContainerProfile(name, "Laura isolation diagnostic", "Temporary synthetic isolation diagnostic", capabilities.List, capabilities.Count, out sid)); created = true;
            string sidText = new SecurityIdentifier(sid).Value;
            Marshal.ThrowExceptionForHR(GetAppContainerFolderPath(sidText, out nint path));
            try { return new(name, sid, Marshal.PtrToStringUni(path) ?? throw new InvalidOperationException("Native profile path is unavailable."), lessPrivileged, disableWin32k, capabilities); }
            finally { Marshal.FreeCoTaskMem(path); }
        }
        catch { if (created) { _ = DeleteAppContainerProfile(name); } if (sid != 0) { _ = FreeSid(sid); } capabilities.Dispose(); throw; }
    }

    /// <summary>Grants only a harness-created unredirected diagnostic tree. Args: directory: Fresh owned staging directory. Raises: InvalidOperationException for paths outside repository diagnostics.</summary>
    internal void GrantDiagnosticDirectory(string directory)
    {
        string? root = null;
        string path = Path.GetFullPath(directory);
        // The platform output and smoke output have different depth; locate the repository marker without modifying ancestors.
        var ancestor = new DirectoryInfo(AppContext.BaseDirectory);
        for (int depth = 0; depth < 8 && ancestor is not null; depth++, ancestor = ancestor.Parent)
        { if (File.Exists(Path.Combine(ancestor.FullName, "Laura.sln"))) { root = Path.Combine(ancestor.FullName, "_Output"); break; } }
        if (root is null || !path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !global::System.IO.Directory.Exists(path))
        { throw new InvalidOperationException("Sandbox diagnostic ACLs require an owned repository output subtree."); }
        for (var parent = new DirectoryInfo(path); parent is not null; parent = parent.Parent)
        { if ((parent.Attributes & FileAttributes.ReparsePoint) != 0) { throw new InvalidOperationException("Redirected diagnostic paths are unsupported."); } }
        var pending = new Stack<string>(); pending.Push(path);
        while (pending.TryPop(out string? current))
        {
            foreach (string entry in global::System.IO.Directory.EnumerateFileSystemEntries(current))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0) { throw new InvalidOperationException("Diagnostic staging contains a redirected entry."); }
                if ((attributes & FileAttributes.Directory) != 0) { pending.Push(entry); }
            }
        }
        var information = new DirectoryInfo(path); var acl = information.GetAccessControl();
        acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(Sid), FileSystemRights.Modify | FileSystemRights.Synchronize,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        information.SetAccessControl(acl); _directories.Add(path);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_sid == 0) { return; }
        _desktop?.Dispose();
        var identity = new SecurityIdentifier(_sid);
        foreach (string path in _directories)
        {
            var information = new DirectoryInfo(path); var acl = information.GetAccessControl();
            acl.PurgeAccessRules(identity); information.SetAccessControl(acl);
        }
        Marshal.ThrowExceptionForHR(DeleteAppContainerProfile(_name)); _ = FreeSid(_sid); _sid = 0; _capabilities.Dispose();
    }

    /// <summary>Creates a fresh current-user AppContainer profile with no device or network capabilities.</summary>
    [LibraryImport("userenv.dll", StringMarshalling = StringMarshalling.Utf16)] private static partial int CreateAppContainerProfile(string name, string displayName, string description, nint capabilities, uint count, out nint sid);
    /// <summary>Retrieves the owned native profile storage location.</summary>
    [LibraryImport("userenv.dll", StringMarshalling = StringMarshalling.Utf16)] private static partial int GetAppContainerFolderPath(string sid, out nint path);
    /// <summary>Deletes only the exact profile created by this object.</summary>
    [LibraryImport("userenv.dll", StringMarshalling = StringMarshalling.Utf16)] private static partial int DeleteAppContainerProfile(string name);
    /// <summary>Releases the SID allocated by profile creation.</summary>
    [LibraryImport("advapi32.dll")] private static partial nint FreeSid(nint sid);
}
