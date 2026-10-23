using Laura.Core.Abstractions;
using Laura.Core.Conversation;
using Laura.Core.Engine;
using Laura.Core.Models;
using Laura.Platform.Windows.Conversation;
using Laura.Platform.Windows.Speech;
using Microsoft.Extensions.Logging;

namespace Laura.App.Infrastructure;

/// <summary>Hands off local clap capture to a short acknowledgment and then continuous conversation.</summary>
public sealed class ClapWakeCoordinator(WindowsClapListener listener, LocalVoiceSession live, IAssistantEngine engine,
    ISettingsService settings, GreetingComposer greeting, ILogger<ClapWakeCoordinator> logger, ConversationProviderRouter router) : IAsyncDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _wake;
    private Task? _worker, _waking;
    private readonly SemaphoreSlim _transitions = new(1, 1);
    private int _busy, _suspensions, _armed;
    private long _retryAfter;
    private long _settingsVersion;
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
                await _transitions.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    if (!CanArm()) { Volatile.Write(ref _armed, 0); await listener.StopAsync().ConfigureAwait(false); continue; }
                    if (Environment.TickCount64 >= _retryAfter) { await listener.StartAsync(token).ConfigureAwait(false); Volatile.Write(ref _armed, listener.IsCapturing ? 1 : 0); }
                }
                catch (Exception error) when (error is NAudio.MmException or InvalidOperationException or IOException)
                { logger.LogWarning("Local clap capture unavailable ({ErrorType}).", error.GetType().Name); _retryAfter = Environment.TickCount64 + 5000; }
                finally { _transitions.Release(); }
            }
        }
        catch (OperationCanceledException) { }
    }
    /// <summary>Schedules wake-up outside the audio callback so closing the recording device cannot deadlock itself.</summary>
    private void OnDoubleClap(object? sender, EventArgs args)
    {
        if (Volatile.Read(ref _armed) == 0 || Volatile.Read(ref _suspensions) != 0 || !settings.Current.ClapWakeEnabled || settings.Current.MicrophoneMuted) { return; }
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) { return; }
        string provider = router.ActiveProvider; long version = Interlocked.Read(ref _settingsVersion);
        var wake = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token); _wake = wake; _waking = Task.Run(() => WakeAsync(wake, provider, version));
    }
    /// <summary>Computes readiness without starting speech capture, model inference or cloud traffic.</summary>
    private bool CanArm(bool ignoreOwnWake = false) => ClapWakePolicy.CanArm(settings.Current, new(router.ActiveProvider, router.IsAvailable,
        live.IsAvailable,
        engine.State == AssistantState.Ready, !ignoreOwnWake && Volatile.Read(ref _busy) != 0
            || live.IsListening || live.IsCaptureStarting || live.IsSpeaking || live.IsThinking, Volatile.Read(ref _suspensions) != 0));

    /// <summary>Closes detection atomically before an explicit listening handoff; disposal allows normal idle reconciliation.</summary>
    public async Task<IDisposable> SuspendAsync()
    {
        Interlocked.Increment(ref _suspensions); Volatile.Write(ref _armed, 0); CancelWake();
        try
        {
            await _transitions.WaitAsync().ConfigureAwait(false);
            try { await listener.StopAsync().ConfigureAwait(false); }
            finally { _transitions.Release(); }
            return new CaptureSuspension(this);
        }
        catch { Interlocked.Decrement(ref _suspensions); throw; }
    }
    /// <summary>Acknowledges the call without collecting news and starts capture only after acknowledgment playback drains.</summary>
    private async Task WakeAsync(CancellationTokenSource wake, string provider, long version)
    {
        using var workflow = wake;
        try
        {
            Volatile.Write(ref _armed, 0); await listener.StopAsync().ConfigureAwait(false);
            if (router.ActiveProvider != provider || Interlocked.Read(ref _settingsVersion) != version || !CanArm(ignoreOwnWake: true)) { return; }
            await engine.SpeakAsync(greeting.ComposeWakeGreeting(), wake.Token).ConfigureAwait(false);
            wake.Token.ThrowIfCancellationRequested();
            if (router.ActiveProvider == provider && Interlocked.Read(ref _settingsVersion) == version && !settings.Current.MicrophoneMuted && settings.Current.ClapWakeEnabled)
            {
                await live.StartListeningAsync(cancellationToken: wake.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { if (!live.IsListening && !live.IsCaptureStarting && !live.IsSpeaking) { live.ReturnToReady(); } }
        catch (Exception error) when (error is InvalidOperationException or IOException or System.Net.WebSockets.WebSocketException or NAudio.MmException)
        { logger.LogWarning("Clap wake conversation failed ({ErrorType}).", error.GetType().Name); live.ReturnToReady(); }
        finally { if (ReferenceEquals(_wake, wake)) { _wake = null; } Interlocked.Exchange(ref _busy, 0); }
    }
    /// <summary>Cancels a pending briefing immediately when local capture permission is revoked or the mic is muted.</summary>
    private void OnSettingsChanged(object? sender, Laura.Core.Configuration.LauraSettings current)
    { Interlocked.Increment(ref _settingsVersion); Volatile.Write(ref _armed, 0); CancelWake(); if (!current.ClapWakeEnabled || current.MicrophoneMuted) { _ = listener.StopAsync(); } }
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
        await listener.StopAsync().ConfigureAwait(false); _lifetime.Dispose(); _transitions.Dispose(); GC.SuppressFinalize(this);
    }
    /// <summary>Releases one explicit handoff once even if the owning UI flow disposes repeatedly.</summary>
    private sealed class CaptureSuspension(ClapWakeCoordinator owner) : IDisposable
    {
        private ClapWakeCoordinator? _owner = owner;
        /// <inheritdoc />
        public void Dispose() { if (Interlocked.Exchange(ref _owner, null) is { } coordinator) { Interlocked.Decrement(ref coordinator._suspensions); } }
    }
}
