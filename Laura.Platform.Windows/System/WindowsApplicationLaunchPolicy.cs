using System.Security.AccessControl;
using System.Security.Principal;
using System.Xml;
using Laura.Core.Files;
using Laura.Core.Permissions;

namespace Laura.Platform.Windows.System;

/// <summary>Classifies bounded installed GUI openings and separately verifies protected installations for unreviewed automatic opening.</summary>
public static class WindowsApplicationLaunchPolicy
{
    private static readonly string[] ProtectedPrograms = ["setup", "install", "update", "unins", "regedit", "regedt32", "rundll32", "mshta", "powershell", "pwsh", "cmd", "wscript", "cscript", "python", "ruby", "node", "java", "autoruns", "procexp", "taskmgr", "credential", "security", "msiexec"];
    /// <summary>Reads PE headers locally; GUI entrypoints must be launched interactively rather than awaited/killed as CLI jobs.</summary>
    public static bool IsGraphical(string target)
    {
        if (!Path.IsPathFullyQualified(target) || !target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || PermissionPolicy.IsNetworkPath(target)) { return false; }
        try { PermissionPolicy.ValidateResources(new(PermissionOperation.Process, "GUI inspection", "Inspect executable", [target]), []); using var input = File.OpenRead(target); return ExecutableMetadata.Read(input).IsGraphicalApplication; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or BadImageFormatException or XmlException or OverflowException or PermissionDeniedException or ArgumentException) { return false; }
    }
    /// <summary>Checks the bounded application effect. Writable installations still require exact reviewed grants.</summary>
    public static bool IsBoundedInstalledApplication(string target, string? arguments)
    {
        if (!string.IsNullOrWhiteSpace(arguments) || !IsGraphical(target) || new FileInfo(target).Length > 64 * 1024 * 1024) { return false; }
        if (!new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 }.Select(Environment.GetFolderPath)
            .Any(root => root.Length > 0 && PermissionPolicy.Within(target, root))) { return false; }
        if (ProtectedPrograms.Any(name => Path.GetFileNameWithoutExtension(target).Contains(name, StringComparison.OrdinalIgnoreCase))) { return false; }
        try { using var input = File.OpenRead(target); return !ExecutableMetadata.Read(input).RequestsElevation; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or BadImageFormatException or XmlException or OverflowException or ArgumentException) { return false; }
    }
    /// <summary>Unreviewed automatic opening additionally requires a protected installation; writable installations need an exact hash-pinned application grant.</summary>
    public static bool CanOpenAutomatically(string target, string? arguments)
    {
        if (!IsBoundedInstalledApplication(target, arguments)) { return false; }
        try
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            var principals = (identity.Groups?.Select(group => group.Value) ?? []).Append(identity.User!.Value)
                .Where(sid => sid is not ("S-1-5-32-544" or "S-1-5-18")).ToHashSet(StringComparer.Ordinal);
            // A writable parent permits replacement even if the executable itself is read-only.
            for (string? current = target; current is not null; current = Path.GetDirectoryName(current))
            {
                FileSystemSecurity security = File.Exists(current) ? new FileInfo(current).GetAccessControl() : new DirectoryInfo(current).GetAccessControl();
                foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
                {
                    if ((rule.PropagationFlags & PropagationFlags.InheritOnly) != 0) { continue; }
                    if (rule.AccessControlType == AccessControlType.Allow && (principals.Contains(rule.IdentityReference.Value)
                        || rule.IdentityReference.Value is "S-1-1-0" or "S-1-5-11" or "S-1-5-32-545")
                        && (rule.FileSystemRights & (FileSystemRights.Write | FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership)) != 0)
                    { return false; }
                }
                if (new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 }.Select(Environment.GetFolderPath).Contains(current, StringComparer.OrdinalIgnoreCase)) { break; }
            }
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or BadImageFormatException or XmlException or OverflowException or global::System.Security.SecurityException or ArgumentException) { return false; }
    }
}
