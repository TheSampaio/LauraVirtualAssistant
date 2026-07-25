using Laura.Core.Skills;

namespace Laura.Core.Abstractions;

/// <summary>
/// Encaminha um comando para a habilidade adequada.
/// </summary>
public interface ISkillDispatcher
{
    /// <summary>
    /// Encontra a habilidade capaz de atender o comando e a executa.
    ///
    /// Args:
    ///     request: Comando recebido.
    ///     cancellationToken: Token que aborta o despacho.
    ///
    /// Returns:
    ///     A resposta da habilidade escolhida, ou <see cref="SkillResponse.NotHandled"/>
    ///     quando nenhuma reconhece o comando.
    /// </summary>
    Task<SkillResponse> DispatchAsync(SkillRequest request, CancellationToken cancellationToken = default);
}
