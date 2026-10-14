using System.Text.Json;

namespace Laura.Core.Mcp;

/// <summary>Explicit local configuration outside Markdown. No connection or installation occurs when a definition is stored.</summary>
public sealed record McpServerDefinition(string Id, string Name, string Executable, IReadOnlyList<string> Arguments, string Directory);
/// <summary>Bounded, untrusted metadata advertised by an actual connected server; annotations never establish authorization.</summary>
public sealed record McpTool(string Name, string Description, JsonElement InputSchema, string Fingerprint);
/// <summary>Text-only external result. Embedded images, audio, links and resources are not implicitly read or transmitted.</summary>
public sealed record McpToolResult(string Text, bool IsError, int OmittedContent = 0);
/// <summary>Observable local connection state without arguments, credentials or remote error payloads.</summary>
public sealed record McpServerState(string Id, string Name, string State, int ToolCount, string Protocol, string Failure);
/// <summary>Platform-owned protocol connection. Capability declarations and returned text are untrusted data.</summary>
public interface IMcpSession : IAsyncDisposable
{
    /// <summary>Gets whether the owned transport is still connected.</summary>
    bool IsConnected { get; }
    /// <summary>Completes when the owned transport stops, allowing prompt removal of advertised availability.</summary>
    Task Completion { get; }
    /// <summary>Gets the actual negotiated protocol version.</summary>
    string Protocol { get; }
    /// <summary>Gets bounded current tool definitions from the connected server.</summary>
    Task<IReadOnlyList<McpTool>> ListToolsAsync(CancellationToken cancellationToken);
    /// <summary>Invokes one exact tool after the native permission boundary; never retries a mutation automatically.</summary>
    Task<McpToolResult> CallAsync(string tool, JsonElement arguments, CancellationToken cancellationToken);
}
