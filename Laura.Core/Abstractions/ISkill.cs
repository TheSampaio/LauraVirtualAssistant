using Laura.Core.Skills;

namespace Laura.Core.Abstractions;

/// <summary>
/// Uma capacidade isolada de Laura, como dizer as horas ou abrir um aplicativo.
///
/// Cada habilidade decide sozinha se um comando lhe pertence e executa apenas isso;
/// acrescentar uma capacidade nova é registrar uma implementação a mais, sem tocar
/// no motor nem nas habilidades existentes.
/// </summary>
public interface ISkill
{
    /// <summary>
    /// Obtém o identificador estável da habilidade, usado em registros de diagnóstico.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Obtém a ordem de avaliação; valores menores são consultados primeiro.
    ///
    /// Habilidades com gatilhos específicos precisam preceder as de gatilho amplo,
    /// para que "abrir configurações" não seja capturado por "abrir".
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Informa se esta habilidade reconhece o comando.
    ///
    /// Deve ser barata e livre de efeitos colaterais: é chamada para cada
    /// habilidade registrada até que uma aceite.
    ///
    /// Args:
    ///     request: Comando a avaliar.
    ///
    /// Returns:
    ///     <see langword="true"/> quando a habilidade sabe atender o comando.
    /// </summary>
    bool CanHandle(SkillRequest request);

    /// <summary>
    /// Executa o comando.
    ///
    /// Args:
    ///     request: Comando a atender, já aprovado por <see cref="CanHandle"/>.
    ///     cancellationToken: Token que aborta a execução.
    ///
    /// Returns:
    ///     A resposta a falar, ou <see cref="SkillResponse.NotHandled"/> quando a
    ///     habilidade desiste após inspecionar o comando mais a fundo.
    /// </summary>
    Task<SkillResponse> ExecuteAsync(SkillRequest request, CancellationToken cancellationToken = default);
}
