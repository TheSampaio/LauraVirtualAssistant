using Laura.Core.Abstractions;

namespace Laura.Core.Conversation;

/// <summary>Guards explicitly enabled Gemini requests and drains work before changing settings or clearing dialogue.</summary>
public sealed class ConversationProviderRouter(ISettingsService settings, IConversationEngine gemini) : IConversationEngine, IDisposable
{
    private readonly SemaphoreSlim _requests = new(1, 1), _resets = new(1, 1);
    private readonly object _gate = new();
    private CancellationTokenSource _lifetime = new();
    private bool _resetting, _disposed;
    private IConversationEngine? Selected => settings.Current.GenerativeAi.Enabled ? gemini : null;
    /// <inheritdoc />
    public bool IsAvailable { get { lock (_gate) { return !_resetting && !_disposed && Selected?.IsAvailable == true; } } }
    /// <inheritdoc />
    public bool SupportsImages => IsAvailable && gemini.SupportsImages;
    /// <inheritdoc />
    public ConversationFailure? LastFailure { get; private set; }
    /// <summary>Gets the sole configured conversation provider without enabling cloud access.</summary>
    public string ActiveProvider => Selected is null ? "unavailable" : "gemini";
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
                IConversationEngine? selected = Selected;
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
            try { await gemini.ResetSessionAsync(clearHistory).ConfigureAwait(false); }
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
