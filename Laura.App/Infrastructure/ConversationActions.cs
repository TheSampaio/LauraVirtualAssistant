using Laura.Core.Abstractions;
using Laura.Core.Conversation;
using Laura.Core.Diagnostics;

namespace Laura.App.Infrastructure;

/// <summary>Serializes transcript exports and confirmed session resets outside the view; providers own remote teardown.</summary>
public sealed class ConversationActions(IAssistantEngine engine, IConversationEngine conversation, IConversationMemory memory, ISettingsService settings) : IDisposable
{
    private readonly SemaphoreSlim _actions = new(1, 1);
    private readonly object _gate = new();
    private readonly List<ConversationMessage> _volatileHistory = [];
    private long _version;
    private int _resetting;
    /// <summary>Loads the full initial retained window off the UI thread, so disabling persistence later does not lose visible session history.</summary>
    public async Task InitializeAsync()
    {
        await _actions.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!settings.Current.RememberConversations) { return; }
            var saved = await Task.Run(memory.ReadAllHistory).ConfigureAwait(false);
            lock (_gate)
            {
                var merged = Merge(saved, _volatileHistory);
                _volatileHistory.Clear(); _volatileHistory.AddRange(merged);
            }
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        { LauraDiagnostics.Record("conversation.history_restore_failed", outcome: error.GetType().Name); }
        finally { _actions.Release(); }
    }
    /// <summary>Gets the current UI generation; dispatchers compare it before showing queued events.</summary>
    public long Version => Interlocked.Read(ref _version);
    /// <summary>Gets whether the old remote session is being drained.</summary>
    public bool IsResetting => Volatile.Read(ref _resetting) != 0;
    /// <summary>Retains in-session visible messages when transcript persistence is disabled.</summary>
    public void Observe(ConversationMessage message)
    { lock (_gate) { if (IsResetting) { return; } _volatileHistory.Add(message); if (_volatileHistory.Count > 2000) { _volatileHistory.RemoveAt(0); } } }
    /// <summary>Drains remote callbacks, cancels queued engine work and clears persisted dialogue; durable knowledge stays independent.</summary>
    public async Task ClearAsync()
    {
        await _actions.WaitAsync().ConfigureAwait(false);
        try
        {
            Interlocked.Exchange(ref _resetting, 1); Interlocked.Increment(ref _version);
            await conversation.ResetSessionAsync(() => engine.ClearConversationAsync()).ConfigureAwait(false);
            lock (_gate) { _volatileHistory.Clear(); }
        }
        finally { Volatile.Write(ref _resetting, 0); _actions.Release(); }
    }
    /// <summary>Snapshots all retained messages, including those not currently rendered, on a background worker.</summary>
    public async Task<IReadOnlyList<ConversationMessage>> SnapshotAsync(CancellationToken token = default)
    {
        await _actions.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (settings.Current.RememberConversations)
            {
                var saved = await Task.Run(memory.ReadAllHistory, token).ConfigureAwait(false);
                lock (_gate) { return Merge(saved, _volatileHistory); }
            }
            lock (_gate) { return _volatileHistory.ToArray(); }
        }
        finally { _actions.Release(); }
    }
    /// <inheritdoc />
    public void Dispose() => _actions.Dispose();
    private static ConversationMessage[] Merge(IReadOnlyList<ConversationMessage> saved, IReadOnlyList<ConversationMessage> current)
    {
        var merged = saved.ToList();
        var remaining = saved.GroupBy(message => message).ToDictionary(group => group.Key, group => group.Count());
        foreach (var message in current)
        { if (remaining.TryGetValue(message, out int count) && count > 0) { remaining[message] = count - 1; } else { merged.Add(message); } }
        return merged.OrderBy(message => message.Timestamp).TakeLast(2000).ToArray();
    }
}
