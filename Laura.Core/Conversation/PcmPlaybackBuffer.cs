namespace Laura.Core.Conversation;

/// <summary>Keeps every PCM sample while priming short network fragments before playback.</summary>
public sealed class PcmPlaybackBuffer(int bytesPerSecond = 48000)
{
    private readonly object _gate = new();
    private readonly Queue<byte[]> _packets = new();
    private int _offset, _bytes;
    private long? _firstPacket;
    private bool _primed, _complete;
    private long _added, _read, _underruns, _misaligned;
    private int _highWater;
    private bool _inUnderrun, _hadAudio;
    private Guid _interactionId;
    /// <summary>Gets a coherent numeric health snapshot, without retaining audio content.</summary>
    public PcmPlaybackMetrics Metrics { get { lock (_gate) { return new(_added, _read, _bytes, _highWater, _underruns, _misaligned); } } }
    /// <summary>Gets the number of actual PCM bytes awaiting playback.</summary>
    public int BufferedBytes { get { lock (_gate) { return _bytes; } } }
    /// <summary>Starts a new turn without discarding any still-queued audio.</summary>
    public void BeginTurn()
    { lock (_gate) { _complete = false; _firstPacket = null; _inUnderrun = false; _hadAudio = _bytes > 0; if (_bytes == 0) { _primed = false; } } }
    /// <summary>Copies received PCM into a bounded queue; overflow is reported instead of silently dropping words.</summary>
    public void Add(ReadOnlySpan<byte> pcm, long milliseconds, Guid interactionId = default)
    {
        if (pcm.Length == 0) { return; }
        lock (_gate)
        {
            if (interactionId != Guid.Empty) { _interactionId = interactionId; }
            if (pcm.Length > bytesPerSecond * 120 - _bytes) { throw new InvalidOperationException("Audio playback queue exceeded its two-minute limit."); }
            _firstPacket ??= milliseconds; _packets.Enqueue(pcm.ToArray()); _bytes += pcm.Length;
            _added += pcm.Length; _highWater = Math.Max(_highWater, _bytes);
        }
    }
    /// <summary>Writes silence during priming and returns the number of real PCM bytes copied without consuming them prematurely.</summary>
    public int Read(Span<byte> destination, long milliseconds)
    {
        destination.Clear();
        lock (_gate)
        {
            if (_bytes < 2) { if (_hadAudio && !_complete) { MarkUnderrun(); } return 0; }
            if (!_primed && !_complete && _bytes < bytesPerSecond * .12 && milliseconds - (_firstPacket ?? milliseconds) < 250) { return 0; }
            _primed = true;
            int available = Math.Min(destination.Length, _bytes) & ~1;
            int copied = 0;
            while (copied < available && _packets.TryPeek(out byte[]? packet))
            {
                int length = Math.Min(available - copied, packet.Length - _offset);
                packet.AsSpan(_offset, length).CopyTo(destination[copied..]);
                copied += length; _offset += length; _bytes -= length;
                if (_offset == packet.Length) { _packets.Dequeue(); _offset = 0; }
            }
            if (copied % 2 != 0) { _misaligned++; }
            _hadAudio |= copied > 0;
            _read += copied;
            if (copied < (destination.Length & ~1) && !_complete) { MarkUnderrun(); }
            else if (copied == (destination.Length & ~1)) { _inUnderrun = false; }
            return copied;
        }
    }
    /// <summary>Allows a short completed utterance to play even when it is below the priming threshold.</summary>
    public void CompleteTurn()
    { lock (_gate) { if ((_bytes & 1) != 0) { throw new InvalidOperationException("PCM turn ended halfway through a 16-bit sample."); } _complete = true; } }
    /// <summary>Reprimes only after an observed active-stream gap, keeping every pending sample in order.</summary>
    private void MarkUnderrun()
    {
        if (!_inUnderrun)
        {
            _underruns++; _inUnderrun = true;
            Laura.Core.Diagnostics.LauraDiagnostics.Record("audio.underrun", amount: _bytes, interactionId: _interactionId == Guid.Empty ? null : _interactionId);
        }
        _primed = false; _firstPacket = null;
    }
    /// <summary>Discards queued audio only following an explicit interruption.</summary>
    public void Clear() { lock (_gate) { _packets.Clear(); _offset = 0; _bytes = 0; _firstPacket = null; _primed = false; _complete = false; _inUnderrun = false; _hadAudio = false; } }
}
