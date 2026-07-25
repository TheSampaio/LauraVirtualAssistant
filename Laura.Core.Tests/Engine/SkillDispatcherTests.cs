using System.Globalization;
using Laura.Core.Configuration;
using Laura.Core.Engine;
using Laura.Core.Skills;
using Laura.Core.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;

namespace Laura.Core.Tests.Engine;

/// <summary>
/// Testes do despachante: ordem de prioridade, isolamento de falhas e recuo para a IA.
/// </summary>
public sealed class SkillDispatcherTests
{
    private static SkillRequest CommandFor(string text) => SkillRequest.Create(
        text,
        CultureInfo.GetCultureInfo("pt-BR"),
        DateTimeOffset.Now,
        SkillRequestSource.Voice);

    private static SkillDispatcher CreateDispatcher(
        IEnumerable<FakeSkill> skills,
        RecordingConversationEngine conversationEngine,
        bool generativeEnabled)
    {
        LauraSettings settings = LauraSettings.Default with
        {
            GenerativeAi = GenerativeAiOptions.Default with { Enabled = generativeEnabled },
        };

        return new SkillDispatcher(
            skills,
            new StubLocalizer(),
            conversationEngine,
            new StubSettingsService(settings),
            new StubUserContext(),
            NullLogger<SkillDispatcher>.Instance);
    }

    [Fact]
    public async Task DispatchAsync_PrefersLowerPriorityValue()
    {
        var specific = new FakeSkill("specific", priority: 10, "abrir configuracoes", SkillResponse.Speak("específica"));
        var generic = new FakeSkill("generic", priority: 60, "abrir", SkillResponse.Speak("genérica"));

        SkillDispatcher dispatcher = CreateDispatcher(
            [generic, specific],
            new RecordingConversationEngine(isAvailable: false, answer: null),
            generativeEnabled: false);

        SkillResponse response = await dispatcher.DispatchAsync(CommandFor("abrir configuracoes"));

        Assert.Equal("específica", response.SpokenText);
        Assert.Equal(1, specific.ExecutionCount);
        Assert.Equal(0, generic.ExecutionCount);
    }

    [Fact]
    public async Task DispatchAsync_ReturnsNotHandledWhenNoSkillMatchesAndAiDisabled()
    {
        var conversationEngine = new RecordingConversationEngine(isAvailable: true, answer: "resposta");

        SkillDispatcher dispatcher = CreateDispatcher([], conversationEngine, generativeEnabled: false);

        SkillResponse response = await dispatcher.DispatchAsync(CommandFor("qualquer coisa"));

        Assert.False(response.Handled);
        Assert.Equal(0, conversationEngine.CallCount);
    }

    [Fact]
    public async Task DispatchAsync_FallsBackToConversationEngineWhenEnabled()
    {
        var conversationEngine = new RecordingConversationEngine(isAvailable: true, answer: "resposta da IA");

        SkillDispatcher dispatcher = CreateDispatcher([], conversationEngine, generativeEnabled: true);

        SkillResponse response = await dispatcher.DispatchAsync(CommandFor("me conte uma piada"));

        Assert.True(response.Handled);
        Assert.Equal("resposta da IA", response.SpokenText);
        Assert.Equal(1, conversationEngine.CallCount);
    }

    [Fact]
    public async Task DispatchAsync_IsolatesSkillFailures()
    {
        var failing = new FailingSkill();

        SkillDispatcher dispatcher = new(
            [failing],
            new StubLocalizer(),
            new RecordingConversationEngine(isAvailable: false, answer: null),
            new StubSettingsService(LauraSettings.Default),
            new StubUserContext(),
            NullLogger<SkillDispatcher>.Instance);

        SkillResponse response = await dispatcher.DispatchAsync(CommandFor("falhe agora"));

        // A falha vira uma resposta falada, não uma exceção propagada.
        Assert.True(response.Handled);
        Assert.NotNull(response.SpokenText);
    }
}
