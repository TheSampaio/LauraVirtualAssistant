using Laura.Core.Abstractions;
using Laura.Core.Conversation;

namespace Laura.Core.Tests.TestDoubles;

/// <summary>Supplies private local rounds and captures their protocol without native inference or external effects.</summary>
internal sealed class ScriptedLocalToolInference : ILocalToolInferenceEngine
{
    internal Queue<Func<CancellationToken, Task<string?>>> Replies { get; } = new();
    internal List<(ConversationTurn Turn, string Instructions, ConversationMessage[] Exchange)> Requests { get; } = [];
    /// <inheritdoc />
    public bool IsAvailable => true;
    /// <inheritdoc />
    public string Status => "ready";
    /// <inheritdoc />
    public string? LoadedModel => Laura.Core.Models.SupportedModels.Intelligence.Id;
    /// <inheritdoc />
    public ConversationFailure? LastFailure { get; set; }
    /// <inheritdoc />
    public event EventHandler<string>? TokenReceived { add { } remove { } }
    /// <inheritdoc />
    public Task<string?> AskWithToolsAsync(ConversationTurn turn, LocalToolPrompt instructions, IReadOnlyList<ConversationMessage> exchange, CancellationToken cancellationToken)
    {
        Requests.Add((turn, instructions.Instructions, exchange.ToArray())); cancellationToken.ThrowIfCancellationRequested();
        return Replies.Dequeue()(cancellationToken);
    }
    /// <inheritdoc />
    public Task<string?> AskAsync(ConversationTurn turn, CancellationToken cancellationToken = default) => Task.FromResult<string?>("Plain answer");
    /// <inheritdoc />
    public Task LoadAsync(string id, CancellationToken cancellationToken) => Task.CompletedTask;
    /// <inheritdoc />
    public Task UnloadAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    /// <inheritdoc />
    public void Cancel() { }
    /// <inheritdoc />
    public Task ResetSessionAsync() => Task.CompletedTask;
    /// <summary>Queues one complete untrusted synthetic model response.</summary>
    internal void Reply(string text) => Replies.Enqueue(_ => Task.FromResult<string?>(text));
    /// <summary>Queues a complete structured final answer without pretending it is a tool call.</summary>
    internal void Answer(string text) => Reply(System.Text.Json.JsonSerializer.Serialize(new { answer = text }));
}
