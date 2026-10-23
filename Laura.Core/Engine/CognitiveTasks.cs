using System.Collections.Concurrent;
using System.Text.Json;
using Laura.Core.Abstractions;
using Laura.Core.Conversation;
using Laura.Core.Configuration;

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
    /// <summary>Gets the latest observed live checkpoint without disk I/O or inferred percentages.</summary>
    public CognitiveTaskCheckpoint? Latest => _snapshots.Values.MaxBy(checkpoint => checkpoint.Updated);

    /// <summary>Admits a new bounded task cycle before inference, preserving task identity, budgets and effect receipts.</summary>
    public async Task<bool> BeginCycleAsync(ConversationWorkingContext working, IReadOnlySet<string> registeredTools, CognitiveTaskOptions options, CancellationToken token)
    {
        if (working.TurnId == Guid.Empty || string.IsNullOrWhiteSpace(working.UserText) || working.UserText.Length > 4000 || _deleted.ContainsKey(working.TurnId)) { return false; }
        if (!_slots.ContainsKey(working.TurnId) && _slots.Count >= 200) { return false; }
        options = options.Sanitized(); Slot slot = _slots.GetOrAdd(working.TurnId, _ => new Slot());
        await slot.Gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (slot.Retired || slot.Cancellation.IsCancellationRequested) { return false; }
            var restored = slot.Run is null ? await journal.ReadAsync(working.TurnId, token).ConfigureAwait(false) : null;
            DateTimeOffset lastActivity = _snapshots.GetValueOrDefault(working.TurnId)?.Updated ?? restored?.Updated ?? DateTimeOffset.UtcNow;
            if (lastActivity < DateTimeOffset.UtcNow.AddMinutes(-options.IdleMinutes)) { return false; }
            slot.Run ??= new CognitiveTaskRun(restored?.Objective ?? working.UserText, registeredTools, options.MaximumToolCalls, TimeSpan.FromMinutes(options.ActiveMinutes),
                working.TurnId, journal, restored, Publish);
            var checkpoint = slot.Run.Checkpoint();
            var rounds = checkpoint.Rounds ?? new CognitiveRoundProgress(MaximumCycles: options.MaximumCycles, MaximumActiveMilliseconds: options.ActiveMinutes * 60000L,
                MaximumRecoveries: options.MaximumRecoveries, MaximumResultBytes: options.MaximumResultMegabytes * 1024L * 1024, MaximumProviderCalls: options.MaximumProviderCalls);
            if (!rounds.CanContinue || checkpoint.HasUncertainEffect || checkpoint.State == "blocked") { return false; }
            slot.Run.SetRounds(rounds with { Cycles = rounds.Cycles + 1, Outcome = "running", NeedsContinuation = false });
            slot.Run.SetActive(true); slot.Active = true; slot.RoundActive = true;
            checkpoint = slot.Run.Checkpoint();
            try { await journal.SaveAsync(checkpoint, token).ConfigureAwait(false); }
            catch
            {
                slot.Run.SetActive(false); slot.Active = false; slot.RoundActive = false;
                slot.Run.SetRounds(rounds);
                throw;
            }
            Publish(checkpoint);
            Diagnostics.LauraDiagnostics.Record("task.cycle.start", amount: rounds.Cycles + 1, taskId: working.TurnId); return true;
        }
        finally { slot.Gate.Release(); }
    }

    /// <summary>Commits an ended inference cycle separately from overall objective completion; active time excludes process downtime.</summary>
    public async Task EndCycleAsync(Guid id, long milliseconds, string provider, string model, ConversationFailure? failure, bool canceled = false)
    {
        if (!_slots.TryGetValue(id, out var slot)) { return; }
        await slot.Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (slot.Run?.Checkpoint().Rounds is not { } rounds) { return; }
            bool recovery = failure?.Kind is ConversationFailureKind.Timeout or ConversationFailureKind.ContextExceeded;
            bool timeout = failure?.Kind == ConversationFailureKind.Timeout;
            var before = slot.Run.Checkpoint(); int confirmed = CognitiveTaskRoutingPolicy.ConfirmedProgress(before);
            bool stalled = failure is null && before.Steps.Any(step => step.State == "pending") && confirmed <= rounds.ConfirmedProgress;
            string outcome = canceled ? "canceled" : timeout ? "timeout" : failure is null ? "completed_round" : "failed";
            slot.Run.SetActive(false); slot.Active = false; slot.RoundActive = false;
            if (rounds.ActiveMilliseconds + milliseconds >= rounds.MaximumActiveMilliseconds) { outcome = "budget"; }
            slot.Run.SetRounds(rounds with { ActiveMilliseconds = rounds.ActiveMilliseconds + Math.Max(0, milliseconds), Provider = provider is "chatgpt" or "local" or "gemini" ? provider : "",
                Model = model.Length <= 256 ? model : "", Outcome = outcome, Recoveries = rounds.Recoveries + (recovery ? 1 : 0), LastFailure = failure?.Kind,
                ConfirmedProgress = confirmed, StalledCycles = stalled ? Math.Min(128, rounds.StalledCycles + 1) : 0 });
            var checkpoint = slot.Run.Checkpoint(); if (checkpoint.State == "working") { checkpoint = checkpoint with { State = "paused" }; }
            await journal.SaveAsync(checkpoint, CancellationToken.None).ConfigureAwait(false); Publish(checkpoint);
            Diagnostics.LauraDiagnostics.Record("task.cycle.end", milliseconds, outcome: outcome, taskId: id);
        }
        finally { slot.Gate.Release(); }
    }

    /// <summary>Returns this process's current native task cancellation; no token or grant survives restart.</summary>
    public CancellationToken CycleCancellation(Guid id) => _slots.TryGetValue(id, out var slot) ? slot.Cancellation.Token : _lifetime.Token;

    /// <summary>Commits the actual provider/model and closed routing reason before dispatch so native progress reflects the active inference.</summary>
    public async Task RecordProviderAsync(Guid id, string provider, string model, CognitiveTaskRouting decision, CancellationToken token, Models.TaskDifficulty difficulty = Models.TaskDifficulty.Light)
    {
        if (!_slots.TryGetValue(id, out var slot)) { return; }
        await slot.Gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (slot.Retired || slot.Run?.Checkpoint().Rounds is not { } rounds) { return; }
            slot.Run.SetRounds(rounds with { Provider = provider, Model = model, RoutingReason = decision.Reason, Recovery = decision.Recovery, Difficulty = difficulty });
            var checkpoint = slot.Run.Checkpoint(); await journal.SaveAsync(checkpoint, token).ConfigureAwait(false); Publish(checkpoint);
        }
        finally { slot.Gate.Release(); }
    }

    /// <summary>Releases a successful dialogue-only cycle without treating it as a durable operational objective; retained history is unchanged.</summary>
    public async Task RetireDialogueAsync(Guid id, CancellationToken token)
    {
        if (!_slots.TryGetValue(id, out var slot)) { return; }
        await slot.Gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var checkpoint = slot.Run?.Checkpoint();
            if (slot.Retired || slot.Active || slot.RoundActive || slot.Cancellation.IsCancellationRequested
                || checkpoint is not { Calls: 0, Steps.Count: 0, Rounds: { Outcome: "completed_round", NeedsContinuation: false, Paused: false, Canceled: false } }
                || checkpoint.RepositoryStudies?.Count > 0 || checkpoint.CommandJobs?.Count > 0) { return; }
            await journal.DeleteAsync(id, token).ConfigureAwait(false);
            slot.Retired = true; _slots.TryRemove(id, out _); _snapshots.TryRemove(id, out _);
            // Already queued native UI requests can still hold this slot; do not dispose their synchronization primitives.
        }
        finally { slot.Gate.Release(); }
        try { Changed?.Invoke(this, EventArgs.Empty); } catch (InvalidOperationException) { }
    }

    /// <summary>Admits one provider request under the task's cumulative cross-provider budget before network dispatch.</summary>
    public async Task<bool> AdmitInferenceAsync(Guid id, CancellationToken token)
    {
        if (!_slots.TryGetValue(id, out var slot)) { return false; }
        await slot.Gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (slot.Run?.Checkpoint().Rounds is not { } rounds || rounds.ProviderCalls >= rounds.MaximumProviderCalls || rounds.Paused || rounds.Canceled) { return false; }
            slot.Run.SetRounds(rounds with { ProviderCalls = rounds.ProviderCalls + 1 });
            var checkpoint = slot.Run.Checkpoint(); await journal.SaveAsync(checkpoint, token).ConfigureAwait(false); Publish(checkpoint); return true;
        }
        finally { slot.Gate.Release(); }
    }

    /// <summary>Persists native automatic-continuation intent without saving model prompts or replaying operations.</summary>
    public async Task<bool> SetContinuationAsync(Guid id, bool needed, CancellationToken token)
    {
        if (!_slots.TryGetValue(id, out var slot)) { return false; }
        await slot.Gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var checkpoint = slot.Run?.Checkpoint();
            if (checkpoint?.Rounds is not { } rounds) { return false; }
            bool allowed = needed && rounds.CanContinue && !checkpoint.HasUncertainEffect && checkpoint.State != "blocked" && !slot.Cancellation.IsCancellationRequested;
            slot.Run!.SetRounds(rounds with { NeedsContinuation = needed, Outcome = needed && !allowed ? "budget" : rounds.Outcome });
            checkpoint = slot.Run.Checkpoint("paused"); await journal.SaveAsync(checkpoint, token).ConfigureAwait(false); Publish(checkpoint); return allowed;
        }
        finally { slot.Gate.Release(); }
    }

    /// <summary>Pauses or cancels only one task; no effect is rolled back or repeated and no unrelated task is interrupted.</summary>
    public async Task PauseAsync(Guid id, bool cancel = false)
    {
        Cancel(id); if (!_slots.TryGetValue(id, out var slot)) { return; }
        await slot.Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (slot.Retired) { return; }
            if (slot.Run?.Checkpoint().Rounds is not { } rounds) { return; }
            slot.Run.SetRounds(rounds with { Paused = !cancel, Canceled = cancel });
            var checkpoint = slot.Run.Checkpoint("paused"); await journal.SaveAsync(checkpoint, CancellationToken.None).ConfigureAwait(false); Publish(checkpoint);
        }
        finally { slot.Gate.Release(); }
    }

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
            if (slot.Retired || _deleted.ContainsKey(id)) { return new("This task was removed before dispatch.") { Failed = true, Status = "blocked" }; }
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
            if (!slot.Retired && slot.Run is { } run)
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

    /// <summary>Finds only checkpoints belonging to actual prior owner turns in the supplied conversation. Args: history: Trusted retained messages; currentTurn: Current native interaction; cancellationToken: Query cancellation. Returns: At most three recent linked checkpoints; assistant text cannot introduce identities.</summary>
    public async Task<IReadOnlyList<CognitiveTaskCheckpoint>> RecentForConversationAsync(IReadOnlyList<ConversationMessage> history, Guid currentTurn, CancellationToken cancellationToken = default)
    {
        Guid[] ids = history.Reverse().Where(message => message.Source is ConversationMessageSource.UserTyped or ConversationMessageSource.UserVoice
            && message.TurnId != Guid.Empty && message.TurnId != currentTurn).Select(message => message.TurnId).Distinct().Take(16).ToArray();
        if (ids.Length == 0) { return []; }
        var available = (await ListAsync(cancellationToken).ConfigureAwait(false)).ToDictionary(task => task.Id);
        return ids.Where(available.ContainsKey).Select(id => available[id]).Take(3).ToArray();
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
        if (slot.RoundActive) { throw new InvalidOperationException("Task inference is still active. Pause it before resuming."); }
        if (!await slot.Gate.WaitAsync(0, cancellationToken).ConfigureAwait(false)) { throw new InvalidOperationException("Task is already executing."); }
        try
        {
        var checkpoint = await journal.ReadAsync(id, cancellationToken).ConfigureAwait(false) ?? throw new InvalidOperationException("Task checkpoint is unavailable.");
        if (checkpoint.HasUncertainEffect || checkpoint.State == "blocked" && checkpoint.Blocker is not ("budget" or "cancelled" or "repetition"))
        { throw new InvalidOperationException("Task requires outcome review or a new explicit objective; safe resumption is unavailable."); }
        int maximumCalls = checkpoint.Calls >= checkpoint.MaximumCalls ? Math.Min(256, checkpoint.MaximumCalls + 32) : checkpoint.MaximumCalls;
        long maximumMilliseconds = checkpoint.ElapsedMilliseconds >= checkpoint.MaximumMilliseconds ? Math.Min(3600000, checkpoint.MaximumMilliseconds + 360000) : checkpoint.MaximumMilliseconds;
        if (checkpoint.Calls >= maximumCalls || checkpoint.ElapsedMilliseconds >= maximumMilliseconds) { throw new InvalidOperationException("Task reached its cumulative recovery budget; start a new explicit objective."); }
        checkpoint = checkpoint with { State = "paused", Updated = DateTimeOffset.UtcNow, Blocker = "", MaximumCalls = maximumCalls, MaximumMilliseconds = maximumMilliseconds,
            Rounds = checkpoint.Rounds is { } rounds ? rounds with { Paused = false, Canceled = false, NeedsContinuation = false, LastFailure = null,
                MaximumCycles = rounds.Cycles >= rounds.MaximumCycles ? Math.Min(128, rounds.MaximumCycles + 16) : rounds.MaximumCycles,
                MaximumProviderCalls = rounds.ProviderCalls >= rounds.MaximumProviderCalls ? Math.Min(256, rounds.MaximumProviderCalls + 32) : rounds.MaximumProviderCalls,
                MaximumActiveMilliseconds = rounds.ActiveMilliseconds >= rounds.MaximumActiveMilliseconds ? Math.Min(3600000, rounds.MaximumActiveMilliseconds + 600000) : rounds.MaximumActiveMilliseconds,
                MaximumRecoveries = rounds.Recoveries > rounds.MaximumRecoveries ? Math.Min(8, rounds.MaximumRecoveries + 2) : rounds.MaximumRecoveries } : null,
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
        internal volatile bool RoundActive;
        internal volatile bool Retired;
        /// <inheritdoc />
        public void Dispose() { Gate.Dispose(); Cancellation.Dispose(); }
    }
}
