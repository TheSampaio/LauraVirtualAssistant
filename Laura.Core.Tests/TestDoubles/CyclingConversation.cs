using System.Text.Json;
using Laura.Core.Abstractions;
using Laura.Core.Conversation;
using Laura.Core.Engine;

namespace Laura.Core.Tests.TestDoubles;

/// <summary>Runs an actual three-step synthetic plan through native task receipts while scripting only provider behavior.</summary>
internal sealed class CyclingConversation(CognitiveTasks tasks, WorkingConversationMemory memory, bool firstTimeout = false) : IConversationEngine, IAssistantTools
{
    internal readonly List<string> Order = [];
    internal readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal readonly TaskCompletionSource Finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Guid Goal;
    internal readonly List<string?> ContinuationProviders = [];
    private int _steps;
    /// <inheritdoc />
    public bool IsAvailable => true;
    /// <inheritdoc />
    public ConversationFailure? LastFailure { get; private set; }
    /// <inheritdoc />
    public object[] Declarations => [new { functionDeclarations = new[] { new { name = "probe" }, new { name = "plan_task" } } }];
    /// <inheritdoc />
    public bool IsRegisteredTool(string name) => name is "probe" or "plan_task";
    /// <inheritdoc />
    public async Task<string?> AskAsync(ConversationTurn turn, CancellationToken cancellationToken = default)
    {
        LastFailure = null;
        if (turn.ContinuingTaskTurnId == Guid.Empty) { memory.SetWorkingContext(new ConversationWorkingContext(turn.TurnId, turn.Prompt)); }
        if (turn.Prompt is "status" or "barrier") { Order.Add(turn.Prompt); return "Observed steps: " + _steps; }
        if (Goal == Guid.Empty)
        {
            Goal = memory.WorkingContext.TurnId;
            await ExecuteAsync("plan_task", JsonSerializer.SerializeToElement(new { }), cancellationToken);
            Started.TrySetResult(); await Release.Task.WaitAsync(cancellationToken);
        }
        await ExecuteAsync("probe", JsonSerializer.SerializeToElement(new { step = _steps + 1 }), cancellationToken);
        if (_steps == 3) { Finished.TrySetResult(); }
        if (_steps == 1 && firstTimeout) { LastFailure = new(ConversationFailureKind.Timeout, true); return null; }
        return "Confirmed stage " + _steps;
    }
    /// <inheritdoc />
    public Task<AssistantToolResult> ExecuteAsync(string name, JsonElement arguments, CancellationToken cancellationToken) => tasks.ExecuteAsync(memory.WorkingContext,
        new HashSet<string>(StringComparer.Ordinal) { "probe", "plan_task" }, name, arguments, (run, _) =>
        {
            if (name == "plan_task") { run.Plan(["probe", "probe", "probe"]); }
            else { _steps++; Order.Add("cycle" + _steps); }
            return Task.FromResult(new AssistantToolResult("Confirmed synthetic operation."));
        }, cancellationToken);
    /// <inheritdoc />
    public Task<string?> PendingTaskContinuationAsync(CancellationToken cancellationToken)
    { ContinuationProviders.Add(ProviderExecutionScope.Provider); return tasks.InspectAsync(memory.WorkingContext.TurnId, run => run.PendingContinuation(), cancellationToken); }
}
