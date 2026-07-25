using Laura.Core.Abstractions;
using Laura.Core.Conversation;
using Laura.Core.Localization;
using Laura.Core.Skills;
using Microsoft.Extensions.Logging;

namespace Laura.Core.Engine;

/// <summary>
/// Escolhe e executa a habilidade que atende cada comando.
///
/// A ordem de consulta é fixada por <see cref="ISkill.Priority"/> no momento da
/// construção. Se nenhuma habilidade aceitar o comando e o modo generativo estiver
/// ligado, o pedido é encaminhado ao modelo — esse é o único ponto do sistema que
/// conhece a existência do modo generativo.
/// </summary>
public sealed class SkillDispatcher : ISkillDispatcher
{
    private readonly IReadOnlyList<ISkill> _skills;
    private readonly ILocalizer _localizer;
    private readonly IConversationEngine _conversationEngine;
    private readonly ISettingsService _settings;
    private readonly IUserContext _userContext;
    private readonly ILogger<SkillDispatcher> _logger;

    /// <summary>
    /// Inicializa o despachante ordenando as habilidades registradas.
    ///
    /// Args:
    ///     skills: Habilidades disponíveis, em qualquer ordem.
    ///     localizer: Fonte das mensagens de erro e recusa.
    ///     conversationEngine: Motor generativo opcional.
    ///     settings: Configurações vigentes, consultadas para saber se o modo
    ///     generativo está ligado.
    ///     userContext: Contexto do usuário repassado ao motor generativo.
    ///     logger: Destino dos registros de diagnóstico.
    /// </summary>
    public SkillDispatcher(
        IEnumerable<ISkill> skills,
        ILocalizer localizer,
        IConversationEngine conversationEngine,
        ISettingsService settings,
        IUserContext userContext,
        ILogger<SkillDispatcher> logger)
    {
        ArgumentNullException.ThrowIfNull(skills);
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(conversationEngine);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(userContext);
        ArgumentNullException.ThrowIfNull(logger);

        _skills = [.. skills.OrderBy(static skill => skill.Priority).ThenBy(static skill => skill.Id, StringComparer.Ordinal)];
        _localizer = localizer;
        _conversationEngine = conversationEngine;
        _settings = settings;
        _userContext = userContext;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<SkillResponse> DispatchAsync(
        SkillRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        foreach (ISkill skill in _skills)
        {
            if (!skill.CanHandle(request))
            {
                continue;
            }

            SkillResponse response = await ExecuteSafelyAsync(skill, request, cancellationToken).ConfigureAwait(false);

            if (response.Handled)
            {
                _logger.LogInformation("Comando \"{Command}\" atendido por {Skill}.", request.RawText, skill.Id);
                return response;
            }
        }

        return await AskConversationEngineAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Executa uma habilidade isolando falhas dela.
    ///
    /// Uma habilidade que lança não pode derrubar a assistente inteira; o erro vira
    /// uma resposta falada e o registro guarda o detalhe.
    ///
    /// Args:
    ///     skill: Habilidade a executar.
    ///     request: Comando a atender.
    ///     cancellationToken: Token que aborta a execução.
    ///
    /// Returns:
    ///     A resposta da habilidade, ou uma resposta de falha genérica.
    /// </summary>
    private async Task<SkillResponse> ExecuteSafelyAsync(
        ISkill skill,
        SkillRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await skill.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "A habilidade {Skill} falhou ao atender \"{Command}\".", skill.Id, request.RawText);
            return SkillResponse.Speak(_localizer.Get(LocalizationKeys.Assistant.SkillFailed));
        }
    }

    /// <summary>
    /// Encaminha ao modelo generativo um comando que nenhuma habilidade reconheceu.
    ///
    /// Args:
    ///     request: Comando não reconhecido.
    ///     cancellationToken: Token que aborta a requisição.
    ///
    /// Returns:
    ///     A resposta do modelo, ou <see cref="SkillResponse.NotHandled"/> quando o
    ///     modo generativo está desligado, indisponível ou não respondeu.
    /// </summary>
    private async Task<SkillResponse> AskConversationEngineAsync(
        SkillRequest request,
        CancellationToken cancellationToken)
    {
        if (!_settings.Current.GenerativeAi.Enabled || !_conversationEngine.IsAvailable)
        {
            _logger.LogInformation("Nenhuma habilidade reconheceu \"{Command}\".", request.RawText);
            return SkillResponse.NotHandled;
        }

        var turn = new ConversationTurn(request.RawText, request.Culture, _userContext.DisplayName);
        string? answer = await _conversationEngine.AskAsync(turn, cancellationToken).ConfigureAwait(false);

        return string.IsNullOrWhiteSpace(answer)
            ? SkillResponse.NotHandled
            : SkillResponse.Speak(answer);
    }
}
