namespace Laura.Core.Permissions;

/// <summary>Code-defined risk and scope rules. Commands, disclosure and input injection require explicit approval; installed GUI opening has a separate bounded policy.</summary>
public static class PermissionPolicy
{
    private static readonly HashSet<string> DataExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".md", ".txt", ".csv", ".log", ".png", ".jpg", ".jpeg" };

    /// <summary>Classifies the entire effect; executable/configuration writes cannot inherit a data-folder grant.</summary>
    public static PermissionRisk Risk(PermissionAction action)
    {
        PermissionRisk risk = action.Operation switch
    {
        PermissionOperation.Internal or PermissionOperation.FileReadLocal => PermissionRisk.Low,
        PermissionOperation.WindowManagement when action.AutomaticEligible && action.Resources.Length == 0 && action.Destination.Length == 0 => PermissionRisk.Moderate,
        PermissionOperation.WorkspaceAuthorization when action.Destination.Length > 0 => PermissionRisk.Critical,
        PermissionOperation.FileWrite when action.Resources.Length == 1 && DataExtensions.Contains(Path.GetExtension(action.Resources[0])) => PermissionRisk.Moderate,
        PermissionOperation.WorkspaceNote or PermissionOperation.FileCreateDirectory or PermissionOperation.ClipboardWrite or PermissionOperation.LocalCapture or PermissionOperation.ScreenshotExport or PermissionOperation.BrainAttachment => PermissionRisk.Moderate,
        PermissionOperation.ApplicationLaunch when action.Resources.Length == 1 && action.Destination.Length == 0 => PermissionRisk.Moderate,
        PermissionOperation.FileCopy or PermissionOperation.FileMove or PermissionOperation.FileTrash => PermissionRisk.High,
        PermissionOperation.Process when action.Destination.Length > 0 => PermissionRisk.Critical,
        PermissionOperation.FileReadCloud or PermissionOperation.ScreenCloud or PermissionOperation.CameraCloud or PermissionOperation.ClipboardCloud or PermissionOperation.DesktopInput or PermissionOperation.Undo or PermissionOperation.ImageCloud or PermissionOperation.ExternalTool => PermissionRisk.Critical,
        _ => PermissionRisk.High
    };
        if (action.Operation != PermissionOperation.Internal && action.Destination.Length > 0 && risk <= PermissionRisk.Moderate) { return PermissionRisk.Critical; }
        return action.Operation != PermissionOperation.Internal && risk < PermissionRisk.High && action.Resources.Any(SensitivePath) ? PermissionRisk.High : risk;
    }
    /// <summary>Only trusted native application adapters may describe this bounded effect; arbitrary processes never inherit the policy.</summary>
    public static bool AllowsAutomatic(PermissionAction action, PermissionOptions options, string? workspaceRoot = null)
    {
        if (options.Mode != ApprovalMode.Automatic || Risk(action) > PermissionRisk.Moderate || action.Destination.Length != 0) { return false; }
        if (action.Operation == PermissionOperation.WindowManagement) { return action.AutomaticEligible; }
        if (action.Operation == PermissionOperation.ApplicationLaunch) { return options.OpenInstalledApplications && action.AutomaticEligible; }
        return workspaceRoot is not null && action.Resources.Length == 1 && Within(action.Resources[0], workspaceRoot)
            && !SensitivePath(action.Resources[0]) && action.Operation is PermissionOperation.FileReadLocal or PermissionOperation.FileWrite or PermissionOperation.FileCreateDirectory or PermissionOperation.WorkspaceNote;
    }
    /// <summary>Identifies policy, credential and security files that cannot inherit automatic workspace capabilities.</summary>
    public static bool SensitivePath(string path)
    {
        string[] segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Any(segment => new[] { ".git", ".ssh", ".aws", ".codex", ".agents", "Security", "Credentials" }.Contains(segment, StringComparer.OrdinalIgnoreCase))
            || new[] { "AGENTS.md", "CLAUDE.md", "SKILL.md", "NuGet.Config", "global.json" }.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            || Path.GetFileName(path).StartsWith(".env", StringComparison.OrdinalIgnoreCase)
            || Path.GetFileName(path).StartsWith("appsettings", StringComparison.OrdinalIgnoreCase)
            || new[] { ".npmrc", ".pypirc", ".netrc", "id_rsa", "id_ed25519", "settings.ini", "api_keys.json" }.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            || new[] { ".key", ".pfx", ".p12", ".pem", ".dat", ".db" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)
            || Path.GetFileName(path).Contains("secret", StringComparison.OrdinalIgnoreCase)
            || Path.GetFileName(path).Contains("credential", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Only bounded local data operations or exact native GUI identities can create session/persistent grants; applications never grant a directory.</summary>
    public static bool CanRemember(PermissionAction action) => action.Operation is PermissionOperation.FileWrite or PermissionOperation.FileCreateDirectory or PermissionOperation.ApplicationLaunch
        && Risk(action) <= PermissionRisk.Moderate && action.Resources.Length == 1 && action.Destination.Length == 0;

    /// <summary>Uses separator boundaries, rejecting sibling prefixes and traversal after canonicalization.</summary>
    public static bool Within(string path, string root)
    {
        string canonical = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        string scope = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return canonical.Equals(scope, comparison) || canonical.StartsWith(scope + Path.DirectorySeparatorChar, comparison);
    }
    /// <summary>Detects UNC/mapped network paths without opening a remote file or triggering SMB authentication.</summary>
    public static bool IsNetworkPath(string path)
    {
        if (path.StartsWith("\\\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal)) { return true; }
        if (!OperatingSystem.IsWindows() || !Path.IsPathFullyQualified(path)) { return false; }
        try { return new DriveInfo(Path.GetPathRoot(path)!).DriveType is DriveType.Network or DriveType.Unknown; }
        catch (Exception error) when (error is IOException or ArgumentException or UnauthorizedAccessException) { return true; }
    }

    /// <summary>Rejects redirects, alternate streams, system roots and application security storage at execution time.</summary>
    public static void ValidateResources(PermissionAction action, IEnumerable<string> forbiddenRoots, string? workspaceRoot = null)
    {
        if (action.Operation == PermissionOperation.Unknown || !Enum.IsDefined(action.Operation)) { throw new PermissionDeniedException("Unknown operation."); }
        foreach (string path in action.Resources)
        {
            if (IsNetworkPath(path)) { throw new PermissionDeniedException("Network filesystem resources require a dedicated policy and are not supported by this local adapter."); }
            if (!Path.IsPathFullyQualified(path) || path.AsSpan(Path.GetPathRoot(path)!.Length).Contains(':'))
            { throw new PermissionDeniedException("Invalid resource path."); }
            if (forbiddenRoots.Any(root => root.Length > 0 && Within(path, root)) && (workspaceRoot is null || !Within(path, workspaceRoot)))
            { throw new PermissionDeniedException("Application security storage is protected."); }
            for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
            {
                if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                { throw new PermissionDeniedException("Redirected resources are not supported."); }
            }
            if (action.Operation is PermissionOperation.FileWrite or PermissionOperation.FileCreateDirectory or PermissionOperation.FileCopy or PermissionOperation.FileMove or PermissionOperation.FileTrash or PermissionOperation.ScreenshotExport or PermissionOperation.WorkspaceAuthorization or PermissionOperation.WorkspaceNote)
            {
                if (Path.TrimEndingDirectorySeparator(path) == Path.TrimEndingDirectorySeparator(Path.GetPathRoot(path)!))
                { throw new PermissionDeniedException("Filesystem roots are protected."); }
                foreach (var folder in new[] { Environment.SpecialFolder.Windows, Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
                { string root = Environment.GetFolderPath(folder); if (root.Length > 0 && Within(path, root)) { throw new PermissionDeniedException("System directories are protected."); } }
            }
        }
    }

    /// <summary>Matches the exact effect, origin and destination. Higher risks never reuse grants.</summary>
    public static bool Matches(PermissionGrant grant, PermissionAction action) => grant.TaskId != Guid.Empty
        ? grant.Operation == PermissionOperation.WorkspaceAuthorization && action.TaskId == grant.TaskId && !grant.Persistent
            && grant.Expires > DateTimeOffset.UtcNow && grant.Origin == action.Origin
            && action.Resources.Length > 0 && action.Resources.All(path => Within(path, grant.Scope) && !SensitivePath(path))
            && (action.Operation is PermissionOperation.FileReadLocal or PermissionOperation.FileWrite or PermissionOperation.FileCreateDirectory
                && action.Destination.Length == 0 || action.Operation == PermissionOperation.FileReadCloud && grant.Destination.Length > 0 && action.Destination == grant.Destination)
        : CanRemember(action)
        && grant.Operation == action.Operation && grant.Origin == action.Origin
        && !(action.Operation == PermissionOperation.ApplicationLaunch && grant.DirectoryScope)
        && (grant.DirectoryScope ? action.Resources.Length == 1 && Within(action.Resources[0], grant.Scope) : grant.Fingerprint == action.Fingerprint);
}
