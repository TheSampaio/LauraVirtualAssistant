using Laura.Core.Execution;

namespace Laura.Core.Abstractions;

/// <summary>Executes a resolved native process through the central permission boundary.</summary>
public interface ICommandExecutor
{
    /// <summary>Runs one explicitly approved command and observes exit, bounded output and cancellation. Args: request: Frozen command; progress: Optional bounded output chunks; cancellationToken: Stop signal. Returns: Actual native result.</summary>
    Task<CommandExecutionResult> ExecuteAsync(CommandExecutionRequest request, IProgress<CommandOutput>? progress = null, CancellationToken cancellationToken = default);
}
