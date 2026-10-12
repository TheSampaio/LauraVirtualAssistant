using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

namespace Laura.App.Infrastructure;

/// <summary>Opt-in, content-free timings for native isolated diagnostics.</summary>
internal static class PerformanceTrace
{
    private static readonly ConcurrentQueue<Sample> Samples = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly bool Enabled = Environment.GetCommandLineArgs().Contains("--ui-smoke", StringComparer.Ordinal);
    /// <summary>Measures a specific startup, construction or rendering operation without tracing user content.</summary>
    internal static IDisposable Measure(string operation) => new Scope(operation);
    /// <summary>Writes numeric timings only into the explicitly selected isolated diagnostic directory.</summary>
    internal static Task WriteAsync(string directory) => File.WriteAllTextAsync(Path.Combine(directory, "performance.json"), JsonSerializer.Serialize(Samples.ToArray(), JsonOptions));
    /// <summary>Represents a completed operation and its wall-clock duration.</summary>
    private sealed record Sample(string Operation, double Milliseconds);
    /// <summary>Records elapsed time on disposal while avoiding a clock in normal application use.</summary>
    private sealed class Scope(string operation) : IDisposable
    {
        private readonly long _start = Enabled ? Stopwatch.GetTimestamp() : 0;
        private int _disposed;
        /// <inheritdoc />
        public void Dispose() { if (Interlocked.Exchange(ref _disposed, 1) == 0 && _start != 0) { Samples.Enqueue(new(operation, Stopwatch.GetElapsedTime(_start).TotalMilliseconds)); } }
    }
}
