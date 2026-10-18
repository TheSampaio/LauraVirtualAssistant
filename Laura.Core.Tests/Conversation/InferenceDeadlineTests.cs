using Laura.Core.Conversation;

namespace Laura.Core.Tests.Conversation;

/// <summary>Checks model/tool lifetime separation and parent cancellation without contacting a provider.</summary>
public sealed class InferenceDeadlineTests
{
    /// <summary>Concurrent tools suspend only the model clock; releasing one tool cannot resume it prematurely.</summary>
    [Fact]
    public void ConcurrentToolsKeepModelClockSuspended()
    {
        using var deadline = new InferenceDeadline(TimeSpan.FromMinutes(2), CancellationToken.None);
        var first = deadline.SuspendForTool(); var second = deadline.SuspendForTool(); Assert.True(deadline.ToolActive);
        first.Dispose(); first.Dispose(); Assert.True(deadline.ToolActive); second.Dispose(); Assert.False(deadline.ToolActive);
        Assert.False(deadline.IsCancellationRequested);
    }
    /// <summary>Owner/task cancellation remains immediate while model timing is suspended.</summary>
    [Fact]
    public void ParentCancellationSurvivesToolSuspension()
    {
        using var owner = new CancellationTokenSource(); using var deadline = new InferenceDeadline(TimeSpan.FromMinutes(2), owner.Token);
        using var tool = deadline.SuspendForTool(); owner.Cancel(); Assert.True(deadline.IsCancellationRequested);
    }
    /// <summary>Late native cleanup cannot rearm a disposed inference or throw into the engine.</summary>
    [Fact]
    public void LateToolCompletionAfterDisposalIsHarmless()
    {
        var deadline = new InferenceDeadline(TimeSpan.FromMinutes(2), CancellationToken.None); var tool = deadline.SuspendForTool();
        deadline.Dispose(); tool.Dispose(); deadline.CancelAfter(TimeSpan.FromSeconds(1)); deadline.SuspendForTool().Dispose();
    }
}
