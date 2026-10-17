using Laura.Core.Abstractions;
using Laura.Core.Configuration;
using Laura.Core.Diagnostics;

namespace Laura.Core.Conversation;

/// <summary>Guards explicitly enabled Gemini requests and drains work before changing settings or clearing dialogue.</summary>
public sealed class ConversationProviderRouter(ISettingsService settings, IConversationEngine gemini, IConversationEngine? chatGpt = null) : IConversationEngine, IDisposable
{
    private readonly SemaphoreSlim _requests = new(1, 1), _resets = new(1, 1);
    private readonly object _gate = new();
    private CancellationTokenSource _lifetime = new();
    private bool _resetting, _disposed;
    private string _activeProvider = "gemini";
    private IConversationEngine? Selected => settings.Current.ProviderSelection == ConversationProviderSelection.ChatGpt ? chatGpt : settings.Current.GenerativeAi.Enabled ? gemini : null;
    /// <inheritdoc />
    public bool IsAvailable { get { lock (_gate) { return !_resetting && !_disposed && Selected?.IsAvailable == true; } } }
    /// <inheritdoc />
    public bool SupportsImages => IsAvailable && Selected?.SupportsImages == true;
    /// <inheritdoc />
    public ConversationFailure? LastFailure { get; private set; }
    /// <summary>Gets the sole configured conversation provider without enabling cloud access.</summary>
    public string ActiveProvider => Selected is null ? "unavailable" : settings.Current.ProviderSelection == ConversationProviderSelection.ChatGpt ? "chatgpt" : _activeProvider;
    /// <inheritdoc />
    public string? PendingRound(Guid taskId) => _activeProvider == "chatgpt" ? chatGpt?.PendingRound(taskId) : gemini.PendingRound(taskId);
    /// <inheritdoc />
    public async Task<string?> AskAsync(ConversationTurn turn, CancellationToken cancellationToken = default)
    {
        CancellationTokenSource operation;
        lock (_gate)
        {
            if (_resetting || _disposed) { return null; }
            operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        }
        using (operation)
        {
            await _requests.WaitAsync(operation.Token).ConfigureAwait(false);
            try
            {
                operation.Token.ThrowIfCancellationRequested(); LastFailure = null;
                bool automaticChatGpt = settings.Current.ProviderSelection == ConversationProviderSelection.Automatic
                    && settings.Current.ChatGpt is { Enabled: true, AllowAutomatic: true, ShareContext: true, IncludedPlanOnly: true }
                    && turn.TaskDemand is CognitiveTaskDemand.Planning or CognitiveTaskDemand.Analysis or CognitiveTaskDemand.Synthesis
                    && chatGpt?.IsAvailable == true && turn.Image is null;
                IConversationEngine? selected = automaticChatGpt ? chatGpt : Selected;
                _activeProvider = ReferenceEquals(selected, chatGpt) && selected is not null ? "chatgpt" : "gemini";
                LauraDiagnostics.Record("provider.selected", outcome: _activeProvider, taskId: turn.ContinuingTaskTurnId != Guid.Empty ? turn.ContinuingTaskTurnId : turn.TurnId);
                if (turn.Image is not null && selected?.SupportsImages != true)
                {
                    LastFailure = new(ConversationFailureKind.UnsupportedInput); return null;
                }
                if (selected?.IsAvailable != true) { LastFailure = new(ConversationFailureKind.Unavailable); return null; }
                try
                {
                    string? answer = await selected.AskAsync(turn, operation.Token).ConfigureAwait(false);
                    LastFailure = string.IsNullOrWhiteSpace(answer) ? selected.LastFailure ?? new(ConversationFailureKind.Unknown) : null;
                    return answer;
                }
                catch (ConversationProviderException error) { LastFailure = error.Failure; return null; }
            }
            finally { _requests.Release(); }
        }
    }
    /// <inheritdoc />
    public Task ResetSessionAsync() => ResetSessionAsync(static () => Task.CompletedTask);
    /// <inheritdoc />
    public async Task ResetSessionAsync(Func<Task> clearHistory)
    {
        ArgumentNullException.ThrowIfNull(clearHistory);
        await _resets.WaitAsync().ConfigureAwait(false);
        try
        {
            lock (_gate) { ObjectDisposedException.ThrowIf(_disposed, this); _resetting = true; _lifetime.Cancel(); }
            await _requests.WaitAsync().ConfigureAwait(false);
            try
            {
                if (chatGpt is not null) { await chatGpt.ResetSessionAsync().ConfigureAwait(false); }
                await gemini.ResetSessionAsync(clearHistory).ConfigureAwait(false);
            }
            finally
            {
                lock (_gate) { _lifetime.Dispose(); _lifetime = new(); _resetting = false; }
                _requests.Release();
            }
        }
        finally { _resets.Release(); }
    }
    /// <inheritdoc />
    public void Dispose() { lock (_gate) { _disposed = true; _lifetime.Cancel(); } GC.SuppressFinalize(this); }
}
