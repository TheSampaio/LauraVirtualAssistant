using Laura.Core.Abstractions;
using Laura.Core.Skills;

namespace Laura.Core.Tests.TestDoubles;

/// <summary>
/// Habilidade de teste com gatilho e resposta configuráveis.
/// </summary>
public sealed class FakeSkill : ISkill
{
    private readonly string _trigger;
    private readonly SkillResponse _response;

    /// <summary>
    /// Cria a habilidade de teste.
    ///
    /// Args:
    ///     id: Identificador da habilidade.
    ///     priority: Prioridade de avaliação.
    ///     trigger: Texto normalizado que a habilidade reconhece.
    ///     response: Resposta devolvida quando o gatilho casa.
    /// </summary>
    public FakeSkill(string id, int priority, string trigger, SkillResponse response)
    {
        Id = id;
        Priority = priority;
        _trigger = trigger;
        _response = response;
    }

    /// <inheritdoc />
    public string Id { get; }

    /// <inheritdoc />
    public int Priority { get; }

    /// <summary>Obtém quantas vezes a habilidade foi executada.</summary>
    public int ExecutionCount { get; private set; }

    /// <inheritdoc />
    public bool CanHandle(SkillRequest request) =>
        request.NormalizedText.Contains(_trigger, StringComparison.Ordinal);

    /// <inheritdoc />
    public Task<SkillResponse> ExecuteAsync(SkillRequest request, CancellationToken cancellationToken = default)
    {
        ExecutionCount++;
        return Task.FromResult(_response);
    }
}
