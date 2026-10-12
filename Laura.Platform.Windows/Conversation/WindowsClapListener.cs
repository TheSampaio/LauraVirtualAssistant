using Laura.Core.Abstractions;
using Laura.Core.Conversation;
using Microsoft.Extensions.Logging;
using NAudio.Wave;

namespace Laura.Platform.Windows.Conversation;

/// <summary>Captures short microphone frames exclusively for explicitly enabled local clap detection.</summary>
public sealed class WindowsClapListener(ISettingsService settings, ILogger<WindowsClapListener> logger) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private WaveInEvent? _microphone;
    private int _device, _sensitivity;
    /// <summary>Reports one recognized double clap without publishing raw microphone data.</summary>
    public event EventHandler? DoubleClap;
    /// <summary>Reports actual local capture transitions for privacy feedback.</summary>
    public event EventHandler? CaptureChanged;
    /// <summary>Gets whether a local microphone stream is open.</summary>
    public bool IsCapturing => Volatile.Read(ref _microphone) is not null;
    /// <summary>Starts only after explicit opt-in and when the global microphone setting is unmuted.</summary>
    public async Task StartAsync(CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var current = settings.Current;
            if (!current.ClapWakeEnabled || current.MicrophoneMuted) { return; }
            if (_microphone is not null && _device == current.InputDevice && _sensitivity == current.ClapWakeSensitivity) { return; }
            await CloseAsync().ConfigureAwait(false);
            var detector = new DoubleClapDetector(current.ClapWakeSensitivity);
            var microphone = new WaveInEvent { DeviceNumber = current.InputDevice, WaveFormat = new WaveFormat(16000, 16, 1), BufferMilliseconds = 20 };
            microphone.DataAvailable += (_, args) =>
            { if (detector.Process(args.Buffer.AsSpan(0, args.BytesRecorded), Environment.TickCount64)) { DoubleClap?.Invoke(this, EventArgs.Empty); } };
            microphone.RecordingStopped += (_, args) =>
            { if (args.Exception is not null) { logger.LogWarning("Local clap microphone stopped ({ErrorType}).", args.Exception.GetType().Name); _ = StopAsync(); } };
            try { token.ThrowIfCancellationRequested(); microphone.StartRecording(); }
            catch { microphone.Dispose(); throw; }
            _device = current.InputDevice; _sensitivity = current.ClapWakeSensitivity; Volatile.Write(ref _microphone, microphone);
            CaptureChanged?.Invoke(this, EventArgs.Empty);
        }
        finally { _gate.Release(); }
    }
    /// <summary>Closes local capture before cloud listening, muting, disabling or shutdown.</summary>
    public async Task StopAsync()
    { await _gate.WaitAsync().ConfigureAwait(false); try { await CloseAsync().ConfigureAwait(false); } finally { _gate.Release(); } }
    /// <summary>Releases the device off the UI and microphone callback threads.</summary>
    private async Task CloseAsync()
    {
        WaveInEvent? microphone = Interlocked.Exchange(ref _microphone, null);
        if (microphone is null) { return; }
        await Task.Run(() => { microphone.StopRecording(); microphone.Dispose(); }).ConfigureAwait(false);
        logger.LogDebug("Local clap microphone capture closed."); CaptureChanged?.Invoke(this, EventArgs.Empty);
    }
    /// <inheritdoc />
    public async ValueTask DisposeAsync() { await StopAsync().ConfigureAwait(false); _gate.Dispose(); GC.SuppressFinalize(this); }
}
