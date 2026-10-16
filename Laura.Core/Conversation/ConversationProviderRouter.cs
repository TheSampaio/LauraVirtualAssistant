using Laura.Core.Abstractions;

namespace Laura.Core.Conversation;

/// <summary>Routes explicitly selected providers and drains both before changing settings or clearing dialogue.</summary>
public sealed class ConversationProviderRouter(ISettingsService settings, IConversationEngine gemini, IConversationEngine openai, ILocalInferenceEngine? local = null) : IConversationEngine, IDisposable
{
    private readonly SemaphoreSlim _requests = new(1, 1), _resets = new(1, 1);
    private readonly object _gate = new();
    private CancellationTokenSource _lifetime = new();
    private bool _resetting, _disposed;
    private volatile IConversationEngine? _active;
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
    public bool SupportsImages => IsAvailable && (_active is { } active ? active.SupportsImages
        : Selected?.SupportsImages == true || CanRouteImages);
    private bool CanRouteImages => settings.Current.GenerativeAi is { Provider: "auto", AllowCloudFallback: true }
        && gemini.IsAvailable && gemini.SupportsImages;
    /// <inheritdoc />
    public ConversationFailure? LastFailure { get; private set; }
    /// <summary>Gets the actual selected runtime location; automatic mode never silently authorizes paid GPT usage.</summary>
    public string ActiveProvider => ReferenceEquals(_active ?? Selected, local) && local is not null ? "local" : ReferenceEquals(_active ?? Selected, openai) ? "openai" : ReferenceEquals(_active ?? Selected, gemini) ? "gemini" : "unavailable";
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
                    if (CanRouteImages) { selected = gemini; }
                    else { LastFailure = new(ConversationFailureKind.UnsupportedInput); return null; }
                }
                if (selected?.IsAvailable != true) { LastFailure = new(ConversationFailureKind.Unavailable); return null; }
                _active = selected;
                try
                {
                    string? answer = await selected.AskAsync(turn, operation.Token).ConfigureAwait(false);
                    LastFailure = string.IsNullOrWhiteSpace(answer) ? selected.LastFailure ?? new(ConversationFailureKind.Unknown) : null;
                    return answer;
                }
                catch (ConversationProviderException error) { LastFailure = error.Failure; return null; }
                catch (Exception error) when (ReferenceEquals(selected, local) && error is IOException or InvalidOperationException)
                { LastFailure = selected.LastFailure ?? new(ConversationFailureKind.Unavailable); return null; }
            }
            finally { _active = null; _requests.Release(); }
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
