using Laura.Core.Abstractions;

namespace Laura.Core.Conversation;

/// <summary>Coordinates explicitly started local recognition and queued replies, keeping capture closed during synthesis and inference.</summary>
public sealed class LocalVoiceSession(ISpeechInput input, IAssistantEngine engine, ISettingsService settings,
    ConversationProviderRouter router) : IAsyncDisposable
{
    private readonly SemaphoreSlim _session = new(1, 1);
    private readonly object _gate = new();
    private CancellationTokenSource? _active;
    private int _capturing, _running;
    /// <summary>Gets whether a local microphone is currently requested.</summary>
    public bool IsCapturing => Volatile.Read(ref _capturing) != 0;
    /// <summary>Gets whether the explicitly requested local listening workflow remains active.</summary>
    public bool IsRunning => Volatile.Read(ref _running) != 0;
    /// <summary>Publishes capture transitions; UI subscribers must dispatch to their UI thread.</summary>
    public event EventHandler? Changed;

    /// <summary>Runs local push-to-talk or continuous speech-end capture. Args: continuous: Listen again after reply playback; release: Held-chord release; cancellationToken: Abort the complete interaction. Returns: Completion after microphone and queued work close.</summary>
    public Task StartAsync(bool continuous, CancellationToken release, CancellationToken cancellationToken = default)
    {
        var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (_gate) { _active?.Cancel(); _active = operation; }
        Volatile.Write(ref _running, 1); Changed?.Invoke(this, EventArgs.Empty);
        return Task.Run(async () =>
        {
            using var lifetime = operation; bool entered = false;
            try
            {
                await _session.WaitAsync(operation.Token).ConfigureAwait(false); entered = true;
                do
                {
                    if (settings.Current.MicrophoneMuted || router.ActiveProvider != "local" || !continuous && release.IsCancellationRequested) { break; }
                    Volatile.Write(ref _capturing, 1); Changed?.Invoke(this, EventArgs.Empty);
                    string? text;
                    try { text = await input.RecognizeAsync(settings.Current.Culture, settings.Current.InputDevice, continuous ? CancellationToken.None : release, operation.Token).ConfigureAwait(false); }
                    finally { Volatile.Write(ref _capturing, 0); Changed?.Invoke(this, EventArgs.Empty); }
                    operation.Token.ThrowIfCancellationRequested();
                    if (settings.Current.MicrophoneMuted || router.ActiveProvider != "local") { break; }
                    if (!string.IsNullOrWhiteSpace(text)) { await engine.SubmitVoiceCommandAsync(text, operation.Token).ConfigureAwait(false); }
                } while (continuous && !operation.IsCancellationRequested);
            }
            finally
            {
                lock (_gate)
                { if (ReferenceEquals(_active, operation)) { _active = null; Volatile.Write(ref _capturing, 0); Volatile.Write(ref _running, 0); } }
                if (entered) { _session.Release(); } Changed?.Invoke(this, EventArgs.Empty);
            }
        }, CancellationToken.None);
    }

    /// <summary>Immediately cancels capture, transcription and queued voice work without granting any new microphone access.</summary>
    public void Stop() { lock (_gate) { _active?.Cancel(); } }
    /// <inheritdoc />
    public async ValueTask DisposeAsync() { Stop(); await _session.WaitAsync().ConfigureAwait(false); _session.Release(); }
}
