using System.Text.Json;
using Laura.Core.Permissions;

namespace Laura.Core.Mcp;

/// <summary>Explicit, cancelable MCP lifecycle outside the Brain. Every launch and invocation is critical; discovery never installs or starts servers from Markdown.</summary>
public sealed class McpRuntime(McpServerStore store, PermissionManager permissions, Func<McpLaunch, CancellationToken, Task<IMcpSession>> connect) : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _failures = new(StringComparer.Ordinal);
    private bool _disposed;
    private sealed class Entry(McpServerDefinition definition)
    {
        public McpServerDefinition Definition { get; } = definition;
        public CancellationTokenSource Lifetime { get; } = new();
        public SemaphoreSlim Calls { get; } = new(1, 1);
        public IMcpSession? Session { get; set; }
        public IReadOnlyList<McpTool> Tools { get; set; } = [];
        public Guid Id { get; } = Guid.NewGuid();
        public int Closed;
        public int Users;
        public bool Retired;
        public bool TransportFailed;
    }
    /// <summary>Gets the last advertised tools of actual live, still configured connections. Execution refreshes and revalidates the exact tool schema.</summary>
    public IReadOnlySet<string> ToolNames
    {
        get
        {
            try
            {
                IReadOnlyList<McpServerDefinition> configured = store.Read(); lock (_gate)
                { return _entries.Where(pair => Active(pair.Value, configured)).SelectMany(pair => pair.Value.Tools.Select(tool => "mcp." + pair.Key + "." + tool.Name)).ToHashSet(StringComparer.Ordinal); }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { return new HashSet<string>(StringComparer.Ordinal); }
        }
    }
    /// <summary>Resolves only explicitly named live MCP dependencies, not arbitrary dependency strings or claimed installations.</summary>
    public bool DependencyAvailable(string dependency)
    {
        try { return dependency.StartsWith("mcp:", StringComparison.Ordinal) && States().Any(state => state.Id == dependency[4..] && state.State == "connected"); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { return false; }
    }
    /// <summary>Returns actual observable connection states without exposing command arguments.</summary>
    public IReadOnlyList<McpServerState> States()
    {
        IReadOnlyList<McpServerDefinition> configured = store.Read(); lock (_gate)
        {
            return configured.Select(definition =>
            {
                _entries.TryGetValue(definition.Id, out Entry? entry);
                bool active = entry is not null && Active(entry, configured);
                return new McpServerState(definition.Id, definition.Name, active ? "connected" : entry?.Session is null && entry is not null ? "connecting" : entry is not null ? "disconnected" : "not_connected",
                    active ? entry!.Tools.Count : 0, active ? entry!.Session!.Protocol : "", _failures.GetValueOrDefault(definition.Id) ?? "");
            }).ToArray();
        }
    }
    private static bool Active(Entry entry, IReadOnlyList<McpServerDefinition> configured) => !entry.Lifetime.IsCancellationRequested && entry.Session?.IsConnected == true
        && configured.Any(current => current.Id == entry.Definition.Id && McpServerStore.Fingerprint(current) == McpServerStore.Fingerprint(entry.Definition));
    private bool Current(Entry entry) { lock (_gate) { return !_disposed && _entries.GetValueOrDefault(entry.Definition.Id) == entry && Active(entry, store.Read()); } }
    /// <summary>Connects only a native configured command after exact reinforced approval, with no automatic restart or invocation.</summary>
    public async Task<McpServerState> ConnectAsync(string id, CancellationToken cancellationToken = default)
    {
        McpServerDefinition definition = store.Read().SingleOrDefault(item => item.Id == id) ?? throw new ArgumentException("No configured MCP server matched.", nameof(id));
        McpLaunch launch = McpLaunch.Prepare(definition); var entry = new Entry(definition);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_entries.ContainsKey(id)) { throw new InvalidOperationException("Disconnect the existing MCP attempt before reconnecting."); }
            if (_entries.Count >= 4) { throw new InvalidOperationException("At most four MCP connections may be active."); }
            _entries.Add(id, entry); entry.Users++; _failures.Remove(id);
        }
        using var use = new Use(this, entry);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, entry.Lifetime.Token);
        try
        {
            return await permissions.ExecuteAsync(new(PermissionOperation.ExternalTool, "MCP server connection", definition.Name + "\n" + definition.Executable + "\n" + JsonSerializer.Serialize(definition.Arguments)
                + "\n" + definition.Directory + "\nExternal program runs with your Windows account access. This approves startup and local capability discovery only; tools require separate approval.", launch.Resources, launch.Identity), async token =>
            {
                if (!store.Read().Any(current => current.Id == id && McpServerStore.Fingerprint(current) == McpServerStore.Fingerprint(definition))) { throw new IOException("MCP configuration changed after review."); }
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(30));
                IMcpSession session;
                using (launch.Acquire()) { session = await connect(launch, timeout.Token).ConfigureAwait(false); }
                bool assigned; lock (_gate) { assigned = entry.Closed == 0; if (assigned) { entry.Session = session; } }
                if (!assigned) { await session.DisposeAsync().ConfigureAwait(false); throw new IOException("MCP startup was cancelled."); }
                entry.Tools = await session.ListToolsAsync(timeout.Token).ConfigureAwait(false);
                if (!Current(entry)) { throw new IOException("MCP connection no longer matches its approved configuration."); }
                _ = WatchAsync(entry);
                return States().Single(state => state.Id == id);
            }, linked.Token).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            lock (_gate) { _failures[id] = error is OperationCanceledException ? "cancelled_or_timeout" : error is PermissionDeniedException ? "denied" : "connection_failed"; }
            await DisconnectEntryAsync(entry).ConfigureAwait(false); throw;
        }
    }
    /// <summary>Refreshes capability metadata on an existing connection. Cloud disclosure requires a distinct exact approval.</summary>
    public async Task<IReadOnlyList<McpTool>> DiscoverAsync(string id, string destination = "", CancellationToken cancellationToken = default)
    {
        Entry entry = Find(id); using var use = new Use(this, entry); using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, entry.Lifetime.Token);
        return await permissions.ExecuteAsync(new(PermissionOperation.ExternalTool, "MCP capability discovery", entry.Definition.Name + "\nRead external tool descriptions and schemas." + (destination.Length > 0 ? "\nSend this metadata to " + destination : ""),
            parameters: entry.Id.ToString("D") + destination, destination: destination), async token =>
        {
            await entry.Calls.WaitAsync(token).ConfigureAwait(false);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(30));
            try
            {
                if (!Current(entry)) { throw new IOException("MCP connection changed."); }
                IReadOnlyList<McpTool> tools = await entry.Session!.ListToolsAsync(timeout.Token).ConfigureAwait(false);
                if (!Current(entry)) { throw new IOException("MCP connection changed."); }
                lock (_gate) { entry.Tools = tools; } return tools;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && (entry.TransportFailed || timeout.IsCancellationRequested && !entry.Lifetime.IsCancellationRequested))
            { await DisconnectEntryAsync(entry).ConfigureAwait(false); throw new IOException("MCP transport closed during capability discovery."); }
            catch { await DisconnectEntryAsync(entry).ConfigureAwait(false); throw; }
            finally { entry.Calls.Release(); }
        }, linked.Token).ConfigureAwait(false);
    }
    /// <summary>Invokes one exact bounded argument object. Server read-only/risk annotations cannot bypass authorization, and failures are never retried automatically.</summary>
    public async Task<McpToolResult> CallAsync(string id, string toolName, JsonElement arguments, string destination = "", CancellationToken cancellationToken = default)
    {
        if (arguments.ValueKind != JsonValueKind.Object || arguments.GetRawText().Length > 32000) { throw new ArgumentException("MCP arguments require a bounded JSON object.", nameof(arguments)); }
        using var frozen = JsonDocument.Parse(arguments.GetRawText(), new JsonDocumentOptions { MaxDepth = 16 });
        Entry entry = Find(id); using var use = new Use(this, entry); McpTool tool;
        lock (_gate) { tool = entry.Tools.SingleOrDefault(tool => tool.Name == toolName) ?? throw new ArgumentException("No advertised MCP tool matched.", nameof(toolName)); }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, entry.Lifetime.Token);
        return await permissions.ExecuteAsync(new(PermissionOperation.ExternalTool, "MCP tool invocation", entry.Definition.Name + " · " + tool.Name + "\n" + frozen.RootElement.GetRawText()
            + "\nExternal effects are not constrained by advertised read-only hints." + (destination.Length > 0 ? "\nReturn text to " + destination : ""),
            parameters: entry.Id.ToString("D") + tool.Fingerprint + frozen.RootElement.GetRawText(), destination: destination), async token =>
        {
            await entry.Calls.WaitAsync(token).ConfigureAwait(false);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(30));
            try
            {
                if (!Current(entry)) { throw new IOException("MCP connection changed after review."); }
                IReadOnlyList<McpTool> current = await entry.Session!.ListToolsAsync(timeout.Token).ConfigureAwait(false);
                lock (_gate) { entry.Tools = current; }
                if (!Current(entry) || !current.Any(actual => actual.Name == tool.Name && actual.Fingerprint == tool.Fingerprint)) { throw new IOException("MCP capability changed after review; discover and approve the current version."); }
                return await entry.Session.CallAsync(tool.Name, frozen.RootElement.Clone(), timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && (entry.TransportFailed || timeout.IsCancellationRequested && !entry.Lifetime.IsCancellationRequested))
            { await DisconnectEntryAsync(entry).ConfigureAwait(false); throw new IOException("MCP transport closed during invocation."); }
            catch { await DisconnectEntryAsync(entry).ConfigureAwait(false); throw; }
            finally { entry.Calls.Release(); }
        }, succeeded: result => !result.IsError, cancellationToken: linked.Token).ConfigureAwait(false);
    }
    private Entry Find(string id)
    {
        lock (_gate)
        { if (_entries.TryGetValue(id, out Entry? entry) && Active(entry, store.Read())) { entry.Users++; return entry; } }
        throw new InvalidOperationException("MCP server is not connected to its current configuration.");
    }
    /// <summary>Immediately removes availability and cancels pending approvals/calls, then disposes only the owned external connection.</summary>
    public async Task DisconnectAsync(string id)
    {
        Entry? entry; lock (_gate) { entry = _entries.GetValueOrDefault(id); }
        if (entry is not null) { await DisconnectEntryAsync(entry).ConfigureAwait(false); }
    }
    private async Task DisconnectEntryAsync(Entry entry)
    {
        lock (_gate) { if (_entries.GetValueOrDefault(entry.Definition.Id) == entry) { _entries.Remove(entry.Definition.Id); } }
        if (Interlocked.Exchange(ref entry.Closed, 1) != 0) { return; }
        entry.Lifetime.Cancel();
        try { if (entry.Session is not null) { await entry.Session.DisposeAsync().ConfigureAwait(false); } }
        finally { lock (_gate) { entry.Retired = true; Cleanup(entry); } }
    }
    private async Task WatchAsync(Entry entry)
    {
        try
        {
            await entry.Session!.Completion.ConfigureAwait(false);
            lock (_gate) { if (entry.Closed == 0) { entry.TransportFailed = true; _failures[entry.Definition.Id] = "transport_closed"; } }
            await DisconnectEntryAsync(entry).ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or InvalidOperationException) { }
    }
    private static void Cleanup(Entry entry) { if (entry.Retired && entry.Users == 0) { entry.Lifetime.Dispose(); entry.Calls.Dispose(); entry.Retired = false; } }
    private sealed class Use(McpRuntime owner, Entry entry) : IDisposable
    { public void Dispose() { lock (owner._gate) { entry.Users--; Cleanup(entry); } } }
    /// <summary>Updates native user configuration and closes all old connections; saving never starts a process.</summary>
    public async Task ConfigureAsync(IReadOnlyList<McpServerDefinition> definitions)
    { store.Save(definitions); string[] ids; lock (_gate) { ids = _entries.Keys.ToArray(); } foreach (string id in ids) { await DisconnectAsync(id).ConfigureAwait(false); } }
    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    { string[] ids; lock (_gate) { _disposed = true; ids = _entries.Keys.ToArray(); } foreach (string id in ids) { await DisconnectAsync(id).ConfigureAwait(false); } GC.SuppressFinalize(this); }
}
