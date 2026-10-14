using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Laura.Core.Diagnostics;

/// <summary>Bounded opt-in numeric diagnostics, with standard .NET metrics and interaction correlation; never records input text or paths.</summary>
public static class LauraDiagnostics
{
    private static readonly Meter Meter = new("Laura.Runtime", "1.0");
    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>("laura.operation.duration", "ms");
    private static readonly AsyncLocal<Guid> Interaction = new();
    private static readonly ConcurrentQueue<LauraDiagnosticSample> Samples = new();
    private static int _captures;
    /// <summary>Measures a named operation without recording its content.</summary>
    public static IDisposable Measure(string operation) => new Measurement(operation);
    /// <summary>Associates nested operations with an existing real interaction, preserving outer scopes.</summary>
    public static IDisposable Correlate(Guid id) => new Correlation(id);
    /// <summary>Enables bounded in-memory detailed diagnostics until disposed; normal use retains no samples.</summary>
    public static IDisposable Capture() { Interlocked.Increment(ref _captures); return new CaptureScope(); }
    /// <summary>Gets the currently retained diagnostic rows; no conversation content is present.</summary>
    public static IReadOnlyList<LauraDiagnosticSample> Snapshot() => Samples.ToArray();
    /// <summary>Publishes numeric measurements with bounded optional identity metadata, never note text or filesystem paths.</summary>
    public static void Record(string operation, double milliseconds = 0, long amount = 0, string outcome = "", IReadOnlyList<string>? ids = null)
    {
        Duration.Record(milliseconds, new KeyValuePair<string, object?>("operation", operation));
        if (Volatile.Read(ref _captures) == 0) { return; }
        Samples.Enqueue(new(Interaction.Value, DateTimeOffset.UtcNow, operation, milliseconds, amount, outcome, ids?.Take(16).ToArray() ?? []));
        while (Samples.Count > 4096) { Samples.TryDequeue(out _); }
    }
    private sealed class Measurement(string operation) : IDisposable
    {
        private readonly long _start = Stopwatch.GetTimestamp();
        public void Dispose() => Record(operation, Stopwatch.GetElapsedTime(_start).TotalMilliseconds);
    }
    private sealed class Correlation : IDisposable
    {
        private readonly Guid _prior;
        public Correlation(Guid id) { _prior = Interaction.Value; Interaction.Value = id; }
        public void Dispose() => Interaction.Value = _prior;
    }
    private sealed class CaptureScope : IDisposable
    {
        private int _disposed;
        public void Dispose() { if (Interlocked.Exchange(ref _disposed, 1) == 0 && Interlocked.Decrement(ref _captures) == 0) { while (Samples.TryDequeue(out _)) { } } }
    }
}

/// <summary>A numeric operation measurement; optional IDs are bounded stable knowledge identities, not contents.</summary>
public sealed record LauraDiagnosticSample(Guid InteractionId, DateTimeOffset Timestamp, string Operation, double Milliseconds, long Amount, string Outcome, IReadOnlyList<string> Ids);
