using Laura.Core.Abstractions;

namespace Laura.Core.Files;

/// <summary>Resolves known-folder aliases and workspace-relative paths without depending on the process directory.</summary>
public static class KnownFolderPaths
{
    /// <summary>Expands an optional native folder alias before normal path validation. Args: value: Owner path; resolver: Windows folder source; workspace: Native working root. Returns: Absolute local path.</summary>
    public static string Resolve(string value, IKnownFolderResolver resolver, string workspace)
    {
        if (Path.IsPathRooted(value)) { return Path.GetFullPath(Environment.ExpandEnvironmentVariables(value)); }
        string normalized = value.Trim().Replace('\\', '/');
        int separator = normalized.IndexOf('/');
        string alias = separator < 0 ? normalized : normalized[..separator];
        string? knownRoot = alias.Length == 0 || alias.Equals("workspace", StringComparison.OrdinalIgnoreCase) ? workspace : resolver.Resolve(alias);
        string root = knownRoot ?? workspace;
        if (root.Length == 0) { throw new IOException("The requested Windows known folder is unavailable."); }
        string child = knownRoot is null ? normalized : separator < 0 ? string.Empty : normalized[(separator + 1)..];
        string path = Path.GetFullPath(Path.Combine(root, child.Replace('/', Path.DirectorySeparatorChar)));
        if (!Permissions.PermissionPolicy.Within(path, root)) { throw new IOException("The relative path escapes the selected known folder."); }
        return path;
    }
}
