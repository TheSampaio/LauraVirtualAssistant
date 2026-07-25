using Laura.Core.Abstractions;
using Laura.Core.Conversation;

namespace Laura.Core.Tests.TestDoubles;

/// <summary>
/// Motor generativo de teste que devolve uma resposta fixa e registra as chamadas.
/// </summary>
public sealed class RecordingConversationEngine : IConversationEngine
{
    private readonly string? _answer;

    /// <summary>
    /// Cria o motor de teste.
    ///
    /// Args:
    ///     isAvailable: Valor devolvido por <see cref="IsAvailable"/>.
    ///     answer: Resposta devolvida por <see cref="AskAsync"/>.
    /// </summary>
    public RecordingConversationEngine(bool isAvailable, string? answer)
    {
        IsAvailable = isAvailable;
        _answer = answer;
    }

    /// <inheritdoc />
    public bool IsAvailable { get; }

    /// <summary>Obtém quantas vezes o motor foi consultado.</summary>
    public int CallCount { get; private set; }

    /// <inheritdoc />
    public Task<string?> AskAsync(ConversationTurn turn, CancellationToken cancellationToken = default)
    {
        CallCount++;
        return Task.FromResult(_answer);
    }
}
