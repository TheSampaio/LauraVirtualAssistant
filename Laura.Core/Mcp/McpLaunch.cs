using System.Security.Cryptography;
using System.Text.Json;
using Laura.Core.Permissions;

namespace Laura.Core.Mcp;

/// <summary>Pins the explicitly configured executable and absolute existing file arguments across approval. Does not sandbox a third-party process or attest its other libraries.</summary>
public sealed class McpLaunch
{
    private readonly Dictionary<string, string> _files;
    private McpLaunch(McpServerDefinition definition, Dictionary<string, string> files) { Definition = definition; _files = files; }
    /// <summary>Gets the frozen native definition.</summary>
    public McpServerDefinition Definition { get; }
    /// <summary>Gets exact configured command and file-byte identity, used only as a permission parameter digest.</summary>
    public string Identity => JsonSerializer.Serialize(new { definition = Definition, files = _files });
    /// <summary>Gets all configured local resources checked by the execution permission policy.</summary>
    public IReadOnlyList<string> Resources => _files.Keys.Append(Definition.Directory).ToArray();
    /// <summary>Validates a bounded command without executing or fetching dependencies. Entry scripts must use absolute paths.</summary>
    public static McpLaunch Prepare(McpServerDefinition definition)
    {
        if (!System.IO.Directory.Exists(definition.Directory) || !definition.Executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) { throw new IOException("MCP requires an installed local executable and existing working directory."); }
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [definition.Executable] = "" };
        foreach (string argument in definition.Arguments)
        {
            if (Path.IsPathFullyQualified(argument) && File.Exists(argument)) { files.TryAdd(Path.GetFullPath(argument), ""); }
            else if (!Path.IsPathFullyQualified(argument) && new[] { ".dll", ".js", ".mjs", ".cjs", ".py", ".ps1" }.Contains(Path.GetExtension(argument), StringComparer.OrdinalIgnoreCase))
            { throw new IOException("Use absolute local paths for MCP entry scripts."); }
        }
        foreach (string file in files.Keys.ToArray()) { Validate(file); using var stream = File.OpenRead(file); files[file] = Convert.ToHexString(SHA256.HashData(stream)); }
        return new(definition, files);
    }
    /// <summary>Revalidates identity immediately before process creation while denying writes/deletion of pinned files until creation completes.</summary>
    public IDisposable Acquire()
    {
        var handles = new List<FileStream>();
        try
        {
            foreach ((string file, string hash) in _files)
            {
                Validate(file); var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read); handles.Add(stream);
                if (Convert.ToHexString(SHA256.HashData(stream)) != hash) { throw new IOException("MCP executable or entry script changed after review."); }
            }
            return new Lease(handles);
        }
        catch { foreach (var stream in handles) { stream.Dispose(); } throw; }
    }
    private static void Validate(string file)
    {
        if (PermissionPolicy.IsNetworkPath(file) || new FileInfo(file).Length > 64 * 1024 * 1024) { throw new IOException("MCP executable or entry script is not a bounded local file."); }
        for (string? current = file; current is not null; current = Path.GetDirectoryName(current))
        { if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) { throw new IOException("MCP redirected entry points are not supported."); } }
    }
    private sealed class Lease(IReadOnlyList<FileStream> handles) : IDisposable
    { public void Dispose() { foreach (var stream in handles) { stream.Dispose(); } } }
}
