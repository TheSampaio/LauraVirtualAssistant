using NAudio.Wave;
using Laura.Core.Conversation;
using Laura.Core.Speech;

namespace Laura.Platform.Windows.Conversation;

/// <summary>Bounded streaming 24 kHz PCM playback on the user's selected device.</summary>
internal sealed class LiveAudioPlayer : IDisposable
{
    private readonly PcmPlaybackBuffer _buffer = new();
    private readonly WaveOutEvent _output;
    private readonly MeteredProvider _meter;
    private readonly object _gate = new();
    private readonly CancellationTokenSource _lifetime = new();
    private Task _reset = Task.CompletedTask;
    private TaskCompletionSource? _stopped;
    private Exception? _playbackError;
    private bool _disposed;
    private long _lastChunk;
    private readonly Func<Guid>? _interaction;
    private long _firstAudioAt;
    private readonly long _createdAt = Environment.TickCount64;
    /// <summary>Gets this sink's correlation identity.</summary>
    public Guid PlaybackId { get; } = Guid.NewGuid();
    /// <summary>Gets the explicitly selected Windows device.</summary>
    public int OutputDevice { get; }
    /// <summary>Receives actual PCM production and output-queue transitions.</summary>
    public Action<SpeechPlaybackState>? PlaybackChanged { get; set; }
    /// <summary>Gets latency to the first received PCM fragment, when one exists.</summary>
    public double? FirstAudioMilliseconds => _firstAudioAt == 0 ? null : _firstAudioAt - _createdAt;
    /// <summary>Gets the output driver's byte position, including priming silence.</summary>
    public long DevicePositionBytes => _output.GetPosition();
    /// <summary>Creates an audio sink without opening a microphone.</summary>
    public LiveAudioPlayer(int device, int volume, Func<Action<byte[]>?>? capture = null, Func<Guid>? interaction = null)
    {
        _interaction = interaction;
        if (device < -1 || device >= WaveInterop.waveOutGetNumDevs()) { throw new InvalidOperationException("The configured playback device is unavailable."); }
        OutputDevice = device;
        _output = new WaveOutEvent { DeviceNumber = device, DesiredLatency = 180, NumberOfBuffers = 3, Volume = volume / 100f };
        _meter = new(_buffer, Environment.TickCount64 + 300, capture, () => PlaybackChanged?.Invoke(SpeechPlaybackState.Playing)); _output.Init(_meter);
        _output.PlaybackStopped += (_, args) => { if (args.Exception is not null) { Volatile.Write(ref _playbackError, args.Exception); } _stopped?.TrySetResult(); };
        _output.Play();
    }
    /// <summary>Gets whether speech remains buffered.</summary>
    public bool IsSpeaking => _buffer.BufferedBytes > 0 || Environment.TickCount64 - _meter.LastAudioRead < 240;
    /// <summary>Gets the amplitude currently being played.</summary>
    public float Level => _meter.Level;
    /// <summary>Gets the total number of PCM bytes accepted by this session.</summary>
    public long BytesReceived { get; private set; }
    /// <summary>Queues a PCM fragment and begins playback.</summary>
    public void Add(byte[] audio)
    {
        if (Volatile.Read(ref _playbackError) is { } error) { throw new InvalidOperationException("The selected playback device failed.", error); }
        long now = Environment.TickCount64, previous = Interlocked.Exchange(ref _lastChunk, now);
        Guid interaction = _interaction?.Invoke() ?? Guid.Empty;
        Laura.Core.Diagnostics.LauraDiagnostics.Record("audio.chunk.arrival", previous == 0 ? 0 : now - previous, audio.Length, interactionId: interaction == Guid.Empty ? null : interaction);
        BytesReceived += audio.Length; _buffer.Add(audio, now, interaction);
        if (audio.Length > 0 && Interlocked.CompareExchange(ref _firstAudioAt, now, 0) == 0) { PlaybackChanged?.Invoke(SpeechPlaybackState.AudioReady); }
    }
    /// <summary>Primes the next utterance without clearing its leading PCM fragments.</summary>
    public void BeginTurn() => _buffer.BeginTurn();
    /// <summary>Flushes short utterances after the model finishes generating.</summary>
    public void CompleteTurn() => _buffer.CompleteTurn();
    /// <summary>Gets the number of real PCM bytes read into the output device queue.</summary>
    public long BytesPlayed => _meter.BytesPlayed;
    /// <summary>Gets content-free queue health counters.</summary>
    public PcmPlaybackMetrics Metrics => _buffer.Metrics;
    /// <summary>Discards sound immediately for interruption.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            if (_disposed) { return; }
            _buffer.Clear(); _meter.Reset();
            Task previous = _reset; _reset = Task.Run(() => ResetOutputAsync(previous));
        }
    }
    /// <summary>Waits for the old WaveOut thread to stop before starting the warm silent stream again.</summary>
    private async Task ResetOutputAsync(Task previous)
    {
        try
        {
            await previous.ConfigureAwait(false);
            Task stopped;
            lock (_gate)
            {
                if (_disposed) { return; }
                _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously); stopped = _stopped.Task;
                if (_output.PlaybackState == PlaybackState.Stopped) { _stopped.TrySetResult(); }
                else { _output.Stop(); }
            }
            await stopped.WaitAsync(TimeSpan.FromSeconds(2), _lifetime.Token).ConfigureAwait(false);
            lock (_gate) { if (!_disposed) { _meter.ResetPosition(); _meter.ReadyAfter = Environment.TickCount64 + 300; _output.Play(); } }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is TimeoutException or NAudio.MmException or ObjectDisposedException)
        { Volatile.Write(ref _playbackError, error); }
    }
    /// <summary>Waits for queued audio and the hardware latency to finish.</summary>
    public async Task DrainAsync(CancellationToken cancellationToken)
    {
        await _reset.WaitAsync(cancellationToken).ConfigureAwait(false);
        while (IsSpeaking || DevicePositionBytes < _meter.LastAudioEndPosition)
        { if (Volatile.Read(ref _playbackError) is { } error) { throw new InvalidOperationException("Playback could not drain.", error); } await Task.Delay(20, cancellationToken).ConfigureAwait(false); }
        if (Volatile.Read(ref _playbackError) is { } finalError) { throw new InvalidOperationException("Playback failed before completion.", finalError); }
    }
    /// <inheritdoc />
    public void Dispose()
    { lock (_gate) { if (_disposed) { return; } _disposed = true; _lifetime.Cancel(); _buffer.Clear(); _output.Stop(); _output.Dispose(); _lifetime.Dispose(); } GC.SuppressFinalize(this); }

    /// <summary>Measures output-device reads, keeping animation synchronized with audible playback.</summary>
    private sealed class MeteredProvider(PcmPlaybackBuffer source, long readyAfter, Func<Action<byte[]>?>? capture, Action firstRead) : IWaveProvider
    {
        private float _level;
        private long _lastAudioRead = long.MinValue / 2;
        private long _bytesPlayed;
        private long _readyAfter = readyAfter;
        private long _outputBytes, _lastAudioEndPosition;
        private int _started;
        /// <summary>Gets the stream position immediately after the last actual PCM frame.</summary>
        public long LastAudioEndPosition => Interlocked.Read(ref _lastAudioEndPosition);
        /// <summary>Resets device-relative position after waveOutReset has completed.</summary>
        public void ResetPosition() { Interlocked.Exchange(ref _outputBytes, 0); Interlocked.Exchange(ref _lastAudioEndPosition, 0); }
        /// <summary>Gets or sets the earliest time real samples may enter a newly restarted device.</summary>
        public long ReadyAfter { get => Interlocked.Read(ref _readyAfter); set => Interlocked.Exchange(ref _readyAfter, value); }
        /// <summary>Gets the actual PCM byte count without counting warm-up silence.</summary>
        public long BytesPlayed => Interlocked.Read(ref _bytesPlayed);
        /// <summary>Gets the thread-safe playback amplitude.</summary>
        public float Level => Volatile.Read(ref _level);
        /// <summary>Gets when nonempty audio last entered the hardware queue.</summary>
        public long LastAudioRead => Interlocked.Read(ref _lastAudioRead);
        /// <inheritdoc />
        public WaveFormat WaveFormat { get; } = new(24000, 16, 1);
        /// <inheritdoc />
        public int Read(byte[] buffer, int offset, int count)
        {
            long now = Environment.TickCount64;
            Span<byte> output = buffer.AsSpan(offset, count); output.Clear();
            int read = now < ReadyAfter ? 0 : source.Read(output, now);
            long start = Interlocked.Add(ref _outputBytes, count) - count;
            Volatile.Write(ref _level, read == 0 ? 0 : AudioLevel.Measure(output[..read]));
            if (read > 0)
            {
                Interlocked.Exchange(ref _lastAudioRead, now); Interlocked.Add(ref _bytesPlayed, read);
                Interlocked.Exchange(ref _lastAudioEndPosition, start + read);
                if (Interlocked.Exchange(ref _started, 1) == 0) { firstRead(); }
                if (capture?.Invoke() is { } observer) { observer(output[..read].ToArray()); }
            }
            return count;
        }
        /// <summary>Clears amplitude and playback-tail tracking after interruption.</summary>
        public void Reset() { Volatile.Write(ref _level, 0); Interlocked.Exchange(ref _lastAudioRead, long.MinValue / 2); }
    }
}
