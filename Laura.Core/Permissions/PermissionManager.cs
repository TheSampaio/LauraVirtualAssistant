using System.Text.Json;
using Laura.Core.Abstractions;
using Laura.Core.Localization;

namespace Laura.Core.Permissions;

/// <summary>Coordinates local UI decisions and execution without exposing reusable one-shot authorization tokens to tools.</summary>
public sealed class PermissionManager : IDisposable
{
    private sealed record Pending(ApprovalRequest Request, TaskCompletionSource<(ApprovalChoice Choice, bool Folder)> Completion);
    private readonly object _gate = new();
    private readonly PermissionStore _store;
    private readonly Func<PermissionOptions> _options;
    private readonly Func<IEnumerable<string>> _protectedRoots;
    private readonly string? _workspaceRoot;
    private readonly IUiLocalizer? _ui;
    private readonly Dictionary<Guid, Pending> _pending = [];
    private readonly List<PermissionGrant> _grants = [];
    private readonly List<ApprovalAudit> _history = [];
    private readonly CancellationTokenSource _lifetime = new();
    private bool _stopped;
    private long _resetVersion;

    /// <summary>Loads only local persistent grants. An unreadable/corrupt store starts with no grants.</summary>
    public PermissionManager(PermissionStore store, Func<PermissionOptions> options, Func<IEnumerable<string>>? protectedRoots = null, string? workspaceRoot = null, IUiLocalizer? ui = null)
    {
        _store = store; _options = options; _protectedRoots = protectedRoots ?? (() => []);
        _workspaceRoot = workspaceRoot; _ui = ui;
        try { var loaded = store.Load(); _grants.AddRange(loaded.Grants); _history.AddRange(loaded.History); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or PermissionDeniedException or ArgumentException)
        { StorageWarning = "Permission storage unavailable; no grants were restored."; }
    }
    /// <summary>Reports a safe startup storage diagnostic without file contents.</summary>
    public string? StorageWarning { get; private set; }
    /// <summary>Signals UI subscribers; authorization never depends on a model or prompt.</summary>
    public event EventHandler? Changed;
    /// <summary>Gets immutable snapshots of waiting actions.</summary>
    public IReadOnlyList<ApprovalRequest> PendingRequests { get { lock (_gate) { return _pending.Values.Select(item => item.Request).OrderBy(item => item.Created).ToArray(); } } }
    /// <summary>Gets active grants, including current-session approvals.</summary>
    public IReadOnlyList<PermissionGrant> Grants { get { lock (_gate) { return _grants.ToArray(); } } }
    /// <summary>Gets retained payload-free history.</summary>
    public IReadOnlyList<ApprovalAudit> History { get { lock (_gate) { PruneHistory(); return _history.ToArray(); } } }

    /// <summary>Accepts a timely UI decision exactly once; critical actions cannot create remembered grants.</summary>
    public bool Decide(Guid id, ApprovalChoice choice, bool folder = false)
    {
        lock (_gate)
        {
            if (_stopped || !Enum.IsDefined(choice) || !_pending.TryGetValue(id, out var pending) || pending.Request.Expires <= DateTimeOffset.UtcNow) { return false; }
            if (choice is ApprovalChoice.Session or ApprovalChoice.Persistent && !pending.Request.CanRemember) { return false; }
            if (folder && pending.Request.Action.Operation == PermissionOperation.ApplicationLaunch) { return false; }
            return pending.Completion.TrySetResult((choice, folder));
        }
    }
    /// <summary>Cancels a pending action from the approval center; later decisions cannot revive it.</summary>
    public bool Cancel(Guid id) { lock (_gate) { return _pending.TryGetValue(id, out var pending) && pending.Completion.TrySetCanceled(); } }

    /// <summary>Authorizes and revalidates immediately before invoking the actual effect. Revocation applies to every new dispatch.</summary>
    public Task<T> ExecuteAsync<T>(PermissionAction action, Func<CancellationToken, Task<T>> execute, CancellationToken cancellationToken = default)
        => ExecuteAsync(action, execute, null, cancellationToken);
    /// <summary>Also records native backend refusal/nonzero outcomes separately from successful authorization.</summary>
    public async Task<T> ExecuteAsync<T>(PermissionAction action, Func<CancellationToken, Task<T>> execute, Func<T, bool>? succeeded, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action); ArgumentNullException.ThrowIfNull(execute);
        long version;
        lock (_gate) { if (_stopped) { throw new PermissionDeniedException("Application is stopping."); } version = _resetVersion; }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        cancellationToken = linked.Token;
        Guid id = Guid.NewGuid(); PermissionRisk risk = PermissionPolicy.Risk(action);
        ApprovalOutcome outcome = ApprovalOutcome.Blocked; string reason = "Policy rejected the action.";
        Pending? pending = null;
        PermissionGrant? proposedGrant = null;
        bool dispatched = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            PermissionPolicy.ValidateResources(action, _protectedRoots(), _workspaceRoot);
            string[] original = SnapshotResources(action);
            PermissionGrant? grant;
            lock (_gate) { grant = _grants.FirstOrDefault(item => PermissionPolicy.Matches(item, action)); }
            bool nativeConsent = action.UserInitiated && risk <= PermissionRisk.Moderate && action.Operation is PermissionOperation.LocalCapture or PermissionOperation.ScreenshotExport or PermissionOperation.BrainAttachment or PermissionOperation.ClipboardWrite;
            bool automaticPolicy = PermissionPolicy.AllowsAutomatic(action, _options(), _workspaceRoot);
            bool allowed = automaticPolicy || nativeConsent || action.Operation == PermissionOperation.Internal || grant is not null
                && (grant.TaskId != Guid.Empty || _options().Mode == ApprovalMode.Automatic || !grant.DirectoryScope);
            string authorization = nativeConsent ? "native_gesture" : automaticPolicy ? "automatic_policy" : grant?.TaskId != Guid.Empty && grant is not null ? "task_grant" : grant is not null ? "remembered_grant" : action.Operation == PermissionOperation.Internal ? "internal_policy" : "user_approval";
            if (allowed) { outcome = nativeConsent ? ApprovalOutcome.Approved : ApprovalOutcome.Automatic; }
            else
            {
                var options = _options().Sanitized();
                DateTimeOffset now = DateTimeOffset.UtcNow;
                pending = new(new(id, action, risk, now, now.AddSeconds(options.TimeoutSeconds), PermissionPolicy.CanRemember(action)),
                    new(TaskCreationOptions.RunContinuationsAsynchronously));
                lock (_gate)
                {
                    if (_pending.Count >= 32 || _pending.Values.Any(item => item.Request.Action.Fingerprint == action.Fingerprint))
                    { throw new PermissionDeniedException("Duplicate request or approval queue limit reached."); }
                    _pending.Add(id, pending);
                }
                Changed?.Invoke(this, EventArgs.Empty);
                (ApprovalChoice Choice, bool Folder) decision;
                try { decision = await pending.Completion.Task.WaitAsync(TimeSpan.FromSeconds(options.TimeoutSeconds), cancellationToken).ConfigureAwait(false); }
                catch (TimeoutException) { outcome = ApprovalOutcome.Expired; reason = "Approval expired."; throw new PermissionDeniedException(reason); }
                if (decision.Choice == ApprovalChoice.Deny) { outcome = ApprovalOutcome.Denied; reason = "User denied the action."; throw new PermissionDeniedException(reason); }
                outcome = ApprovalOutcome.Approved;
                if (decision.Choice is ApprovalChoice.Session or ApprovalChoice.Persistent)
                {
                    if (!PermissionPolicy.CanRemember(action)) { throw new PermissionDeniedException("This effect cannot be remembered."); }
                    string scope = decision.Folder ? Path.GetDirectoryName(action.Resources[0])! : action.Resources[0];
                    if (decision.Folder && (scope == Path.GetPathRoot(scope) || _protectedRoots().Any(root => PermissionPolicy.Within(root, scope))))
                    { throw new PermissionDeniedException("This directory scope is too broad."); }
                    proposedGrant = new(Guid.NewGuid(), action.Operation, action.Origin, scope, action.Fingerprint, DateTimeOffset.UtcNow,
                        decision.Choice == ApprovalChoice.Persistent, decision.Folder);
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            PermissionPolicy.ValidateResources(action, _protectedRoots(), _workspaceRoot);
            if (!original.SequenceEqual(SnapshotResources(action), StringComparer.Ordinal))
            { throw new PermissionDeniedException("A resource changed while awaiting approval. Request approval again."); }
            Task<T> operation;
            lock (_gate)
            {
                if (_stopped || version != _resetVersion || grant is not null && !_grants.Any(item => item.Id == grant.Id))
                { throw new PermissionDeniedException("Authorization was revoked before dispatch."); }
                if (pending is not null && pending.Request.Expires <= DateTimeOffset.UtcNow)
                { outcome = ApprovalOutcome.Expired; throw new PermissionDeniedException("Approval expired before dispatch."); }
                if (grant is { DirectoryScope: true, TaskId: var scopedTask } && scopedTask == Guid.Empty && _options().Mode != ApprovalMode.Automatic)
                { throw new PermissionDeniedException("Approval mode changed before dispatch."); }
                if (grant is { TaskId: var activeTask } && activeTask != Guid.Empty && !PermissionPolicy.Matches(grant, action)) { throw new PermissionDeniedException("Task scope expired before dispatch."); }
                if (automaticPolicy && !PermissionPolicy.AllowsAutomatic(action, _options(), _workspaceRoot)) { throw new PermissionDeniedException("Automatic policy changed before dispatch."); }
                if (proposedGrant is not null)
                { _grants.Add(proposedGrant); try { Save(); } catch { _grants.Remove(proposedGrant); throw; } }
                cancellationToken.ThrowIfCancellationRequested();
                if (pending is not null && pending.Request.Expires <= DateTimeOffset.UtcNow)
                { outcome = ApprovalOutcome.Expired; throw new PermissionDeniedException("Approval expired before dispatch."); }
                if (pending is not null) { _pending.Remove(id); }
                // Commit dispatch under the authorization gate, then run effects on a worker so native startup cannot block approval UI reads.
                dispatched = true;
                operation = Task.Run(() => execute(cancellationToken), cancellationToken);
            }
            reason = "Authorized by " + authorization + ".";
            Laura.Core.Diagnostics.LauraDiagnostics.Record("permission.authorization", outcome: authorization, interactionId: action.TaskId);
            T result = await operation.ConfigureAwait(false);
            if (succeeded is not null && !succeeded(result)) { outcome = ApprovalOutcome.Failed; reason = "Authorized operation reported failure."; }
            return result;
        }
        catch (OperationCanceledException) { outcome = ApprovalOutcome.Cancelled; reason = "Action cancelled."; throw; }
        catch (PermissionDeniedException error) { reason = error.Message; if (outcome is not (ApprovalOutcome.Denied or ApprovalOutcome.Expired)) { outcome = ApprovalOutcome.Blocked; } throw; }
        catch { outcome = ApprovalOutcome.Failed; reason = "Execution or permission storage failed."; throw; }
        finally
        {
            Laura.Core.Diagnostics.LauraDiagnostics.Record("permission." + action.Operation, amount: (int)risk, outcome: outcome.ToString(),
                interactionId: Laura.Core.Diagnostics.LauraDiagnostics.CurrentInteractionId == Guid.Empty ? id : Laura.Core.Diagnostics.LauraDiagnostics.CurrentInteractionId);
            lock (_gate)
            {
                _pending.Remove(id);
                if (!dispatched && proposedGrant is not null) { _grants.RemoveAll(item => item.Id == proposedGrant.Id); }
                // Internal reads do not flood the audit trail.
                if (action.Operation != PermissionOperation.Internal)
                {
                    _history.Add(new(id, DateTimeOffset.UtcNow, action.Operation, action.Origin, risk, outcome,
                        action.Resources.Length > 0 ? action.Resources[0] : action.Operation.ToString(), reason) { TaskId = action.TaskId });
                    try { Save(); } catch (Exception error) when (error is IOException or UnauthorizedAccessException or PermissionDeniedException)
                    { StorageWarning = "Audit could not be persisted."; }
                }
            }
            if (action.Operation != PermissionOperation.Internal) { Changed?.Invoke(this, EventArgs.Empty); }
        }
    }
    /// <summary>Applies retention after settings are loaded or changed; persistence errors remain visible to the caller.</summary>
    public void RefreshOptions() { lock (_gate) { Save(); } Changed?.Invoke(this, EventArgs.Empty); }
    /// <summary>Revokes one grant before persisting. A failed write leaves this process fail closed.</summary>
    public void Revoke(Guid id) { lock (_gate) { _grants.RemoveAll(item => item.Id == id); Save(); } Changed?.Invoke(this, EventArgs.Empty); }
    /// <summary>Requests one native review of project edits and an optional explicit cloud read recipient. Args: taskId: Trusted active task; directory: Existing project root; destination: Actual provider host. Returns: Revocable session-only grant.</summary>
    public Task<PermissionGrant> AuthorizeWorkspaceAsync(Guid taskId, string directory, string destination, CancellationToken cancellationToken = default)
    {
        if (taskId == Guid.Empty || destination is not ("" or "api.openai.com" or "generativelanguage.googleapis.com"))
        { throw new PermissionDeniedException("A workspace requires an active task and a known recipient."); }
        string root = Path.GetFullPath(directory);
        if (!Directory.Exists(root) || PermissionPolicy.SensitivePath(root) || _protectedRoots().Any(path => path.Length > 0 && PermissionPolicy.Within(path, root)))
        { throw new PermissionDeniedException("Choose an existing project directory outside protected storage and its parents."); }
        var action = new PermissionAction(PermissionOperation.WorkspaceAuthorization, "Workspace task review",
            (_ui?.Get(LocalizationKeys.Permissions.WorkspaceReview) ?? "Authorize project file reads, creation and edits for this task for one hour. Credential and policy files, deletion, processes, network and system changes are excluded.")
                + " " + (destination.Length > 0 ? _ui?.Get(LocalizationKeys.Permissions.WorkspaceCloudReview, destination) ?? "Project text read by this task will be disclosed to " + destination + "."
                    : _ui?.Get(LocalizationKeys.Permissions.WorkspaceLocalReview) ?? "Access remains local."),
            [root], parameters: taskId.ToString("D"), destination: destination, taskId: taskId);
        return ExecuteAsync(action, token =>
        {
            token.ThrowIfCancellationRequested();
            var grant = new PermissionGrant(Guid.NewGuid(), PermissionOperation.WorkspaceAuthorization, "Native tool: file_control", root,
                action.Fingerprint, DateTimeOffset.UtcNow, false, true) { TaskId = taskId, Expires = DateTimeOffset.UtcNow.AddHours(1), Destination = destination };
            lock (_gate) { _grants.Add(grant); }
            return Task.FromResult(grant);
        }, cancellationToken);
    }
    /// <summary>Reviews one task's repository reads, private Brain retention and optional actual provider disclosure. Source code never receives execution authority.</summary>
    public Task<PermissionGrant> AuthorizeRepositoryStudyAsync(Guid taskId, string source, string destination, CancellationToken cancellationToken = default)
    {
        if (taskId == Guid.Empty || destination is not ("" or "api.openai.com" or "generativelanguage.googleapis.com"))
        { throw new PermissionDeniedException("Repository study requires a trusted task and a known recipient."); }
        bool remoteSource = source.StartsWith("https://", StringComparison.Ordinal);
        string scope = remoteSource ? Brain.GitHubRepositoryAddress.Parse(source).Url : Path.GetFullPath(source);
        if (!remoteSource && (!Directory.Exists(scope) || PermissionPolicy.SensitivePath(scope) || _protectedRoots().Any(path => path.Length > 0 && PermissionPolicy.Within(path, scope))))
        { throw new PermissionDeniedException("Choose an existing repository outside protected storage and its parents."); }
        lock (_gate)
        {
            var existing = _grants.FirstOrDefault(grant => grant.Operation == PermissionOperation.RepositoryStudyAuthorization
                && grant.TaskId == taskId && grant.Scope == scope && grant.Destination == destination && grant.Expires > DateTimeOffset.UtcNow && !grant.Persistent);
            if (existing is not null) { return Task.FromResult(existing); }
        }
        var action = new PermissionAction(PermissionOperation.RepositoryStudyAuthorization, "Repository study review",
            (_ui?.Get(LocalizationKeys.Permissions.RepositoryStudyReview) ?? "Authorize bounded repository text reads and untrusted Brain references for this task for one hour. No source code, dependency or script will execute.")
                + "\n" + scope + "\n" + (destination.Length > 0 ? _ui?.Get(LocalizationKeys.Permissions.WorkspaceCloudReview, destination) ?? "Selected source text will be disclosed to " + destination : _ui?.Get(LocalizationKeys.Permissions.WorkspaceLocalReview) ?? "Access remains local."),
            remoteSource ? [] : [scope], parameters: scope, destination: destination, taskId: taskId, scopeIdentity: scope);
        return ExecuteAsync(action, token =>
        {
            token.ThrowIfCancellationRequested();
            var grant = new PermissionGrant(Guid.NewGuid(), PermissionOperation.RepositoryStudyAuthorization, "Repository study", scope,
                action.Fingerprint, DateTimeOffset.UtcNow, false, !remoteSource) { TaskId = taskId, Expires = DateTimeOffset.UtcNow.AddHours(1), Destination = destination };
            lock (_gate) { _grants.Add(grant); } return Task.FromResult(grant);
        }, cancellationToken);
    }

    /// <summary>Clears all grants and cancels pending operations; settings reset is performed by the local UI.</summary>
    public void Reset()
    {
        lock (_gate) { _resetVersion++; _grants.Clear(); foreach (var item in _pending.Values) { item.Completion.TrySetCanceled(); } Save(); }
        Changed?.Invoke(this, EventArgs.Empty);
    }
    /// <summary>Clears retained audit data without changing grants.</summary>
    public void ClearHistory() { lock (_gate) { _history.Clear(); Save(); } Changed?.Invoke(this, EventArgs.Empty); }
    private void PruneHistory()
    {
        int days = _options().Sanitized().HistoryDays;
        _history.RemoveAll(item => days == 0 || item.Time < DateTimeOffset.UtcNow.AddDays(-days));
        if (_history.Count > 2000) { _history.RemoveRange(0, _history.Count - 2000); }
    }
    private void Save() { PruneHistory(); _store.Save(new(_grants.Where(item => item.Persistent).ToArray(), _history.ToArray())); }
    private static string[] SnapshotResources(PermissionAction action) => action.Resources.Select(path =>
    {
        if (File.Exists(path)) { var file = new FileInfo(path); return $"{file.Length}:{file.LastWriteTimeUtc.Ticks}:{file.CreationTimeUtc.Ticks}"; }
        return Directory.Exists(path) ? "directory:" + Directory.GetCreationTimeUtc(path).Ticks : "absent";
    }).ToArray();
    /// <summary>Cancels waiting work on application shutdown. Never saves session grants.</summary>
    public void Stop() { lock (_gate) { if (_stopped) { return; } _stopped = true; _resetVersion++; _grants.RemoveAll(item => !item.Persistent); _lifetime.Cancel(); foreach (var item in _pending.Values) { item.Completion.TrySetCanceled(); } } }
    /// <inheritdoc />
    public void Dispose() { Stop(); _lifetime.Dispose(); }
}
