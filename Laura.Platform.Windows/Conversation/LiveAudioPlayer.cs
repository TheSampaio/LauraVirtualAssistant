using NAudio.Wave;
using Laura.Core.Conversation;

namespace Laura.Platform.Windows.Conversation;

/// <summary>Bounded streaming 24 kHz PCM playback on the user's selected device.</summary>
internal sealed class LiveAudioPlayer : IDisposable
{
    private readonly BufferedWaveProvider _buffer = new(new WaveFormat(24000, 16, 1))
    { BufferDuration = TimeSpan.FromSeconds(30), DiscardOnBufferOverflow = true, ReadFully = true };
    private readonly WaveOutEvent _output;
    private readonly MeteredProvider _meter;
    /// <summary>Creates an audio sink without opening a microphone.</summary>
    public LiveAudioPlayer(int device, int volume)
    {
        _output = new WaveOutEvent { DeviceNumber = device < WaveInterop.waveOutGetNumDevs() ? device : -1, DesiredLatency = 120, Volume = volume / 100f };
        _meter = new(_buffer); _output.Init(_meter);
    }
    /// <summary>Gets whether speech remains buffered.</summary>
    public bool IsSpeaking => _buffer.BufferedBytes > 0 || Environment.TickCount64 - _meter.LastAudioRead < 150;
    /// <summary>Gets the amplitude currently being played.</summary>
    public float Level => _meter.Level;
    /// <summary>Gets the total number of PCM bytes accepted by this session.</summary>
    public long BytesReceived { get; private set; }
    /// <summary>Queues a PCM fragment and begins playback.</summary>
    public void Add(byte[] audio) { BytesReceived += audio.Length; _buffer.AddSamples(audio, 0, audio.Length); if (_output.PlaybackState != PlaybackState.Playing) { _output.Play(); } }
    /// <summary>Discards sound immediately for interruption.</summary>
    public void Clear() { _output.Stop(); _buffer.ClearBuffer(); _meter.Reset(); }
    /// <summary>Waits for queued audio and the hardware latency to finish.</summary>
    public async Task DrainAsync(CancellationToken cancellationToken)
    {
        while (_buffer.BufferedBytes > 0) { await Task.Delay(30, cancellationToken).ConfigureAwait(false); }
        await Task.Delay(150, cancellationToken).ConfigureAwait(false);
    }
    /// <inheritdoc />
    public void Dispose() { Clear(); _output.Dispose(); }

    /// <summary>Measures output-device reads, keeping animation synchronized with audible playback.</summary>
    private sealed class MeteredProvider(BufferedWaveProvider source) : IWaveProvider
    {
        private float _level;
        private long _lastAudioRead = long.MinValue / 2;
        /// <summary>Gets the thread-safe playback amplitude.</summary>
        public float Level => Volatile.Read(ref _level);
        /// <summary>Gets when nonempty audio last entered the hardware queue.</summary>
        public long LastAudioRead => Interlocked.Read(ref _lastAudioRead);
        /// <inheritdoc />
        public WaveFormat WaveFormat => source.WaveFormat;
        /// <inheritdoc />
        public int Read(byte[] buffer, int offset, int count)
        {
            bool audio = source.BufferedBytes > 0;
            int read = source.Read(buffer, offset, count);
            Volatile.Write(ref _level, AudioLevel.Measure(buffer.AsSpan(offset, read)));
            if (audio) { Interlocked.Exchange(ref _lastAudioRead, Environment.TickCount64); }
            return read;
        }
        /// <summary>Clears amplitude and playback-tail tracking after interruption.</summary>
        public void Reset() { Volatile.Write(ref _level, 0); Interlocked.Exchange(ref _lastAudioRead, long.MinValue / 2); }
    }
}
