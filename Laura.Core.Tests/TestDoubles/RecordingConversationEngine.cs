using Laura.Core.Abstractions;
using Laura.Core.Conversation;

namespace Laura.Core.Tests.TestDoubles;

/// <summary>
/// Test generative engine that returns a fixed answer and records calls.
/// </summary>
public sealed class RecordingConversationEngine : IConversationEngine
{
    private readonly string? _answer;

    /// <summary>
    /// Creates the test engine.
    ///
    /// Args:
    ///     isAvailable: Value returned by <see cref="IsAvailable"/>.
    ///     answer: Answer returned by <see cref="AskAsync"/>.
    /// </summary>
    public RecordingConversationEngine(bool isAvailable, string? answer)
    {
        IsAvailable = isAvailable;
        _answer = answer;
    }

    /// <inheritdoc />
    public bool IsAvailable { get; }

    /// <summary>Gets how many times the engine was queried.</summary>
    public int CallCount { get; private set; }

    /// <inheritdoc />
    public Task<string?> AskAsync(ConversationTurn turn, CancellationToken cancellationToken = default)
    {
        CallCount++;
        return Task.FromResult(_answer);
    }
}
