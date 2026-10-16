using System.Collections.Concurrent;
using Laura.Core.Abstractions;
using Laura.Core.Engine;

namespace Laura.Core.Tests.TestDoubles;

/// <summary>Records write-ahead ordering and injects durable failures without mocking frameworks.</summary>
internal sealed class RecordingTaskJournal : ICognitiveTaskJournal
{
    private readonly ConcurrentDictionary<Guid, CognitiveTaskCheckpoint> _entries = new();
    private int _saves;
    internal int FailFromSave { get; set; } = int.MaxValue;
    /// <inheritdoc />
    public Task SaveAsync(CognitiveTaskCheckpoint checkpoint, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.Increment(ref _saves) >= FailFromSave) { throw new IOException("Synthetic checkpoint write failure."); }
        _entries[checkpoint.Id] = checkpoint.Validated(); return Task.CompletedTask;
    }
    /// <inheritdoc />
    public Task<CognitiveTaskCheckpoint?> ReadAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(_entries.GetValueOrDefault(id));
    /// <inheritdoc />
    public Task<IReadOnlyList<CognitiveTaskCheckpoint>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CognitiveTaskCheckpoint>>(_entries.Values.ToArray());
    /// <inheritdoc />
    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) { _entries.TryRemove(id, out _); return Task.CompletedTask; }
}
