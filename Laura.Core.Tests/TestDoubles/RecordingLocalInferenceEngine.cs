using Laura.Core.Abstractions;
using Laura.Core.Conversation;

namespace Laura.Core.Tests.TestDoubles;

/// <summary>Records local routing without loading native weights or creating a network request.</summary>
public sealed class RecordingLocalInferenceEngine : ILocalInferenceEngine
{
    /// <inheritdoc />
    public bool IsAvailable { get; set; } = true;
    /// <inheritdoc />
    public string Status => "ready";
    /// <inheritdoc />
    public string? LoadedModel => "synthetic";
    /// <summary>Gets the number of actual local requests.</summary>
    public int Calls { get; private set; }
    /// <summary>Gets the last supplied shared context.</summary>
    public ConversationTurn? LastTurn { get; private set; }
    /// <inheritdoc />
    public event EventHandler<string>? TokenReceived { add { } remove { } }
    /// <inheritdoc />
    public Task LoadAsync(string id, CancellationToken cancellationToken) => Task.CompletedTask;
    /// <inheritdoc />
    public Task UnloadAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    /// <inheritdoc />
    public void Cancel() { }
    /// <inheritdoc />
    public Task ResetSessionAsync() => Task.CompletedTask;
    /// <inheritdoc />
    public Task<string?> AskAsync(ConversationTurn turn, CancellationToken cancellationToken = default)
    { Calls++; LastTurn = turn; return Task.FromResult<string?>("Synthetic local answer"); }
}
