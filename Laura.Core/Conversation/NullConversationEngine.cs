using Laura.Core.Abstractions;

namespace Laura.Core.Conversation;

/// <summary>
/// Motor generativo inerte, registrado enquanto nenhum provedor real está conectado.
///
/// Aplicar o padrão Objeto Nulo aqui evita verificações de nulidade espalhadas pelo
/// despachante e mantém o caminho sem IA como o caminho normal, não como exceção.
/// </summary>
public sealed class NullConversationEngine : IConversationEngine
{
    /// <inheritdoc />
    public bool IsAvailable => false;

    /// <inheritdoc />
    public Task<string?> AskAsync(ConversationTurn turn, CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);
}
