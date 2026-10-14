using System.Text.Json;
using Laura.Core.Brain;
using Laura.Core.Mcp;
using Laura.Core.Permissions;

namespace Laura.Core.Tests.Mcp;

/// <summary>Checks execution-boundary policy with an observed fake transport. Native protocol interoperability is tested separately with an actual stdio fixture.</summary>
public sealed class McpRuntimeTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "laura-mcp-" + Guid.NewGuid().ToString("N"));
    private PermissionManager _permissions = null!;
    private McpServerStore _store = null!;
    private McpRuntime _runtime = null!;
    private McpServerDefinition _definition = null!;
    private FakeSession _session = null!;
    private int _starts;
    public async Task InitializeAsync()
    {
        string workspace = Path.Combine(_root, "Workspace"); Directory.CreateDirectory(workspace);
        string executable = Path.Combine(workspace, "Synthetic.exe"); await File.WriteAllBytesAsync(executable, [1, 2, 3]);
        _definition = new("fixture", "Synthetic MCP", executable, [], workspace); _store = new(Path.Combine(_root, "Security", "mcp-servers.json")); _store.Save([_definition]);
        _permissions = new(new(Path.Combine(_root, "Security", "permissions.json")), () => new() { Mode = ApprovalMode.Automatic }, () => [Path.Combine(_root, "Security")]);
        _session = new(); _runtime = new(_store, _permissions, (_, _) => { _starts++; return Task.FromResult<IMcpSession>(_session); });
    }
    private async Task ApproveAsync(ApprovalChoice choice = ApprovalChoice.Once)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (_permissions.PendingRequests.Count == 0) { await Task.Delay(10, timeout.Token); }
        ApprovalRequest request = Assert.Single(_permissions.PendingRequests); Assert.Equal(PermissionRisk.Critical, request.Risk); Assert.False(request.CanRemember);
        Assert.False(_permissions.Decide(request.Id, ApprovalChoice.Persistent)); Assert.True(_permissions.Decide(request.Id, choice));
    }
    private async Task ConnectAsync() { Task<McpServerState> task = _runtime.ConnectAsync("fixture"); await ApproveAsync(); Assert.Equal("connected", (await task).State); }
    private static JsonElement Arguments(string value = "synthetic argument") => JsonSerializer.SerializeToElement(new { message = value });
    [Fact]
    public async Task AutomaticModeAndReadOnlyHintsDoNotAuthorizeStartupDiscoveryOrCalls()
    {
        Assert.Empty(_runtime.ToolNames); Assert.False(_runtime.DependencyAvailable("mcp:fixture")); Assert.Equal("not_connected", Assert.Single(_runtime.States()).State);
        Task<McpServerState> denied = _runtime.ConnectAsync("fixture"); Assert.Equal(0, _starts); await ApproveAsync(ApprovalChoice.Deny); await Assert.ThrowsAsync<PermissionDeniedException>(() => denied); Assert.Equal(0, _starts);
        await ConnectAsync(); Assert.Equal(1, _starts); Assert.Contains("mcp.fixture.echo", _runtime.ToolNames); Assert.True(_runtime.DependencyAvailable("mcp:fixture")); Assert.False(_runtime.DependencyAvailable("npm-fiction"));
        Task<IReadOnlyList<McpTool>> discovery = _runtime.DiscoverAsync("fixture", "synthetic.model.invalid"); await ApproveAsync(); Assert.Single(await discovery);
        Task<McpToolResult> call = _runtime.CallAsync("fixture", "echo", Arguments(), "synthetic.model.invalid"); Assert.Equal(0, _session.Calls); await ApproveAsync(); Assert.False((await call).IsError);
        Assert.Equal(1, _session.Calls); Assert.Empty(_permissions.Grants); Assert.DoesNotContain(JsonSerializer.Serialize(_permissions.History), "synthetic argument", StringComparison.Ordinal);
        Task<McpToolResult> next = _runtime.CallAsync("fixture", "echo", Arguments("changed arguments")); await ApproveAsync(ApprovalChoice.Deny); await Assert.ThrowsAsync<PermissionDeniedException>(() => next); Assert.Equal(1, _session.Calls);
    }
    [Fact]
    public async Task DisconnectCancelsPendingApprovalAndCannotBeRevivedAfterReconnect()
    {
        await ConnectAsync(); Task<McpToolResult> waiting = _runtime.CallAsync("fixture", "echo", Arguments());
        Guid pending = Assert.Single(_permissions.PendingRequests).Id; await _runtime.DisconnectAsync("fixture");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting); Assert.False(_permissions.Decide(pending, ApprovalChoice.Once)); Assert.Empty(_runtime.ToolNames); Assert.Equal(0, _session.Calls); Assert.True(_session.Disposed);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _runtime.CallAsync("fixture", "echo", Arguments()));
    }
    [Fact]
    public async Task ChangedConfigurationExecutableAndSchemaCannotDispatchAnApprovedDifferentOperation()
    {
        Task<McpServerState> start = _runtime.ConnectAsync("fixture"); await File.AppendAllTextAsync(_definition.Executable, "changed while awaiting approval"); await ApproveAsync();
        await Assert.ThrowsAsync<PermissionDeniedException>(() => start); Assert.Equal(0, _starts);
        await ConnectAsync(); Task<McpToolResult> call = _runtime.CallAsync("fixture", "echo", Arguments()); _session.Tools = [Tool("changed actual schema")];
        await ApproveAsync(); await Assert.ThrowsAsync<IOException>(() => call); Assert.Equal(0, _session.Calls); Assert.Empty(_runtime.ToolNames);
    }
    [Fact]
    public async Task ConfigChangesAndUnavailableServersDoNotBecomeCapabilitiesOrBlockUnrelatedBrainOperations()
    {
        await ConnectAsync(); _store.Save([_definition with { Arguments = ["--changed"] }]); Assert.Empty(_runtime.ToolNames); Assert.False(_runtime.DependencyAvailable("mcp:fixture"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _runtime.CallAsync("fixture", "echo", Arguments())); Assert.Equal(0, _session.Calls);
        string path = Path.Combine(_root, "Security", "mcp-servers.json"); await File.WriteAllTextAsync(path, "{malformed");
        Assert.Empty(_runtime.ToolNames); Assert.False(_runtime.DependencyAvailable("mcp:fixture")); Assert.Throws<JsonException>(() => _store.Read()); Assert.Equal("{malformed", await File.ReadAllTextAsync(path));
    }
    [Fact]
    public async Task ExternalErrorsAreAuditedAsFailuresAndNeverAutomaticallyRetried()
    {
        await ConnectAsync(); _session.Error = true; Task<McpToolResult> failed = _runtime.CallAsync("fixture", "echo", Arguments()); await ApproveAsync(); Assert.True((await failed).IsError);
        Assert.Equal(1, _session.Calls); Assert.Equal(ApprovalOutcome.Failed, _permissions.History.Last().Outcome);
        _session.Throw = true; Task<McpToolResult> protocol = _runtime.CallAsync("fixture", "echo", Arguments()); await ApproveAsync(); await Assert.ThrowsAsync<IOException>(() => protocol);
        Assert.Equal(2, _session.Calls); Assert.True(_session.Disposed); Assert.Empty(_runtime.ToolNames);
    }
    [Fact]
    public async Task TransportLossIsAFailureRatherThanClaimedUserCancellation()
    {
        await ConnectAsync(); _session.CrashCancel = true; Task<McpToolResult> call = _runtime.CallAsync("fixture", "echo", Arguments()); await ApproveAsync();
        await Assert.ThrowsAsync<IOException>(() => call); Assert.Equal(ApprovalOutcome.Failed, _permissions.History.Last().Outcome); Assert.Empty(_runtime.ToolNames);
    }
    [Fact]
    public async Task ShutdownCancelsAnInFlightCallAndRemovesCapabilitiesWithoutLateReexecution()
    {
        await ConnectAsync(); _session.Wait = true; Task<McpToolResult> call = _runtime.CallAsync("fixture", "echo", Arguments()); await ApproveAsync();
        await _session.Started.Task.WaitAsync(TimeSpan.FromSeconds(5)); await _runtime.DisposeAsync(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
        Assert.Empty(_runtime.ToolNames); Assert.Equal(1, _session.Calls); Assert.True(_session.Disposed);
    }
    [Fact]
    public async Task CancellationDuringStartupDisposesALateTransportInsteadOfInstallingIt()
    {
        await _runtime.DisposeAsync(); var returned = new TaskCompletionSource<IMcpSession>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _runtime = new(_store, _permissions, (_, _) => { started.SetResult(); return returned.Task; });
        Task<McpServerState> task = _runtime.ConnectAsync("fixture"); await ApproveAsync(); await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await _runtime.DisconnectAsync("fixture"); returned.SetResult(_session); await Assert.ThrowsAsync<IOException>(() => task);
        Assert.True(_session.Disposed); Assert.Empty(_runtime.ToolNames);
    }
    [Fact]
    public async Task ConfigurationRejectsUnknownIdsDuplicatesNetworkScriptsAndOversizedArgumentsWithoutExecution()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _runtime.ConnectAsync("invented"));
        Assert.Throws<ArgumentException>(() => _store.Save([_definition, _definition]));
        Assert.Throws<ArgumentException>(() => _store.Save([_definition with { Executable = "\\\\synthetic.invalid\\app.exe" }]));
        Assert.Throws<ArgumentException>(() => _store.Save([_definition with { Arguments = [new string('x', 2001)] }]));
        Assert.Throws<IOException>(() => McpLaunch.Prepare(_definition with { Arguments = ["relative-server.js"] })); Assert.Equal(0, _starts);
        await ConnectAsync(); await Assert.ThrowsAsync<ArgumentException>(() => _runtime.CallAsync("fixture", "invented", Arguments())); Assert.Empty(_permissions.PendingRequests);
    }
    private static McpTool Tool(string identity) => new("echo", "Synthetic server says read-only; this hint grants no permissions.", JsonSerializer.SerializeToElement(new { type = "object" }), FileBrainStorage.Hash(identity));
    private sealed class FakeSession : IMcpSession
    {
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Disposed { get; private set; }
        public bool IsConnected => !Disposed;
        public Task Completion => _completion.Task;
        public string Protocol => "synthetic-contract";
        public IReadOnlyList<McpTool> Tools { get; set; } = [Tool("original")];
        public int Calls { get; private set; }
        public bool Error { get; set; }
        public bool Throw { get; set; }
        public bool Wait { get; set; }
        public bool CrashCancel { get; set; }
        public Task<IReadOnlyList<McpTool>> ListToolsAsync(CancellationToken cancellationToken) => Task.FromResult(Tools);
        public async Task<McpToolResult> CallAsync(string tool, JsonElement arguments, CancellationToken cancellationToken)
        { Calls++; Started.TrySetResult(); if (CrashCancel) { _completion.TrySetResult(); await Task.Delay(Timeout.Infinite, cancellationToken); } if (Wait) { await Task.Delay(Timeout.Infinite, cancellationToken); } if (Throw) { throw new IOException("Synthetic protocol failure"); } return new("Synthetic observed result", Error); }
        public ValueTask DisposeAsync() { Disposed = true; _completion.TrySetResult(); return ValueTask.CompletedTask; }
    }
    public async Task DisposeAsync() { await _runtime.DisposeAsync(); _permissions.Dispose(); if (Directory.Exists(_root)) { Directory.Delete(_root, true); } }
}
