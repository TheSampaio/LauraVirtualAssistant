using Laura.Core.Abstractions;

namespace Laura.Core.Conversation;

/// <summary>Routes explicitly selected providers and drains both before changing settings or clearing dialogue.</summary>
public sealed class ConversationProviderRouter(ISettingsService settings, IConversationEngine gemini, IConversationEngine openai, ILocalInferenceEngine? local = null) : IConversationEngine, IDisposable
{
    private readonly SemaphoreSlim _requests = new(1, 1), _resets = new(1, 1);
    private readonly object _gate = new();
    private CancellationTokenSource _lifetime = new();
    private bool _resetting, _disposed;
    private IConversationEngine? Selected => settings.Current.GenerativeAi.Provider switch
    {
        "openai" => openai,
        "gemini" => gemini,
        "local" => local,
        "auto" when local?.IsAvailable == true => local,
        "auto" when settings.Current.GenerativeAi.AllowCloudFallback => gemini,
        _ => null,
    };
    /// <inheritdoc />
    public bool IsAvailable { get { lock (_gate) { return !_resetting && !_disposed && Selected?.IsAvailable == true; } } }
    /// <inheritdoc />
    public bool SupportsImages => IsAvailable && Selected?.SupportsImages == true;
    /// <summary>Gets the actual selected runtime location; automatic mode never silently authorizes paid GPT usage.</summary>
    public string ActiveProvider => ReferenceEquals(Selected, local) && local is not null ? "local" : ReferenceEquals(Selected, openai) ? "openai" : ReferenceEquals(Selected, gemini) ? "gemini" : "unavailable";
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
            try { operation.Token.ThrowIfCancellationRequested(); return Selected is { } selected ? await selected.AskAsync(turn, operation.Token).ConfigureAwait(false) : null; }
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
            try { await gemini.ResetSessionAsync(() => openai.ResetSessionAsync(async () =>
                { if (local is not null) { await local.ResetSessionAsync().ConfigureAwait(false); } await clearHistory().ConfigureAwait(false); })).ConfigureAwait(false); }
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
