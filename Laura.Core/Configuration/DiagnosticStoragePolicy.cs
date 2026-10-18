namespace Laura.Core.Configuration;

/// <summary>Rejects diagnostics that could use or contain the owner's ordinary application storage.</summary>
public static class DiagnosticStoragePolicy
{
    /// <summary>Requires a separate explicit data root before constructing diagnostic services. Returns: Canonical isolated path. Raises: InvalidOperationException for absent/overlapping storage.</summary>
    public static string RequireIsolated(string? storage, string owner)
    {
        if (string.IsNullOrWhiteSpace(storage)) { throw new InvalidOperationException("Native diagnostics require an explicit isolated --storage directory before services are created."); }
        string chosen = Path.GetFullPath(storage);
        if (Permissions.PermissionPolicy.Within(chosen, owner) || Permissions.PermissionPolicy.Within(owner, chosen))
        { throw new InvalidOperationException("Native diagnostic storage must be separate from owner application storage."); }
        for (string? parent = chosen; parent is not null; parent = Path.GetDirectoryName(parent))
        { if ((File.Exists(parent) || Directory.Exists(parent)) && (File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0) { throw new InvalidOperationException("Redirected diagnostic storage is unsupported."); } }
        return chosen;
    }
}
