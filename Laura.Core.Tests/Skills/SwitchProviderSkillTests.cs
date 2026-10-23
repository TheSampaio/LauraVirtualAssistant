using System.Globalization;
using Laura.Core.Configuration;
using Laura.Core.Conversation;
using Laura.Core.Engine;
using Laura.Core.Localization;
using Laura.Core.Skills;
using Laura.Core.Skills.Builtin;
using Laura.Core.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;

namespace Laura.Core.Tests.Skills;

/// <summary>Verifies that explicit provider commands change native settings without model interpretation.</summary>
public sealed class SwitchProviderSkillTests
{
    /// <summary>A Portuguese owner command selects ChatGPT even when Gemini is unavailable.</summary>
    [Fact]
    public async Task PortugueseCommandSelectsReadyChatGpt()
    {
        var localizer = Localizer("pt-BR"); var settings = new StubSettingsService(LauraSettings.Default);
        var skill = new SwitchProviderSkill(localizer, settings, () => true, () => false);
        Assert.False(skill.ModelCallable);
        SkillResponse response = await skill.ExecuteAsync(Request("Troca para o GPT", "pt-BR"));
        Assert.Equal(ConversationProviderSelection.ChatGpt, settings.Current.ProviderSelection);
        Assert.Contains("ChatGPT", response.SpokenText, StringComparison.Ordinal);
    }

    /// <summary>An unavailable target leaves the active provider and its consent unchanged.</summary>
    [Fact]
    public async Task UnavailableTargetDoesNotChangeSelection()
    {
        var localizer = Localizer("en-US"); var settings = new StubSettingsService(LauraSettings.Default);
        var skill = new SwitchProviderSkill(localizer, settings, () => false, () => true);
        SkillResponse response = await skill.ExecuteAsync(Request("switch to ChatGPT", "en-US"));
        Assert.Equal(ConversationProviderSelection.Automatic, settings.Current.ProviderSelection);
        Assert.Contains("isn't connected", response.SpokenText, StringComparison.Ordinal);
        Assert.False(skill.CanHandle(Request("write about ChatGPT", "en-US")));
    }
    /// <summary>The native command takes effect before the next model request reaches the router.</summary>
    [Fact]
    public async Task NativeDispatcherRoutesNextTurnToSelectedProvider()
    {
        var localizer = Localizer("en-US"); var settings = new StubSettingsService(LauraSettings.Default with
        { GenerativeAi = new() { Enabled = true }, ChatGpt = new() { Enabled = true, Model = "synthetic", IncludedPlanOnly = true } });
        var local = new RecordingConversationEngine(true, "Local answer"); var chat = new RecordingConversationEngine(true, "ChatGPT answer");
        using var router = new ConversationProviderRouter(settings, chat, local: local);
        var user = new StubUserContext(); var memory = new WorkingConversationMemory();
        var native = new SkillDispatcher([new SwitchProviderSkill(localizer, settings, () => chat.IsAvailable, () => local.IsAvailable)], localizer, router,
            settings, user, NullLogger<SkillDispatcher>.Instance, memory);
        var dispatcher = new AssistantSkillDispatcher(router, settings, user, memory, localizer, native);
        await dispatcher.DispatchAsync(Request("switch to ChatGPT", "en-US"));
        SkillResponse answer = await dispatcher.DispatchAsync(Request("Tell me the result", "en-US"));
        Assert.Equal("ChatGPT answer", answer.SpokenText); Assert.Equal(0, local.CallCount); Assert.Equal(1, chat.CallCount);
    }

    /// <summary>Loads actual EN/PT phrase catalogues rather than hard-coding triggers in the skill.</summary>
    private static JsonLocalizer Localizer(string culture)
    {
        var localizer = new JsonLocalizer(Path.Combine(AppContext.BaseDirectory, "Localization", "Locales"), NullLogger<JsonLocalizer>.Instance);
        localizer.SetCulture(CultureInfo.GetCultureInfo(culture)); return localizer;
    }

    /// <summary>Creates a normalized owner request for the native dispatcher.</summary>
    private static SkillRequest Request(string text, string culture) => SkillRequest.Create(text, CultureInfo.GetCultureInfo(culture), DateTimeOffset.UtcNow, SkillRequestSource.Text);
}
