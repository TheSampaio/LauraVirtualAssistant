using System.Text.Json;

namespace Laura.Core.Permissions;

/// <summary>Atomic bounded JSON outside Brain. An invalid store never grants access.</summary>
public sealed class PermissionStore(string filePath)
{
    private readonly string _path = Path.GetFullPath(filePath);
    /// <summary>Loads persistent grants and minimal audit records; temporary grants are never restored.</summary>
    public PermissionSnapshot Load()
    {
        if (!File.Exists(_path)) { return new([], []); }
        if (new FileInfo(_path).Length > 4 * 1024 * 1024) { throw new IOException("Permission store exceeds its size limit."); }
        PermissionPolicy.ValidateResources(new(PermissionOperation.Internal, "store", "", [_path]), []);
        var snapshot = JsonSerializer.Deserialize<PermissionSnapshot>(File.ReadAllText(_path)) ?? throw new JsonException("Invalid permission store.");
        if (snapshot.Grants is null || snapshot.History is null) { throw new JsonException("Invalid permission store."); }
        return new(snapshot.Grants.Where(grant => grant.Persistent
            && grant.TaskId == Guid.Empty && grant.Expires is null && grant.Destination.Length == 0
            && grant.Operation is PermissionOperation.FileWrite or PermissionOperation.FileCreateDirectory or PermissionOperation.ApplicationLaunch
            && (grant.Operation != PermissionOperation.ApplicationLaunch || !grant.DirectoryScope)
            && grant.Fingerprint.Length == 64 && grant.Scope.Length > 0).Take(1000).ToArray(), snapshot.History.TakeLast(2000).ToArray());
    }
    /// <summary>Flushes a new snapshot before atomically replacing the original; failures propagate and never permit execution.</summary>
    public void Save(PermissionSnapshot snapshot)
    {
        PermissionPolicy.ValidateResources(new(PermissionOperation.Internal, "store", "", [_path]), []);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        string temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(stream, snapshot); stream.Flush(true); }
            File.Move(temporary, _path, true);
        }
        finally { if (File.Exists(temporary)) { File.Delete(temporary); } }
    }
}
/// <summary>Version-independent local storage payload containing no model-generated policies.</summary>
public sealed record PermissionSnapshot(IReadOnlyList<PermissionGrant> Grants, IReadOnlyList<ApprovalAudit> History);
