using Laura.Core.Conversation;

namespace Laura.Core.Abstractions;

/// <summary>
/// Ponto de extensão para respostas geradas por um modelo de linguagem.
///
/// É deliberadamente opcional: o despachante só o consulta depois que todas as
/// habilidades recusam o comando, e a implementação padrão não faz nada. Assim
/// Laura permanece inteiramente funcional e offline, e conectar um provedor local
/// como o Ollama no futuro se resume a registrar outra implementação desta interface.
/// </summary>
public interface IConversationEngine
{
    /// <summary>
    /// Obtém um valor que indica se o motor está configurado e pronto para responder.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Pede uma resposta ao modelo.
    ///
    /// Args:
    ///     turn: Pergunta e contexto do usuário.
    ///     cancellationToken: Token que aborta a requisição.
    ///
    /// Returns:
    ///     A resposta a falar, ou <see langword="null"/> quando o motor não está
    ///     disponível ou não conseguiu responder. Falhas do provedor não devem
    ///     propagar exceções: Laura precisa cair de volta na resposta padrão.
    /// </summary>
    Task<string?> AskAsync(ConversationTurn turn, CancellationToken cancellationToken = default);
}
