using System.Globalization;
using Laura.Core.Configuration;
using Laura.Core.Engine;
using Laura.Core.Skills;
using Laura.Core.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;

namespace Laura.Core.Tests.Engine;

/// <summary>
/// Dispatcher tests: priority order, failure isolation, and AI fallback.
/// </summary>
public sealed class SkillDispatcherTests
{
    private static SkillRequest CommandFor(string text) => SkillRequest.Create(
        text,
        CultureInfo.GetCultureInfo("en-US"),
        DateTimeOffset.Now,
        SkillRequestSource.Text);

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
        var specific = new FakeSkill("specific", priority: 10, "open settings", SkillResponse.Speak("specific"));
        var generic = new FakeSkill("generic", priority: 60, "open", SkillResponse.Speak("generic"));

        SkillDispatcher dispatcher = CreateDispatcher(
            [generic, specific],
            new RecordingConversationEngine(isAvailable: false, answer: null),
            generativeEnabled: false);

        SkillResponse response = await dispatcher.DispatchAsync(CommandFor("open settings"));

        Assert.Equal("specific", response.SpokenText);
        Assert.Equal(1, specific.ExecutionCount);
        Assert.Equal(0, generic.ExecutionCount);
    }

    [Fact]
    public async Task DispatchAsync_ReturnsNotHandledWhenNoSkillMatchesAndAiDisabled()
    {
        var conversationEngine = new RecordingConversationEngine(isAvailable: true, answer: "answer");

        SkillDispatcher dispatcher = CreateDispatcher([], conversationEngine, generativeEnabled: false);

        SkillResponse response = await dispatcher.DispatchAsync(CommandFor("anything"));

        Assert.False(response.Handled);
        Assert.Equal(0, conversationEngine.CallCount);
    }

    [Fact]
    public async Task DispatchAsync_FallsBackToConversationEngineWhenEnabled()
    {
        var conversationEngine = new RecordingConversationEngine(isAvailable: true, answer: "AI answer");

        SkillDispatcher dispatcher = CreateDispatcher([], conversationEngine, generativeEnabled: true);

        SkillResponse response = await dispatcher.DispatchAsync(CommandFor("tell me a joke"));

        Assert.True(response.Handled);
        Assert.Equal("AI answer", response.SpokenText);
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

        SkillResponse response = await dispatcher.DispatchAsync(CommandFor("fail now"));

        // The failure becomes a spoken response, not a propagated exception.
        Assert.True(response.Handled);
        Assert.NotNull(response.SpokenText);
    }
}
