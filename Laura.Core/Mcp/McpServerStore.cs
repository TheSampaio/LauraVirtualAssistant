using System.Text.Json;
using Laura.Core.Permissions;

namespace Laura.Core.Mcp;

/// <summary>Bounded, atomic JSON configuration in application security storage. Brain content cannot register or activate servers.</summary>
public sealed class McpServerStore(string path)
{
    private readonly object _gate = new();
    private static readonly JsonSerializerOptions Json = new() { MaxDepth = 12, WriteIndented = true };
    private sealed record Document(int Version, IReadOnlyList<McpServerDefinition> Servers);
    /// <summary>Reads explicit definitions; malformed/future configuration is preserved and reported rather than silently replaced.</summary>
    public IReadOnlyList<McpServerDefinition> Read()
    {
        lock (_gate)
        {
            if (!File.Exists(path)) { return []; }
            if (new FileInfo(path).Length > 64000) { throw new IOException("MCP configuration exceeds its bound."); }
            Document data = JsonSerializer.Deserialize<Document>(File.ReadAllText(path), Json) ?? throw new IOException("Invalid MCP configuration.");
            if (data.Version != 1) { throw new IOException("Unsupported MCP configuration version."); }
            return Validate(data.Servers);
        }
    }
    /// <summary>Saves only native user-edited definitions. No model tool exposes this operation.</summary>
    public void Save(IReadOnlyList<McpServerDefinition> servers)
    {
        McpServerDefinition[] valid = Validate(servers);
        string content = JsonSerializer.Serialize(new Document(1, valid), Json); if (content.Length > 64000) { throw new IOException("MCP configuration exceeds its bound."); }
        lock (_gate)
        {
            string absolute = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
            string temporary = absolute + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllText(temporary, content); File.Move(temporary, absolute, true); }
            finally { if (File.Exists(temporary)) { File.Delete(temporary); } }
        }
    }
    /// <summary>Returns an immutable parameter fingerprint; this is not an executable signature or approval.</summary>
    public static string Fingerprint(McpServerDefinition definition) => Brain.FileBrainStorage.Hash(JsonSerializer.Serialize(definition));
    private static McpServerDefinition[] Validate(IReadOnlyList<McpServerDefinition>? servers)
    {
        if (servers is null || servers.Count > 16) { throw new ArgumentException("At most sixteen MCP definitions are supported."); }
        var result = new List<McpServerDefinition>(); var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (McpServerDefinition item in servers)
        {
            if (item is null || item.Id is not { Length: > 0 and <= 40 } || !item.Id.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-') || !ids.Add(item.Id)
                || string.IsNullOrWhiteSpace(item.Name) || item.Name.Length > 100 || item.Executable is not { Length: > 0 and <= 1000 } || item.Directory is not { Length: > 0 and <= 1000 }
                || !Path.IsPathFullyQualified(item.Executable) || !Path.IsPathFullyQualified(item.Directory) || PermissionPolicy.IsNetworkPath(item.Executable) || PermissionPolicy.IsNetworkPath(item.Directory)
                || item.Arguments is null || item.Arguments.Count > 32 || item.Arguments.Any(argument => argument is null || argument.Length > 2000 || argument.Contains('\0', StringComparison.Ordinal)))
            { throw new ArgumentException("MCP definitions require unique bounded IDs and explicit local executable, directory and arguments."); }
            result.Add(item with { Executable = Path.GetFullPath(item.Executable), Directory = Path.GetFullPath(item.Directory), Arguments = item.Arguments.ToArray() });
        }
        return result.ToArray();
    }
}
