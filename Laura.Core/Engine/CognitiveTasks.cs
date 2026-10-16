using System.Collections.Concurrent;
using System.Text.Json;
using Laura.Core.Abstractions;
using Laura.Core.Conversation;

namespace Laura.Core.Engine;

/// <summary>Coordinates bounded per-task execution and private checkpoints without serializing unrelated resources.</summary>
public sealed class CognitiveTasks(ICognitiveTaskJournal journal, Execution.CommandJobs? commandJobs = null) : IDisposable
{
    private readonly ConcurrentDictionary<Guid, Slot> _slots = new();
    private readonly ConcurrentDictionary<Guid, CognitiveTaskCheckpoint> _snapshots = new();
    private readonly ConcurrentDictionary<Guid, byte> _deleted = new();
    private readonly CancellationTokenSource _lifetime = new();
    /// <summary>Reports metadata changes; UI subscribers must use their dispatcher.</summary>
    public event EventHandler? Changed;

    /// <summary>Executes in one task's lane and commits checkpoints around adapter dispatch. Args: working: Trusted input identity; registeredTools: Closed tool registry; name: Registered operation; arguments: Structured bounded arguments; execute: Native adapter; cancellationToken: Caller cancellation. Returns: Actual adapter outcome.</summary>
    public async Task<AssistantToolResult> ExecuteAsync(ConversationWorkingContext working, IReadOnlySet<string> registeredTools,
        string name, JsonElement arguments, Func<CognitiveTaskRun, CancellationToken, Task<AssistantToolResult>> execute, CancellationToken cancellationToken, string initiator = "model")
    {
        bool attributed = working.TurnId != Guid.Empty && !string.IsNullOrWhiteSpace(working.UserText);
        if (!attributed && name == "start_program")
        { return new("Background commands require an attributed user objective and native task identity.") { Failed = true, Status = "blocked" }; }
        Guid id = attributed ? working.TurnId : Guid.NewGuid();
        if (_deleted.ContainsKey(id)) { return new("The native user removed this task. Its queued operations cannot recreate it.") { Failed = true, Status = "blocked" }; }
        if (!_slots.ContainsKey(id) && _slots.Count >= 200) { return new("Task retention bound reached. Review retained tasks before creating more work.") { Failed = true, Status = "blocked" }; }
        Slot slot = _slots.GetOrAdd(id, _ => new Slot());
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token, slot.Cancellation.Token);
        await slot.Gate.WaitAsync(operation.Token).ConfigureAwait(false);
        try
        {
            if (_deleted.ContainsKey(id)) { return new("This task was removed before dispatch.") { Failed = true, Status = "blocked" }; }
            if (slot.Run is null)
            {
                var restored = attributed ? await journal.ReadAsync(id, operation.Token).ConfigureAwait(false) : null;
                slot.Run = new(restored?.Objective ?? (attributed ? working.UserText : "Inspect an unattributed native capability."), registeredTools,
                    taskId: id, journal: attributed ? journal : null, restored: restored, checkpointed: Publish);
            }
            slot.Run.SetActive(true); slot.Active = true; Publish(slot.Run.Checkpoint());
            AssistantToolResult result = await slot.Run.ExecuteAsync(name, arguments, token => execute(slot.Run, token), operation.Token, initiator).ConfigureAwait(false);
            return result;
        }
        finally
        {
            if (slot.Run is { } run)
            {
                run.SetActive(false); slot.Active = false;
                var checkpoint = run.Checkpoint();
                if (checkpoint.State == "working") { checkpoint = checkpoint with { State = "paused" }; }
                Publish(checkpoint);
                try { if (attributed) { await journal.SaveAsync(checkpoint, CancellationToken.None).ConfigureAwait(false); } }
                finally { slot.Gate.Release(); }
            }
            else { slot.Gate.Release(); }
            if (!attributed) { _slots.TryRemove(id, out _); _snapshots.TryRemove(id, out _); slot.Dispose(); }
        }
    }

    /// <summary>Reads status independently from a blocked or long-running tool's task gate.</summary>
    public CognitiveTaskCheckpoint? Current(Guid id) => _snapshots.GetValueOrDefault(id);

    /// <summary>Lists recent private durable tasks and live metadata without waiting for active operations.</summary>
    public async Task<IReadOnlyList<CognitiveTaskCheckpoint>> ListAsync(CancellationToken cancellationToken = default)
    {
        var stored = await journal.ListAsync(cancellationToken).ConfigureAwait(false);
        return stored.Concat(_snapshots.Values).GroupBy(checkpoint => checkpoint.Id).Select(group => group.Last()).OrderByDescending(checkpoint => checkpoint.Updated).Take(200).ToArray();
    }

    /// <summary>Cancels the selected task immediately; other tasks and their permissions remain independent.</summary>
    public void Cancel(Guid id)
    { commandJobs?.CancelTask(id); if (_slots.TryGetValue(id, out var slot)) { slot.Cancellation.Cancel(); } }

    /// <summary>Removes an inactive task after native confirmation; no model declaration exposes this operation.</summary>
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _slots.TryGetValue(id, out var slot);
        if (slot is not null && !await slot.Gate.WaitAsync(0, cancellationToken).ConfigureAwait(false)) { throw new InvalidOperationException("Cancel active work before removing its checkpoint."); }
        try
        {
            commandJobs?.ForgetTask(id);
            await journal.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
            _deleted[id] = 0;
            _slots.TryRemove(id, out _); _snapshots.TryRemove(id, out _); Changed?.Invoke(this, EventArgs.Empty);
        }
        finally { slot?.Gate.Release(); }
    }

    /// <summary>Prepares explicit native resumption without resetting budgets or replaying uncertain operations.</summary>
    public async Task<CognitiveTaskCheckpoint> PrepareResumeAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (_deleted.ContainsKey(id)) { throw new InvalidOperationException("This task was removed."); }
        var slot = _slots.GetOrAdd(id, _ => new Slot());
        if (!await slot.Gate.WaitAsync(0, cancellationToken).ConfigureAwait(false)) { throw new InvalidOperationException("Task is already executing."); }
        try
        {
        var checkpoint = await journal.ReadAsync(id, cancellationToken).ConfigureAwait(false) ?? throw new InvalidOperationException("Task checkpoint is unavailable.");
        if (checkpoint.HasUncertainEffect || checkpoint.State == "blocked" && checkpoint.Blocker is not ("budget" or "cancelled"))
        { throw new InvalidOperationException("Task requires outcome review or a new explicit objective; safe resumption is unavailable."); }
        int maximumCalls = checkpoint.Calls >= checkpoint.MaximumCalls ? Math.Min(256, checkpoint.MaximumCalls + 32) : checkpoint.MaximumCalls;
        long maximumMilliseconds = checkpoint.ElapsedMilliseconds >= checkpoint.MaximumMilliseconds ? Math.Min(3600000, checkpoint.MaximumMilliseconds + 360000) : checkpoint.MaximumMilliseconds;
        if (checkpoint.Calls >= maximumCalls || checkpoint.ElapsedMilliseconds >= maximumMilliseconds) { throw new InvalidOperationException("Task reached its cumulative recovery budget; start a new explicit objective."); }
        checkpoint = checkpoint with { State = "paused", Blocker = "", MaximumCalls = maximumCalls, MaximumMilliseconds = maximumMilliseconds,
            Steps = checkpoint.Blocker == "cancelled" ? checkpoint.Steps.Select(step => step.State == "cancelled" ? step with { State = "pending" } : step).ToArray() : checkpoint.Steps };
        await journal.SaveAsync(checkpoint, cancellationToken).ConfigureAwait(false);
        // Recreate the run from committed state; never carry a cancellation token or an execution grant across recovery.
        slot.Run = null; if (slot.Cancellation.IsCancellationRequested) { slot.Cancellation.Dispose(); slot.Cancellation = new(); }
        Publish(checkpoint);
        return checkpoint;
        }
        finally { slot.Gate.Release(); }
    }

    /// <summary>Gets the current native run for source continuation while the caller owns its task lane.</summary>
    public CognitiveTaskRun? Run(Guid id) => _slots.TryGetValue(id, out var slot) ? slot.Run : null;

    /// <summary>Reads continuation state within one task's lane without locking unrelated tools or UI status.</summary>
    public async Task<T?> InspectAsync<T>(Guid id, Func<CognitiveTaskRun, T?> inspect, CancellationToken cancellationToken) where T : class
    {
        if (!_slots.TryGetValue(id, out var slot)) { return null; }
        await slot.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return slot.Run is null ? null : inspect(slot.Run); } finally { slot.Gate.Release(); }
    }

    /// <summary>Publishes immutable metadata; observer failures cannot change execution results.</summary>
    private void Publish(CognitiveTaskCheckpoint checkpoint)
    {
        _snapshots[checkpoint.Id] = checkpoint;
        try { Changed?.Invoke(this, EventArgs.Empty); } catch (InvalidOperationException) { }
    }

    /// <inheritdoc />
    public void Dispose()
    { _lifetime.Cancel(); foreach (var slot in _slots.Values) { slot.Cancellation.Cancel(); } GC.SuppressFinalize(this); }

    /// <summary>Owns one task's queue and cancellation without a global action semaphore.</summary>
    private sealed class Slot : IDisposable
    {
        internal readonly SemaphoreSlim Gate = new(1, 1);
        internal CancellationTokenSource Cancellation = new();
        internal CognitiveTaskRun? Run;
        internal volatile bool Active;
        /// <inheritdoc />
        public void Dispose() { Gate.Dispose(); Cancellation.Dispose(); }
    }
}
