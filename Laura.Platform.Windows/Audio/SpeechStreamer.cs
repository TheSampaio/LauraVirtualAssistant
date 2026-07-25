namespace Laura.Platform.Windows.Audio;

/// <summary>
/// A blocking, in-memory pipe between a capture device and the recognition engine.
///
/// System.Speech reads its audio from a <see cref="Stream"/>, but a chosen capture
/// device delivers audio through callbacks. This stream bridges the two: the capture
/// callback writes samples, and the engine's read blocks until samples are available.
/// A bounded buffer keeps a burst of audio from growing without limit.
/// </summary>
#pragma warning disable CA1710 // The "Streamer" name reads better here than "…Stream" for a live audio pipe.
public sealed class SpeechStreamer : Stream
#pragma warning restore CA1710
{
    private readonly object _sync = new();
    private readonly byte[] _buffer;
    private int _head;
    private int _tail;
    private int _count;
    private bool _closed;

    /// <summary>
    /// Initializes the stream with a fixed-capacity ring buffer.
    ///
    /// Args:
    ///     capacityBytes: Size of the ring buffer; roughly a few seconds of audio.
    /// </summary>
    public SpeechStreamer(int capacityBytes = 1 << 20) => _buffer = new byte[capacityBytes];

    /// <inheritdoc />
    public override bool CanRead => true;

    /// <inheritdoc />
    public override bool CanSeek => false;

    /// <inheritdoc />
    public override bool CanWrite => true;

    /// <inheritdoc />
    public override long Length => throw new NotSupportedException();

    /// <inheritdoc />
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        lock (_sync)
        {
            while (_count == 0 && !_closed)
            {
                Monitor.Wait(_sync);
            }

            if (_count == 0 && _closed)
            {
                return 0;
            }

            int toRead = Math.Min(count, _count);

            for (int i = 0; i < toRead; i++)
            {
                buffer[offset + i] = _buffer[_head];
                _head = (_head + 1) % _buffer.Length;
            }

            _count -= toRead;
            Monitor.PulseAll(_sync);
            return toRead;
        }
    }

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        lock (_sync)
        {
            if (_closed)
            {
                return;
            }

            for (int i = 0; i < count; i++)
            {
                // When the buffer is full the oldest byte is dropped; falling
                // behind should degrade audio, not deadlock the writer.
                if (_count == _buffer.Length)
                {
                    _head = (_head + 1) % _buffer.Length;
                    _count--;
                }

                _buffer[_tail] = buffer[offset + i];
                _tail = (_tail + 1) % _buffer.Length;
                _count++;
            }

            Monitor.PulseAll(_sync);
        }
    }

    /// <summary>
    /// Signals that no more audio will be written and wakes any blocked reader.
    /// </summary>
    public void Complete()
    {
        lock (_sync)
        {
            _closed = true;
            Monitor.PulseAll(_sync);
        }
    }

    /// <inheritdoc />
    public override void Flush()
    {
    }

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Complete();
        }

        base.Dispose(disposing);
    }
}
