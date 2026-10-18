using Laura.Core.Permissions;

namespace Laura.Core.Tests.Permissions;

/// <summary>Exercises native reviewed task scopes without trusting repository text or model policy fields.</summary>
public sealed class WorkspacePermissionTests : IDisposable
{
    [Fact]
    /// <summary>Ensures synchronous effect startup cannot delay dispatch or block approval snapshots.</summary>
    public async Task SynchronousNativeStartupCannotHoldTheApprovalUiSnapshotGate()
    {
        Directory.CreateDirectory(_root);
        using var manager = new PermissionManager(new PermissionStore(Path.Combine(_root, "permissions.json")), () => new());
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var dispatchTime = System.Diagnostics.Stopwatch.StartNew();
        Task<int> effect = manager.ExecuteAsync(new(PermissionOperation.Internal, "Synthetic native startup", "No OS effect"), _ =>
        { entered.Set(); release.Wait(TimeSpan.FromSeconds(10)); return Task.FromResult(1); });
        try
        {
            Assert.True(dispatchTime.Elapsed < TimeSpan.FromSeconds(3), "Dispatch waited for synchronous native startup.");
            Assert.True(entered.Wait(TimeSpan.FromSeconds(3)));
            var snapshot = await Task.Run(() => manager.PendingRequests).WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Empty(snapshot);
        }
        finally { release.Set(); }
        Assert.Equal(1, await effect);
    }
    private readonly string _root = Path.Combine(Path.GetTempPath(), "laura-task-permissions-" + Guid.NewGuid().ToString("N"));

    /// <summary>Rejects UNC scope reviews before source inventory or filesystem metadata can contact another host.</summary>
    [Fact]
    public async Task NetworkTaskScopesCannotTriggerMetadataOrCreatePendingAuthority()
    {
        Directory.CreateDirectory(_root); using var manager = new PermissionManager(new PermissionStore(Path.Combine(_root, "permissions.json")), () => new());
        const string source = @"\\example.invalid\unreachable\Project";
        await Assert.ThrowsAsync<PermissionDeniedException>(() => manager.AuthorizeWorkspaceAsync(Guid.NewGuid(), source, ""));
        await Assert.ThrowsAsync<PermissionDeniedException>(() => manager.AuthorizeRepositoryStudyAsync(Guid.NewGuid(), source, ""));
        Assert.Empty(manager.PendingRequests); Assert.Empty(manager.Grants);
    }

    /// <summary>Rejects a broken ancestor junction before touching its nonexistent target or considering a child resource safe.</summary>
    [Fact]
    public async Task BrokenJunctionAncestorCannotBecomeAnApprovedNewFileTarget()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        Directory.CreateDirectory(_root); string link = Path.Combine(_root, "BrokenLink"), target = Path.Combine(_root, "MissingTarget");
        var start = new System.Diagnostics.ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in new[] { "/c", "mklink", "/J", link, target }) { start.ArgumentList.Add(argument); }
        using var process = System.Diagnostics.Process.Start(start)!; await process.WaitForExitAsync(); Assert.Equal(0, process.ExitCode);
        var action = new PermissionAction(PermissionOperation.FileWrite, "Synthetic native adapter", "Create harmless fixture", [Path.Combine(link, "new.txt")]);
        Assert.Throws<PermissionDeniedException>(() => PermissionPolicy.ValidateResources(action, [])); Assert.False(Directory.Exists(target));
        Directory.Delete(link);
    }

    /// <summary>Exercises a read-only repository grant across task, root, recipient, operation and revocation boundaries.</summary>
    [Fact]
    public async Task RepositoryStudyReviewCannotAuthorizeEditsCommandsSecretsOtherTasksOrRecipients()
    {
        string root = Path.Combine(_root, "Project"); Directory.CreateDirectory(root); Guid task = Guid.NewGuid();
        using var manager = new PermissionManager(new PermissionStore(Path.Combine(_root, "permissions.json")), () => new());
        Task<PermissionGrant> review = manager.AuthorizeRepositoryStudyAsync(task, root, "generativelanguage.googleapis.com");
        manager.Decide(Assert.Single(manager.PendingRequests).Id, ApprovalChoice.Once); var grant = await review;
        Assert.Equal(grant.Id, (await manager.AuthorizeRepositoryStudyAsync(task, root, "generativelanguage.googleapis.com")).Id); Assert.Single(manager.Grants);
        var read = new PermissionAction(PermissionOperation.RepositoryStudyRead, "Repository study", "Read scoped sources", [Path.Combine(root, "main.cs")], destination: "generativelanguage.googleapis.com", taskId: task, scopeIdentity: root);
        Assert.Equal(1, await manager.ExecuteAsync(read, _ => Task.FromResult(1)));
        foreach (var denied in new[]
        {
            new PermissionAction(PermissionOperation.RepositoryStudyRead, read.Origin, "Other task", read.Resources, destination: read.Destination, taskId: Guid.NewGuid(), scopeIdentity: root),
            new PermissionAction(PermissionOperation.RepositoryStudyRead, read.Origin, "Other recipient", read.Resources, destination: "untrusted.example", taskId: task, scopeIdentity: root),
            new PermissionAction(PermissionOperation.RepositoryStudyRead, read.Origin, "Sibling", [Path.Combine(_root, "ProjectSibling", "main.cs")], destination: read.Destination, taskId: task, scopeIdentity: root),
            new PermissionAction(PermissionOperation.RepositoryStudyRead, read.Origin, "Secret", [Path.Combine(root, ".env")], destination: read.Destination, taskId: task, scopeIdentity: root),
            new PermissionAction(PermissionOperation.FileWrite, read.Origin, "Edit", read.Resources, taskId: task, scopeIdentity: root),
            new PermissionAction(PermissionOperation.Process, read.Origin, "Command", read.Resources, taskId: task, scopeIdentity: root),
        }) { Assert.False(PermissionPolicy.Matches(grant, denied)); }
        manager.Revoke(grant.Id); var pending = manager.ExecuteAsync<int>(read, _ => throw new InvalidOperationException("Revoked study dispatched."));
        manager.Decide(Assert.Single(manager.PendingRequests).Id, ApprovalChoice.Deny); await Assert.ThrowsAsync<PermissionDeniedException>(() => pending);
        using var restarted = new PermissionManager(new PermissionStore(Path.Combine(_root, "permissions.json")), () => new()); Assert.Empty(restarted.Grants);
    }

    [Theory]
    [InlineData(ApprovalMode.Ask)]
    [InlineData(ApprovalMode.Automatic)]
    public async Task ReviewedWorkspaceAllowsProjectEditsOnlyForTheSameTaskAndCanBeRevoked(ApprovalMode mode)
    {
        string project = Path.Combine(_root, "Project"); Directory.CreateDirectory(project);
        using var manager = new PermissionManager(new PermissionStore(Path.Combine(_root, "Security", "permissions.json")), () => new() { Mode = mode });
        Guid task = Guid.NewGuid();
        Task<PermissionGrant> review = manager.AuthorizeWorkspaceAsync(task, project, "");
        Assert.True(manager.Decide(Assert.Single(manager.PendingRequests).Id, ApprovalChoice.Once)); PermissionGrant grant = await review;
        var edit = new PermissionAction(PermissionOperation.FileWrite, "Native tool: file_control", "Edit code", [Path.Combine(project, "main.cs")], taskId: task);
        Assert.Equal(1, await manager.ExecuteAsync(edit, _ => Task.FromResult(1))); Assert.Empty(manager.PendingRequests);
        foreach (var rejected in new[]
        {
            new PermissionAction(PermissionOperation.FileWrite, edit.Origin, "Other task", edit.Resources, taskId: Guid.NewGuid()),
            new PermissionAction(PermissionOperation.FileWrite, edit.Origin, "Other project", [Path.Combine(_root, "ProjectSibling", "main.cs")], taskId: task),
            new PermissionAction(PermissionOperation.FileReadCloud, edit.Origin, "Cloud read", edit.Resources, destination: "generativelanguage.googleapis.com", taskId: task),
            new PermissionAction(PermissionOperation.Process, edit.Origin, "Run build", edit.Resources, taskId: task),
            new PermissionAction(PermissionOperation.FileTrash, edit.Origin, "Delete", edit.Resources, taskId: task),
            new PermissionAction(PermissionOperation.FileWrite, edit.Origin, "Secrets", [Path.Combine(project, ".env")], taskId: task),
            new PermissionAction(PermissionOperation.FileWrite, edit.Origin, "Policy", [Path.Combine(project, "AGENTS.md")], taskId: task),
        })
        {
            Task<int> pending = manager.ExecuteAsync<int>(rejected, _ => throw new InvalidOperationException("Must not dispatch."));
            manager.Decide(Assert.Single(manager.PendingRequests).Id, ApprovalChoice.Deny);
            await Assert.ThrowsAsync<PermissionDeniedException>(() => pending);
        }
        manager.Revoke(grant.Id); Task<int> revoked = manager.ExecuteAsync<int>(edit, _ => throw new InvalidOperationException("Must not dispatch."));
        manager.Decide(Assert.Single(manager.PendingRequests).Id, ApprovalChoice.Deny); await Assert.ThrowsAsync<PermissionDeniedException>(() => revoked);
        using var restarted = new PermissionManager(new PermissionStore(Path.Combine(_root, "Security", "permissions.json")), () => new());
        Assert.Empty(restarted.Grants);
    }

    /// <summary>Task scopes obtained only through Full access expire with that session activation.</summary>
    [Fact]
    public async Task FullAccessTaskScopesAreRemovedWhenTheModeIsTurnedOff()
    {
        string project = Path.Combine(_root, "Project"); Directory.CreateDirectory(project);
        using var manager = new PermissionManager(new PermissionStore(Path.Combine(_root, "Security", "permissions.json")),
            () => new() { Mode = ApprovalMode.FullAccess });
        manager.ActivateFullAccess();
        Guid task = Guid.NewGuid();
        PermissionGrant grant = await manager.AuthorizeWorkspaceAsync(task, project, "");
        Assert.True(grant.FullAccessDerived);
        Assert.Single(manager.Grants);
        manager.DeactivateFullAccess();
        Assert.Empty(manager.Grants);
        var edit = new PermissionAction(PermissionOperation.FileWrite, "Native tool: file_control", "Edit after deactivation", [Path.Combine(project, "main.cs")], taskId: task);
        Task<int> pending = manager.ExecuteAsync<int>(edit, _ => throw new InvalidOperationException("Revoked scope dispatched."));
        manager.Decide(Assert.Single(manager.PendingRequests).Id, ApprovalChoice.Deny);
        await Assert.ThrowsAsync<PermissionDeniedException>(() => pending);
    }

    [Fact]
    public async Task ExplicitProjectDisclosureNeverTransfersToAnotherRecipient()
    {
        Directory.CreateDirectory(_root); Guid task = Guid.NewGuid();
        using var manager = new PermissionManager(new PermissionStore(Path.Combine(_root, "permissions.json")), () => new());
        Task<PermissionGrant> pending = manager.AuthorizeWorkspaceAsync(task, _root, "generativelanguage.googleapis.com");
        ApprovalRequest request = Assert.Single(manager.PendingRequests);
        Assert.Equal(PermissionRisk.Critical, request.Risk); Assert.Equal("generativelanguage.googleapis.com", request.Action.Destination);
        manager.Decide(request.Id, ApprovalChoice.Once); PermissionGrant grant = await pending;
        var read = new PermissionAction(PermissionOperation.FileReadCloud, "Native tool: file_control", "Read code", [Path.Combine(_root, "code.cs")], destination: "generativelanguage.googleapis.com", taskId: task);
        Assert.Equal(1, await manager.ExecuteAsync(read, _ => Task.FromResult(1)));
        Assert.False(PermissionPolicy.Matches(grant, new(PermissionOperation.FileReadCloud, read.Origin, "Other provider", read.Resources, destination: "untrusted.example", taskId: task)));
        Assert.False(PermissionPolicy.Matches(grant with { Expires = DateTimeOffset.UtcNow.AddSeconds(-1) }, read));
        Assert.False(PermissionPolicy.Matches(grant with { Persistent = true }, read));
    }

    [Fact]
    public async Task RootAndProtectedStorageCannotBecomeTaskWorkspaces()
    {
        string storage = Path.Combine(_root, "Security"); Directory.CreateDirectory(storage);
        using var manager = new PermissionManager(new PermissionStore(Path.Combine(storage, "permissions.json")), () => new(), () => [storage]);
        await Assert.ThrowsAsync<PermissionDeniedException>(() => manager.AuthorizeWorkspaceAsync(Guid.NewGuid(), _root, ""));
        await Assert.ThrowsAsync<PermissionDeniedException>(() => manager.AuthorizeWorkspaceAsync(Guid.NewGuid(), Path.GetPathRoot(_root)!, ""));
        Assert.Empty(manager.PendingRequests);
    }

    [Theory]
    [InlineData(PermissionOperation.FileWrite, "note.md", true)]
    [InlineData(PermissionOperation.FileWrite, "code.cs", false)]
    [InlineData(PermissionOperation.FileWrite, ".env", false)]
    [InlineData(PermissionOperation.FileReadLocal, "note.md", true)]
    [InlineData(PermissionOperation.WorkspaceNote, "Notes", true)]
    [InlineData(PermissionOperation.FileReadCloud, "note.md", false)]
    [InlineData(PermissionOperation.FileTrash, "note.md", false)]
    public void AutomaticWorkspacePolicyRemainsBounded(PermissionOperation operation, string name, bool expected)
    {
        var action = new PermissionAction(operation, "Native file adapter", "Workspace operation", [Path.Combine(_root, name)]);
        Assert.Equal(expected, PermissionPolicy.AllowsAutomatic(action, new() { Mode = ApprovalMode.Automatic }, _root));
        Assert.False(PermissionPolicy.AllowsAutomatic(action, new() { Mode = ApprovalMode.Ask }, _root));
        Assert.False(PermissionPolicy.AllowsAutomatic(action, new() { Mode = ApprovalMode.Automatic }, Path.Combine(_root, "Elsewhere")));
    }

    /// <inheritdoc />
    public void Dispose() { if (Directory.Exists(_root)) { Directory.Delete(_root, true); } }
}
