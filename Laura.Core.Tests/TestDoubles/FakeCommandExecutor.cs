using Laura.Core.Abstractions;
using Laura.Core.Execution;

namespace Laura.Core.Tests.TestDoubles;

/// <summary>Controls actual-creation notifications and process observations without launching a platform process.</summary>
public sealed class FakeCommandExecutor : ICommandExecutor
{
    private int _nextId;
    /// <summary>Gets or sets the explicitly scripted boundary behavior.</summary>
    public Func<CommandExecutionRequest, IProgress<CommandOutput>?, Action<int>?, CancellationToken, Task<CommandExecutionResult>>? Handler { get; set; }
    /// <inheritdoc />
    public async Task<CommandExecutionResult> ExecuteAsync(CommandExecutionRequest request, IProgress<CommandOutput>? progress = null, Action<int>? started = null, CancellationToken cancellationToken = default)
    {
        if (Handler is { } handler) { return await handler(request, progress, started, cancellationToken); }
        int id = Interlocked.Increment(ref _nextId); started?.Invoke(id);
        try { await Task.Delay(Timeout.Infinite, cancellationToken); }
        catch (OperationCanceledException) { }
        return new(id, 1, "", "", "cancelled", false, TimeSpan.Zero);
    }
}
