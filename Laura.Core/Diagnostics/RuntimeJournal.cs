using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Laura.Core.Diagnostics;

/// <summary>Payload-free local journal. Producers never wait for disk; one worker owns rotation, snapshots and retention.</summary>
public sealed class RuntimeJournal : IAsyncDisposable
{
    private readonly string _directory;
    private readonly long _fileBytes;
    private readonly int _fileCount;
    private readonly Channel<Item> _queue = Channel.CreateBounded<Item>(new BoundedChannelOptions(2048) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly Task _worker;
    private readonly Timer _resources;
    private long _dropped, _failures;
    private int _disposed;
    private readonly Guid _run = Guid.NewGuid();
    private int _minimumLevel = (int)LogLevel.Information;
    /// <summary>Gets or sets the current native detail threshold; changing it does not enable sensitive payload logging.</summary>
    public LogLevel MinimumLevel
    {
        get => (LogLevel)Volatile.Read(ref _minimumLevel);
        set => Volatile.Write(ref _minimumLevel, Enum.IsDefined(value) ? (int)value : (int)LogLevel.Information);
    }
    private sealed record Item(RuntimeLogEvent? Event = null, TaskCompletionSource<RuntimeLogSnapshot>? Snapshot = null);
    /// <summary>Starts bounded recording in an application-owned directory; no message text, screenshots, PCM or credentials are accepted.</summary>
    public RuntimeJournal(string directory, long fileBytes = 1024 * 1024, int fileCount = 8)
    {
        _directory = Path.GetFullPath(directory); _fileBytes = Math.Clamp(fileBytes, 1024, 4 * 1024 * 1024); _fileCount = Math.Clamp(fileCount, 2, 16);
        LauraDiagnostics.Published += Observe;
        _worker = Task.Run(WriteAsync);
        Record("runtime.start", outcome: "metadata_only");
        _resources = new Timer(_ => SampleResources(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }
    /// <summary>Gets events discarded by backpressure and local storage failure counts, without throwing into the assistant.</summary>
    public long DroppedEvents => Interlocked.Read(ref _dropped);
    /// <summary>Gets failed local write attempts; the journal continues accepting later diagnostics.</summary>
    public long WriteFailures => Interlocked.Read(ref _failures);
    private void Observe(LauraDiagnosticSample sample) => Enqueue(new(_run, sample.InteractionId, sample.Timestamp, Safe(sample.Operation), double.IsFinite(sample.Milliseconds) ? sample.Milliseconds : 0, sample.Amount,
        Safe(sample.Outcome), sample.Ids.Select(HashIdentity).Take(16).ToArray(), sample.Scores.Where(double.IsFinite).Take(16).ToArray()) { TaskId = sample.TaskId, OperationId = sample.OperationId });
    /// <summary>Accepts finite numeric fields and restricted symbolic codes only. Arbitrary strings become 'redacted'.</summary>
    public void Record(string operation, double milliseconds = 0, long amount = 0, string outcome = "", Guid interactionId = default) =>
        Enqueue(new(_run, interactionId, DateTimeOffset.UtcNow, Safe(operation), double.IsFinite(milliseconds) ? milliseconds : 0, amount, Safe(outcome), [], []));
    private void Enqueue(RuntimeLogEvent row)
    { if (row.Level < MinimumLevel) { return; } if (Volatile.Read(ref _disposed) == 0 && !_queue.Writer.TryWrite(new(row))) { Interlocked.Increment(ref _dropped); } }
    /// <summary>Flushes preceding events and snapshots all retained files on the writer lane. Recording can continue during export.</summary>
    public async Task<RuntimeLogSnapshot> SnapshotAsync(CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var completion = new TaskCompletionSource<RuntimeLogSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        await _queue.Writer.WriteAsync(new(Snapshot: completion), token).ConfigureAwait(false);
        return await completion.Task.WaitAsync(token).ConfigureAwait(false);
    }
    private async Task WriteAsync()
    {
        string? path = null; long length = 0; int segment = 0;
        await foreach (Item item in _queue.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                Directory.CreateDirectory(_directory);
                if (item.Snapshot is not null)
                {
                    var rows = new List<RuntimeLogEvent>(); int malformed = 0;
                    foreach (string file in RetainedFiles())
                    {
                        if (new FileInfo(file).Length > 4 * 1024 * 1024 + 8192) { malformed++; continue; }
                        foreach (string line in await File.ReadAllLinesAsync(file).ConfigureAwait(false))
                        {
                            try
                            {
                                var row = JsonSerializer.Deserialize<RuntimeLogEvent>(line);
                                if (row is null) { malformed++; continue; }
                                // Revalidate on export: files may have been edited outside Laura.
                                rows.Add(row with { Operation = Safe(row.Operation), Outcome = Safe(row.Outcome), Ids = (row.Ids ?? []).Select(HashIdentity).Take(16).ToArray(),
                                    Scores = (row.Scores ?? []).Where(double.IsFinite).Take(16).ToArray(), Milliseconds = double.IsFinite(row.Milliseconds) ? row.Milliseconds : 0 });
                            }
                            catch (JsonException) { malformed++; }
                        }
                    }
                    item.Snapshot.TrySetResult(new(DateTimeOffset.UtcNow, rows.OrderBy(row => row.Timestamp).ToArray(), DroppedEvents, WriteFailures, malformed));
                    continue;
                }
                string json = JsonSerializer.Serialize(item.Event) + "\n"; int bytes = Encoding.UTF8.GetByteCount(json);
                if (path is null || length + bytes > _fileBytes)
                {
                    path = Path.Combine(_directory, "runtime-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff", System.Globalization.CultureInfo.InvariantCulture) + "-" + _run.ToString("N")[..8] + "-" + (++segment).ToString("D6", System.Globalization.CultureInfo.InvariantCulture) + ".jsonl");
                    length = 0;
                    foreach (string old in RetainedFiles().Where(file => File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-7))) { File.Delete(old); }
                    foreach (string old in RetainedFiles().Reverse().Skip(_fileCount - 1)) { File.Delete(old); }
                }
                await File.AppendAllTextAsync(path, json, new UTF8Encoding(false)).ConfigureAwait(false); length += bytes;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                Interlocked.Increment(ref _failures);
                item.Snapshot?.TrySetException(new IOException("Local diagnostic storage is unavailable."));
                path = null;
            }
        }
    }
    private IEnumerable<string> RetainedFiles() => Directory.EnumerateFiles(_directory, "runtime-*.jsonl", SearchOption.TopDirectoryOnly)
        .Where(file => (File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0).OrderBy(file => file, StringComparer.Ordinal);
    private void SampleResources()
    {
        try
        {
            using Process process = Process.GetCurrentProcess();
            Record("resources.working_set", amount: process.WorkingSet64); Record("resources.cpu_total", milliseconds: process.TotalProcessorTime.TotalMilliseconds);
            Record("resources.allocated_total", amount: GC.GetTotalAllocatedBytes()); Record("resources.gc_gen2", amount: GC.CollectionCount(2));
        }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception) { Record("resources.unavailable"); }
    }
    private static string Safe(string? value) => value is { Length: <= 120 } && !value.StartsWith("sk-", StringComparison.OrdinalIgnoreCase)
        && !value.StartsWith("AIza", StringComparison.Ordinal) && !value.StartsWith("eyJ", StringComparison.Ordinal)
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-') ? value : "redacted";
    private static string HashIdentity(string? value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? "")))[..16];
    /// <summary>Stops subscriptions and drains accepted events on orderly shutdown.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) { return; }
        LauraDiagnostics.Published -= Observe; await _resources.DisposeAsync().ConfigureAwait(false); _queue.Writer.TryComplete(); await _worker.ConfigureAwait(false);
    }
}

/// <summary>Strict technical schema; no arbitrary payload, exception message or transcript field.</summary>
public sealed record RuntimeLogEvent(Guid RunId, Guid InteractionId, DateTimeOffset Timestamp, string Operation, double Milliseconds, long Amount, string Outcome, IReadOnlyList<string> Ids, IReadOnlyList<double> Scores)
{
    /// <summary>Gets the native task identity without retaining the objective.</summary>
    public Guid TaskId { get; init; }
    /// <summary>Gets the native effect invocation identity.</summary>
    public Guid OperationId { get; init; }
    /// <summary>Gets severity derived only from symbolic native event codes.</summary>
    public LogLevel Level => RuntimeEventLevel.For(Operation, Outcome);
}
/// <summary>A consistent retained technical window and explicit loss counters.</summary>
public sealed record RuntimeLogSnapshot(DateTimeOffset CapturedAt, IReadOnlyList<RuntimeLogEvent> Events, long DroppedEvents, long WriteFailures, int MalformedLines);
