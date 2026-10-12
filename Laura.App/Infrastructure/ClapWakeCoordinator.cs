using Laura.Core.Abstractions;
using Laura.Core.Conversation;
using Laura.Core.Engine;
using Laura.Platform.Windows.Conversation;
using Microsoft.Extensions.Logging;

namespace Laura.App.Infrastructure;

/// <summary>Hands off local clap capture to a short acknowledgment and then continuous conversation.</summary>
public sealed class ClapWakeCoordinator(WindowsClapListener listener, GeminiLiveClient live, IAssistantEngine engine,
    ISettingsService settings, GreetingComposer greeting, ILogger<ClapWakeCoordinator> logger) : IAsyncDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _wake;
    private Task? _worker, _waking;
    private int _busy;
    private long _retryAfter;
    /// <summary>Starts the background lifecycle without opening a microphone unless the user opted in.</summary>
    public void Start()
    {
        if (_worker is not null) { return; }
        listener.DoubleClap += OnDoubleClap; settings.Changed += OnSettingsChanged; live.StandbyRequested += OnStandby;
        live.Interrupted += OnStandby;
        _worker = Task.Run(() => RunAsync(_lifetime.Token));
    }
    /// <summary>Reconciles capture while avoiding speaker echo, concurrent microphone sessions and repeated failed opens.</summary>
    private async Task RunAsync(CancellationToken token)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(200));
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                bool allowed = settings.Current.ClapWakeEnabled && !settings.Current.MicrophoneMuted && Volatile.Read(ref _busy) == 0
                    && !live.IsListening && !live.IsCaptureStarting && !live.IsSpeaking && !live.IsThinking && engine.State == AssistantState.Ready;
                if (!allowed) { await listener.StopAsync().ConfigureAwait(false); continue; }
                if (Environment.TickCount64 < _retryAfter) { continue; }
                try { if (live.IsAvailable) { await listener.StartAsync(token).ConfigureAwait(false); } else { await listener.StopAsync().ConfigureAwait(false); } }
                catch (Exception error) when (error is NAudio.MmException or InvalidOperationException or IOException)
                { logger.LogWarning("Local clap capture unavailable ({ErrorType}).", error.GetType().Name); _retryAfter = Environment.TickCount64 + 5000; }
            }
        }
        catch (OperationCanceledException) { }
    }
    /// <summary>Schedules wake-up outside the audio callback so closing the recording device cannot deadlock itself.</summary>
    private void OnDoubleClap(object? sender, EventArgs args)
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) { return; }
        var wake = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token); _wake = wake; _waking = Task.Run(() => WakeAsync(wake));
    }
    /// <summary>Acknowledges the call without collecting news and starts capture only after acknowledgment playback drains.</summary>
    private async Task WakeAsync(CancellationTokenSource wake)
    {
        using var workflow = wake;
        try
        {
            await listener.StopAsync().ConfigureAwait(false);
            if (!settings.Current.ClapWakeEnabled || settings.Current.MicrophoneMuted) { return; }
            await engine.SpeakAsync(greeting.ComposeWakeGreeting(), wake.Token).ConfigureAwait(false);
            wake.Token.ThrowIfCancellationRequested();
            if (!settings.Current.MicrophoneMuted && settings.Current.ClapWakeEnabled)
            { await live.StartListeningAsync(cancellationToken: wake.Token).ConfigureAwait(false); }
        }
        catch (OperationCanceledException) { if (!live.IsListening && !live.IsCaptureStarting && !live.IsSpeaking) { live.ReturnToReady(); } }
        catch (Exception error) when (error is InvalidOperationException or IOException or System.Net.WebSockets.WebSocketException or NAudio.MmException)
        { logger.LogWarning("Clap wake conversation failed ({ErrorType}).", error.GetType().Name); live.ReturnToReady(); }
        finally { _wake = null; Interlocked.Exchange(ref _busy, 0); }
    }
    /// <summary>Cancels a pending briefing immediately when local capture permission is revoked or the mic is muted.</summary>
    private void OnSettingsChanged(object? sender, Laura.Core.Configuration.LauraSettings current)
    { if (!current.ClapWakeEnabled || current.MicrophoneMuted) { CancelWake(); _ = listener.StopAsync(); } }
    /// <summary>Prevents a standby command from being followed by a pending automatic listening start.</summary>
    private void OnStandby(object? sender, EventArgs args) => CancelWake();
    /// <summary>Cancels the current workflow without retaining a disposed cancellation source.</summary>
    private void CancelWake() { try { _wake?.Cancel(); } catch (ObjectDisposedException) { } }
    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        listener.DoubleClap -= OnDoubleClap; settings.Changed -= OnSettingsChanged; live.StandbyRequested -= OnStandby;
        live.Interrupted -= OnStandby;
        await _lifetime.CancelAsync().ConfigureAwait(false); CancelWake();
        if (_worker is not null) { await _worker.ConfigureAwait(false); } if (_waking is not null) { await _waking.ConfigureAwait(false); }
        await listener.StopAsync().ConfigureAwait(false); _lifetime.Dispose(); GC.SuppressFinalize(this);
    }
}
