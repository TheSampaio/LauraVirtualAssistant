namespace Laura.Core.Conversation;

/// <summary>Tracks model waiting independently from native tool duration while preserving owner/task cancellation.</summary>
public sealed class InferenceDeadline : IDisposable
{
    private readonly CancellationTokenSource _source;
    private readonly object _gate = new();
    private TimeSpan _duration;
    private int _tools;
    private bool _disposed;
    /// <summary>Creates a model deadline. Args: duration: Maximum model wait; cancellationToken: Independent owner/task lifetime.</summary>
    public InferenceDeadline(TimeSpan duration, CancellationToken cancellationToken)
    { _duration = duration; _source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); _source.CancelAfter(duration); }
    /// <summary>Gets the token for model waiting; parent cancellation remains effective during tools.</summary>
    public CancellationToken Token => _source.Token;
    /// <summary>Gets whether this model wait or its parent lifetime was canceled.</summary>
    public bool IsCancellationRequested => _source.IsCancellationRequested;
    /// <summary>Gets whether native operations currently suspend model waiting.</summary>
    public bool ToolActive { get { lock (_gate) { return _tools > 0; } } }
    /// <summary>Changes the next model-wait limit without charging active native tool duration.</summary>
    public void CancelAfter(TimeSpan duration)
    { lock (_gate) { if (_disposed) { return; } _duration = duration; _source.CancelAfter(_tools > 0 ? Timeout.InfiniteTimeSpan : duration); } }
    /// <summary>Suspends only model waiting until all concurrent native tool leases end.</summary>
    public IDisposable SuspendForTool()
    {
        lock (_gate)
        {
            if (_disposed) { return new ToolLease(null); }
            if (_tools++ == 0) { _source.CancelAfter(Timeout.InfiniteTimeSpan); }
            return new ToolLease(this);
        }
    }
    /// <summary>Resumes a fresh bounded model response after the last actual native tool completes.</summary>
    private void Resume()
    { lock (_gate) { if (_disposed) { return; } if (--_tools == 0) { _source.CancelAfter(_duration); } } }
    /// <inheritdoc />
    public void Dispose() { lock (_gate) { if (_disposed) { return; } _disposed = true; _source.Dispose(); } GC.SuppressFinalize(this); }
    /// <summary>Releases a single tool boundary exactly once, including error/cancellation paths.</summary>
    private sealed class ToolLease(InferenceDeadline? owner) : IDisposable
    {
        private InferenceDeadline? _owner = owner;
        /// <inheritdoc />
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Resume();
    }
}
