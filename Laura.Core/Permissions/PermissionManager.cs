using System.Text.Json;

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
    private readonly Dictionary<Guid, Pending> _pending = [];
    private readonly List<PermissionGrant> _grants = [];
    private readonly List<ApprovalAudit> _history = [];
    private readonly CancellationTokenSource _lifetime = new();
    private bool _stopped;
    private long _resetVersion;

    /// <summary>Loads only local persistent grants. An unreadable/corrupt store starts with no grants.</summary>
    public PermissionManager(PermissionStore store, Func<PermissionOptions> options, Func<IEnumerable<string>>? protectedRoots = null, string? workspaceRoot = null)
    {
        _store = store; _options = options; _protectedRoots = protectedRoots ?? (() => []);
        _workspaceRoot = workspaceRoot;
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
            return pending.Completion.TrySetResult((choice, folder));
        }
    }
    /// <summary>Cancels a pending action from the approval center; later decisions cannot revive it.</summary>
    public bool Cancel(Guid id) { lock (_gate) { return _pending.TryGetValue(id, out var pending) && pending.Completion.TrySetCanceled(); } }

    /// <summary>Authorizes and revalidates immediately before invoking the actual effect. Revocation applies to every new dispatch.</summary>
    public async Task<T> ExecuteAsync<T>(PermissionAction action, Func<CancellationToken, Task<T>> execute, CancellationToken cancellationToken = default)
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
            bool allowed = nativeConsent || action.Operation == PermissionOperation.Internal || grant is not null
                && (_options().Mode == ApprovalMode.Automatic || !grant.DirectoryScope);
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
                if (grant is { DirectoryScope: true } && _options().Mode != ApprovalMode.Automatic)
                { throw new PermissionDeniedException("Approval mode changed before dispatch."); }
                if (proposedGrant is not null)
                { _grants.Add(proposedGrant); try { Save(); } catch { _grants.Remove(proposedGrant); throw; } }
                cancellationToken.ThrowIfCancellationRequested();
                if (pending is not null && pending.Request.Expires <= DateTimeOffset.UtcNow)
                { outcome = ApprovalOutcome.Expired; throw new PermissionDeniedException("Approval expired before dispatch."); }
                if (pending is not null) { _pending.Remove(id); }
                // Enter the effect while holding the dispatch gate; concurrent revocation cannot precede a new invocation.
                dispatched = true;
                operation = execute(cancellationToken);
            }
            reason = "Authorized dispatch.";
            return await operation.ConfigureAwait(false);
        }
        catch (OperationCanceledException) { outcome = ApprovalOutcome.Cancelled; reason = "Action cancelled."; throw; }
        catch (PermissionDeniedException error) { reason = error.Message; if (outcome is not (ApprovalOutcome.Denied or ApprovalOutcome.Expired)) { outcome = ApprovalOutcome.Blocked; } throw; }
        catch { outcome = ApprovalOutcome.Failed; reason = "Execution or permission storage failed."; throw; }
        finally
        {
            lock (_gate)
            {
                _pending.Remove(id);
                if (!dispatched && proposedGrant is not null) { _grants.RemoveAll(item => item.Id == proposedGrant.Id); }
                // Internal reads do not flood the audit trail.
                if (action.Operation != PermissionOperation.Internal)
                {
                    _history.Add(new(id, DateTimeOffset.UtcNow, action.Operation, action.Origin, risk, outcome,
                        action.Resources.Length > 0 ? action.Resources[0] : action.Operation.ToString(), reason));
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
