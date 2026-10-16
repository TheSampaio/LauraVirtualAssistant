using Laura.Core.Abstractions;

namespace Laura.Core.Execution;

/// <summary>Tracks bounded, exclusively owned background commands independently of the conversation queue. No output or executable arguments are persisted.</summary>
public sealed class CommandJobs(ICommandExecutor executor) : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, Slot> _jobs = [];
    private readonly CancellationTokenSource _lifetime = new();
    private bool _stopped;
    private Task? _shutdown;
    /// <summary>Reports metadata changes. UI subscribers must dispatch; stream bodies are available only through scoped reads.</summary>
    public event EventHandler? Changed;

    /// <summary>Returns only after native creation, or propagates refusal before reporting a started job. Args: request: Trusted task and structured command; recipient: Actual provider destination; cancellationToken: Admission cancellation only. Returns: Actual job metadata.</summary>
    public async Task<CommandJobSnapshot> StartAsync(CommandExecutionRequest request, string recipient, CancellationToken cancellationToken = default)
    {
        request = (request with { Background = true }).Validated();
        if (request.TaskId == Guid.Empty) { throw new ArgumentException("Background execution requires a native task identity.", nameof(request)); }
        ArgumentNullException.ThrowIfNull(recipient); cancellationToken.ThrowIfCancellationRequested();
        Slot slot;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_stopped, this);
            if (_jobs.Count >= 64 || _jobs.Values.Count(item => !item.Finished) >= 4)
            { throw new InvalidOperationException("Background command retention or active-process limit reached. Stop active jobs or remove finished task records."); }
            slot = new(request.TaskId, recipient, _lifetime.Token); _jobs.Add(slot.Id, slot);
            slot.Work = Task.Run(() => RunAsync(slot, request), CancellationToken.None);
        }
        using var admission = cancellationToken.Register(static state => ((Slot)state!).Cancel(), slot);
        await slot.Started.Task.ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) { return Snapshot(slot, 0, false); }
    }

    /// <summary>Reads at most 32 ordered retained chunks for one native task and its original recipient. Args: taskId: Trusted task; id: Job ID; recipient: Actual destination; after: Last consumed sequence. Returns: Bounded observation; unavailable IDs never attach to a PID.</summary>
    public CommandJobSnapshot Read(Guid taskId, Guid id, string recipient, long after = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(after);
        lock (_gate) { return Snapshot(Find(taskId, id, recipient), after, true); }
    }

    /// <summary>Lists metadata without output for native task navigation. Args: taskId: Trusted owner. Returns: Current process-session jobs only.</summary>
    public IReadOnlyList<CommandJobSnapshot> List(Guid taskId)
    { lock (_gate) { return _jobs.Values.Where(slot => slot.TaskId == taskId).Select(slot => Snapshot(slot, 0, false)).ToArray(); } }

    /// <summary>Reads a bounded page for an explicit native local user gesture. This method must never be exposed as a model tool or transmitted externally. Args: taskId: Native selected task; id: Native selected job; after: Local cursor. Returns: Local-only output.</summary>
    public CommandJobSnapshot ReadForNativeUser(Guid taskId, Guid id, long after = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(after);
        lock (_gate)
        {
            if (!_jobs.TryGetValue(id, out var slot) || slot.TaskId != taskId) { throw new InvalidOperationException("This process-session job is unavailable."); }
            return Snapshot(slot, after, true);
        }
    }

    /// <summary>Cancels an owned job and waits for verified native cleanup. Args: taskId: Trusted owner; id: Native job ID; recipient: Actual destination. Returns: Final bounded observation.</summary>
    public async Task<CommandJobSnapshot> StopAsync(Guid taskId, Guid id, string recipient)
    {
        Slot slot; lock (_gate) { slot = Find(taskId, id, recipient); }
        slot.Cancel(); await slot.Work.ConfigureAwait(false);
        lock (_gate) { return Snapshot(slot, 0, true); }
    }

    /// <summary>Cancels every current job for an explicitly selected native task without touching another task or reopening any PID.</summary>
    public void CancelTask(Guid taskId)
    {
        Slot[] slots; lock (_gate) { slots = _jobs.Values.Where(slot => slot.TaskId == taskId && !slot.Finished).ToArray(); }
        foreach (var slot in slots) { slot.Cancel(); }
    }

    /// <summary>Removes finished in-memory observations after native task deletion. Args: taskId: Trusted task. Raises: InvalidOperationException if a job is still active.</summary>
    public void ForgetTask(Guid taskId)
    {
        lock (_gate)
        {
            var slots = _jobs.Values.Where(slot => slot.TaskId == taskId).ToArray();
            if (slots.Any(slot => !slot.Finished || !slot.Work.IsCompleted)) { throw new InvalidOperationException("Stop active command jobs before deleting their task."); }
            foreach (var slot in slots) { _jobs.Remove(slot.Id); slot.Dispose(); }
        }
    }

    /// <summary>Stops all owned lifetimes at application shutdown and observes their worker completions.</summary>
    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        lock (_gate) { _stopped = true; return new(_shutdown ??= ShutdownAsync(_jobs.Values.ToArray())); }
    }

    /// <summary>Observes worker cleanup once and disposes retained cancellation registrations. Args: slots: Frozen shutdown inventory.</summary>
    private async Task ShutdownAsync(Slot[] slots)
    {
        await _lifetime.CancelAsync().ConfigureAwait(false);
        await Task.WhenAll(slots.Select(slot => slot.Work)).ConfigureAwait(false);
        foreach (var slot in slots) { slot.Dispose(); }
        _lifetime.Dispose();
    }

    /// <summary>Executes through the existing exact approval boundary and retains only bounded observations. Args: slot: Exclusive in-memory state; request: Frozen command.</summary>
    private async Task RunAsync(Slot slot, CommandExecutionRequest request)
    {
        try
        {
            var result = await executor.ExecuteAsync(request, new OutputSink(this, slot), id =>
            {
                if (id <= 0) { throw new InvalidOperationException("Invalid native process creation identity."); }
                lock (_gate)
                {
                    if (slot.ProcessId is not null) { throw new InvalidOperationException("Duplicate native process creation notification."); }
                    slot.ProcessId = id; slot.Status = "running"; slot.Started.TrySetResult();
                }
                Notify();
            }, slot.Token).ConfigureAwait(false);
            if (!slot.Started.Task.IsCompletedSuccessfully) { throw new InvalidOperationException("Command returned without observed native creation."); }
            if (result.ProcessId != slot.ProcessId || result.Status is not ("exited" or "cancelled" or "timeout" or "termination_failed" or "output_incomplete"))
            { throw new InvalidDataException("Command returned an inconsistent process observation."); }
            lock (_gate) { slot.ProcessId = result.ProcessId; slot.ExitCode = result.ExitCode; slot.Status = result.Status; slot.Truncated |= result.Truncated; slot.Duration = result.Duration; }
        }
        catch (OperationCanceledException) { lock (_gate) { slot.Status = "cancelled"; } slot.Started.TrySetCanceled(slot.Token); }
        catch (Exception failure)
        {
            lock (_gate) { slot.Status = failure is Permissions.PermissionDeniedException ? "blocked" : "failed"; }
            slot.Started.TrySetException(failure);
        }
        finally
        {
            // An adapter must report creation or explicitly fail. A final result cannot fabricate an owned running process.
            slot.Started.TrySetException(new InvalidOperationException("Command ended before native creation was observed."));
            Notify();
        }
    }

    /// <summary>Isolates view notification failures from process ownership and cleanup.</summary>
    private void Notify()
    {
        foreach (EventHandler handler in Changed?.GetInvocationList() ?? [])
        {
            try { handler(this, EventArgs.Empty); }
            catch (Exception) { Diagnostics.LauraDiagnostics.Record("process.observer_failure", outcome: "observer"); }
        }
    }

    /// <summary>Checks both native ownership and original disclosure destination. Args: taskId: Trusted owner; id: Job identity; recipient: Actual destination. Returns: Owned slot. Raises: InvalidOperationException for missing/foreign jobs or recipient changes.</summary>
    private Slot Find(Guid taskId, Guid id, string recipient)
    {
        if (!_jobs.TryGetValue(id, out var slot) || slot.TaskId != taskId || !string.Equals(slot.Recipient, recipient, StringComparison.Ordinal))
        { throw new InvalidOperationException("No current owned command matches this task and recipient. Restarted jobs cannot be attached by PID. Output stays bound to its original provider; inspect locally or return to that provider."); }
        return slot;
    }

    /// <summary>Copies a bounded output page with an explicit gap indicator. Args: slot: Locked state; after: Cursor; includeOutput: Whether recipient validation occurred. Returns: Immutable observation.</summary>
    private static CommandJobSnapshot Snapshot(Slot slot, long after, bool includeOutput)
    {
        var output = includeOutput ? slot.Output.Where(chunk => chunk.Sequence > after).Take(32).ToArray() : [];
        return new(slot.Id, slot.TaskId, slot.ProcessId, slot.Status, slot.ExitCode, slot.Created,
            slot.Duration ?? DateTimeOffset.UtcNow - slot.Created, slot.Sequence, output.Length > 0 ? output[^1].Sequence : after,
            includeOutput && slot.Output.Count > 0 && after < slot.Output.Peek().Sequence - 1, slot.Truncated, output);
    }

    /// <summary>Retains a maximum of 256 chunks and 128,000 characters; the executor independently bounds and continues draining the OS streams.</summary>
    private sealed class OutputSink(CommandJobs owner, Slot slot) : IProgress<CommandOutput>
    {
        /// <inheritdoc />
        public void Report(CommandOutput value)
        {
            lock (owner._gate)
            {
                string text = value.Text.Length <= 1024 ? value.Text : value.Text[..1024];
                slot.Truncated |= text.Length != value.Text.Length;
                slot.Output.Enqueue(new(++slot.Sequence, value.StandardError, text)); slot.Characters += text.Length;
                while (slot.Output.Count > 256 || slot.Characters > 128_000) { slot.Characters -= slot.Output.Dequeue().Text.Length; }
            }
        }
    }

    /// <summary>Owns one process-session identity and its cancellation until application shutdown; neither handles nor output are restored from disk.</summary>
    private sealed class Slot(Guid taskId, string recipient, CancellationToken lifetime) : IDisposable
    {
        private readonly object _cancellationGate = new();
        private readonly CancellationTokenSource _cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        private bool _disposed;
        internal readonly Guid Id = Guid.NewGuid(), TaskId = taskId;
        internal readonly string Recipient = recipient;
        internal readonly DateTimeOffset Created = DateTimeOffset.UtcNow;
        internal CancellationToken Token => _cancellation.Token;
        internal readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly Queue<CommandOutput> Output = new();
        internal Task Work = Task.CompletedTask;
        internal string Status = "awaiting_approval";
        internal int? ProcessId, ExitCode;
        internal TimeSpan? Duration;
        internal long Sequence;
        internal int Characters;
        internal bool Truncated;
        internal bool Finished => Status is not ("awaiting_approval" or "running");
        /// <summary>Cancels only this slot and tolerates an admission callback racing finished-record removal.</summary>
        internal void Cancel() { lock (_cancellationGate) { if (!_disposed) { _cancellation.Cancel(); } } }
        /// <inheritdoc />
        public void Dispose() { lock (_cancellationGate) { _disposed = true; _cancellation.Dispose(); } }
    }
}
