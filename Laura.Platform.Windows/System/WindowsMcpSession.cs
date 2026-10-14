using System.Diagnostics;
using System.Buffers;
using System.Text;
using System.Text.Json;
using Laura.Core.Brain;
using Laura.Core.Mcp;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Laura.Platform.Windows.System;

/// <summary>Owned, hidden stdio process with the official MCP SDK, bounded wire messages and no inherited credentials or automatic tool execution.</summary>
public sealed class WindowsMcpSession : IMcpSession
{
    private readonly Process _process;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Task _stderr;
    private McpClient? _client;
    private int _disposed;
    private WindowsMcpSession(Process process) { _process = process; _stderr = DrainAsync(process.StandardError, _lifetime.Token); }
    /// <inheritdoc />
    public bool IsConnected
    { get { try { return Volatile.Read(ref _disposed) == 0 && _client?.Completion.IsCompleted == false && !_process.HasExited; } catch (InvalidOperationException) { return false; } } }
    /// <inheritdoc />
    public string Protocol => _client?.NegotiatedProtocolVersion ?? "";
    /// <inheritdoc />
    public Task Completion => _client?.Completion ?? Task.CompletedTask;
    /// <summary>Starts only the frozen already-authorized executable. Startup does not evaluate a shell, install packages or offer sampling/roots/elicitation handlers.</summary>
    public static async Task<IMcpSession> ConnectAsync(McpLaunch launch, CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo(launch.Definition.Executable) { WorkingDirectory = launch.Definition.Directory, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        foreach (string argument in launch.Definition.Arguments) { info.ArgumentList.Add(argument); }
        info.Environment.Clear(); foreach ((string key, string? value) in StdioClientTransportOptions.GetDefaultEnvironmentVariables()) { info.Environment[key] = value; }
        var process = new Process { StartInfo = info };
        try { cancellationToken.ThrowIfCancellationRequested(); if (!process.Start()) { throw new IOException("MCP process was not created."); } }
        catch (global::System.ComponentModel.Win32Exception) { process.Dispose(); throw new IOException("MCP process could not be created."); }
        catch { process.Dispose(); throw; }
        var session = new WindowsMcpSession(process);
        try
        {
            var transport = new StreamClientTransport(process.StandardInput.BaseStream, new McpMessageReadStream(process.StandardOutput.BaseStream));
            session._client = await McpClient.CreateAsync(transport, new McpClientOptions { ClientInfo = new() { Name = "LAURA", Version = "1.0" },
                Capabilities = new(), InitializationTimeout = TimeSpan.FromSeconds(25), DiscoverProbeTimeout = TimeSpan.FromSeconds(3) }, cancellationToken: cancellationToken).ConfigureAwait(false);
            return session;
        }
        catch (Exception error) when (error is McpException or JsonException) { await session.DisposeAsync().ConfigureAwait(false); throw new IOException("MCP initialization failed."); }
        catch { await session.DisposeAsync().ConfigureAwait(false); throw; }
    }
    /// <inheritdoc />
    public async Task<IReadOnlyList<McpTool>> ListToolsAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(!IsConnected, this);
        var tools = new List<McpTool>(); var names = new HashSet<string>(StringComparer.Ordinal); var cursors = new HashSet<string>(StringComparer.Ordinal); string? cursor = null;
        for (int page = 0; page < 4; page++)
        {
            ListToolsResult result;
            try { result = await _client!.ListToolsAsync(new ListToolsRequestParams { Cursor = cursor }, cancellationToken).ConfigureAwait(false); }
            catch (Exception error) when (error is McpException or JsonException) { throw new IOException("MCP capability discovery failed."); }
            foreach (Tool tool in result.Tools)
            {
                string schema = tool.InputSchema.GetRawText();
                if (tool.Name.Length is 0 or > 128 || !tool.Name.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.') || !names.Add(tool.Name)
                    || schema.Length > 32000 || tool.InputSchema.ValueKind != JsonValueKind.Object || tools.Count >= 128)
                { throw new IOException("MCP tool inventory exceeds its supported bounds."); }
                string description = tool.Description ?? ""; description = description.Length > 1000 ? description[..1000] : description;
                tools.Add(new(tool.Name, description, tool.InputSchema.Clone(), FileBrainStorage.Hash(JsonSerializer.Serialize(tool))));
            }
            cursor = result.NextCursor; if (cursor is null) { return tools; }
            if (cursor.Length > 2000 || !cursors.Add(cursor)) { throw new IOException("Invalid MCP pagination."); }
        }
        throw new IOException("MCP inventory exceeds four pages.");
    }
    /// <inheritdoc />
    public async Task<McpToolResult> CallAsync(string tool, JsonElement arguments, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(!IsConnected, this);
        Dictionary<string, object?> values = arguments.EnumerateObject().ToDictionary(property => property.Name, property => (object?)property.Value.Clone(), StringComparer.Ordinal);
        CallToolResult result;
        try { result = await _client!.CallToolAsync(tool, values, cancellationToken: cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && !IsConnected) { throw new IOException("MCP transport closed during invocation."); }
        catch (Exception error) when (error is McpException or JsonException) { throw new IOException("MCP tool protocol failed."); }
        var text = new StringBuilder(); int omitted = 0;
        foreach (ContentBlock block in result.Content)
        {
            if (block is TextContentBlock actual) { int remaining = 16000 - text.Length; if (remaining > 0) { text.Append(actual.Text.AsSpan(0, Math.Min(remaining, actual.Text.Length))); } if (actual.Text.Length > remaining) { omitted++; } }
            else { omitted++; }
        }
        if (result.StructuredContent is not null && text.Length == 0)
        { string structured = JsonSerializer.Serialize(result.StructuredContent); text.Append(structured.AsSpan(0, Math.Min(16000, structured.Length))); if (structured.Length > 16000) { omitted++; } }
        return new(text.ToString(), result.IsError == true, omitted);
    }
    private static async Task DrainAsync(StreamReader reader, CancellationToken token)
    { char[] buffer = new char[1024]; try { while (await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false) > 0) { } } catch (Exception error) when (error is IOException or OperationCanceledException or ObjectDisposedException) { } }
    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) { return; }
        try
        {
            if (_client is not null) { await _client.DisposeAsync().ConfigureAwait(false); }
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or McpException) { }
        finally
        {
            try { _process.StandardInput.Close(); } catch (Exception error) when (error is IOException or InvalidOperationException) { }
            try
            {
                if (!_process.HasExited)
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    try { await _process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) { if (!_process.HasExited) { _process.Kill(entireProcessTree: true); } }
                }
            }
            catch (Exception error) when (error is IOException or InvalidOperationException or global::System.ComponentModel.Win32Exception) { }
            _lifetime.Cancel(); await _stderr.ConfigureAwait(false); _process.Dispose(); _lifetime.Dispose(); GC.SuppressFinalize(this);
        }
    }
}

/// <summary>Bounds each newline-delimited UTF-8 message before the SDK allocates/parses a complete untrusted JSON frame.</summary>
internal sealed class McpMessageReadStream(Stream inner) : Stream
{
    private readonly ArrayBufferWriter<byte> _frame = new();
    private void Validate(ReadOnlySpan<byte> bytes)
    {
        while (!bytes.IsEmpty)
        {
            int newline = bytes.IndexOf((byte)'\n'); int count = newline >= 0 ? newline : bytes.Length;
            if (_frame.WrittenCount + count > 512 * 1024) { throw new IOException("MCP message exceeds 512 KiB."); }
            _frame.Write(bytes[..count]);
            if (newline < 0) { return; }
            try
            {
                using var message = JsonDocument.Parse(_frame.WrittenMemory, new JsonDocumentOptions { MaxDepth = 64 });
                if (message.RootElement.ValueKind != JsonValueKind.Object || !message.RootElement.TryGetProperty("jsonrpc", out JsonElement version) || version.ValueKind != JsonValueKind.String || version.GetString() != "2.0")
                { throw new IOException("MCP stdout is not a JSON-RPC message."); }
            }
            catch (JsonException) { throw new IOException("MCP stdout contains malformed JSON."); }
            _frame.Clear(); bytes = bytes[(newline + 1)..];
        }
    }
    public override int Read(byte[] buffer, int offset, int count) { int read = inner.Read(buffer, offset, count); Validate(buffer.AsSpan(offset, read)); return read; }
    public override int Read(Span<byte> buffer) { int read = inner.Read(buffer); Validate(buffer[..read]); return read; }
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    { int read = await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false); Validate(buffer.Span[..read]); return read; }
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    protected override void Dispose(bool disposing) { if (disposing) { _frame.Clear(); inner.Dispose(); } base.Dispose(disposing); }
}
