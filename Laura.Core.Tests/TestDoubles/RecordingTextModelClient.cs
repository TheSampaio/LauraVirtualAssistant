using Laura.Core.Abstractions;
using Laura.Core.Conversation;
using System.Text.Json;

namespace Laura.Core.Tests.TestDoubles;

/// <summary>Records text-protocol inputs without credentials, native inference or network access.</summary>
internal sealed class RecordingTextModelClient(string provider) : ITextModelClient
{
    /// <inheritdoc />
    public string Provider => provider;
    /// <inheritdoc />
    public string Model => "synthetic-model";
    /// <inheritdoc />
    public bool IsAvailable => true;
    /// <inheritdoc />
    public bool SupportsImages { get; init; }
    /// <inheritdoc />
    public int ContextTokenLimit { get; init; } = int.MaxValue;
    /// <summary>Gets programmed model rounds.</summary>
    public Queue<TextModelReply> Replies { get; } = new();
    /// <summary>Gets actual immutable request snapshots.</summary>
    public List<TextModelRequest> Requests { get; } = [];
    /// <inheritdoc />
    public Task<TextModelReply> CompleteAsync(TextModelRequest request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); Requests.Add(request with { Messages = request.Messages.ToArray() });
        return Task.FromResult(Replies.TryDequeue(out var reply) ? reply : new TextModelReply("Verified answer.", [], JsonSerializer.SerializeToElement(new { role = "assistant", content = "Verified answer." })));
    }
}
