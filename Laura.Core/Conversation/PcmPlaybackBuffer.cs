namespace Laura.Core.Conversation;

/// <summary>Keeps every PCM sample while priming short network fragments before playback.</summary>
public sealed class PcmPlaybackBuffer(int bytesPerSecond = 48000)
{
    private readonly object _gate = new();
    private readonly Queue<byte[]> _packets = new();
    private int _offset, _bytes;
    private long? _firstPacket;
    private bool _primed, _complete;
    /// <summary>Gets the number of actual PCM bytes awaiting playback.</summary>
    public int BufferedBytes { get { lock (_gate) { return _bytes; } } }
    /// <summary>Starts a new turn without discarding any still-queued audio.</summary>
    public void BeginTurn()
    { lock (_gate) { _complete = false; _firstPacket = null; if (_bytes == 0) { _primed = false; } } }
    /// <summary>Copies received PCM into a bounded queue; overflow is reported instead of silently dropping words.</summary>
    public void Add(ReadOnlySpan<byte> pcm, long milliseconds)
    {
        if (pcm.Length == 0) { return; }
        lock (_gate)
        {
            if (pcm.Length > bytesPerSecond * 120 - _bytes) { throw new InvalidOperationException("Audio playback queue exceeded its two-minute limit."); }
            _firstPacket ??= milliseconds; _packets.Enqueue(pcm.ToArray()); _bytes += pcm.Length;
        }
    }
    /// <summary>Writes silence during priming and returns the number of real PCM bytes copied without consuming them prematurely.</summary>
    public int Read(Span<byte> destination, long milliseconds)
    {
        destination.Clear();
        lock (_gate)
        {
            if (_bytes == 0 || (!_primed && !_complete && _bytes < bytesPerSecond * .12 && milliseconds - (_firstPacket ?? milliseconds) < 250)) { return 0; }
            _primed = true;
            int copied = 0;
            while (copied < destination.Length && _packets.TryPeek(out byte[]? packet))
            {
                int length = Math.Min(destination.Length - copied, packet.Length - _offset);
                packet.AsSpan(_offset, length).CopyTo(destination[copied..]);
                copied += length; _offset += length; _bytes -= length;
                if (_offset == packet.Length) { _packets.Dequeue(); _offset = 0; }
            }
            return copied;
        }
    }
    /// <summary>Allows a short completed utterance to play even when it is below the priming threshold.</summary>
    public void CompleteTurn() { lock (_gate) { _complete = true; } }
    /// <summary>Discards queued audio only following an explicit interruption.</summary>
    public void Clear() { lock (_gate) { _packets.Clear(); _offset = 0; _bytes = 0; _firstPacket = null; _primed = false; _complete = false; } }
}
