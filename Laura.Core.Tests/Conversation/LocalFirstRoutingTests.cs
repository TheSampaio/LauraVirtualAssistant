using System.Globalization;
using Laura.Core.Configuration;
using Laura.Core.Conversation;
using Laura.Core.Models;
using Laura.Core.Tests.TestDoubles;

namespace Laura.Core.Tests.Conversation;

/// <summary>Checks local-first admission and explicit consent without starting a server, voice session or network connection.</summary>
public sealed class LocalFirstRoutingTests
{
    /// <summary>A failed Light request never starts a second provider without the owner's selection.</summary>
    [Fact]
    public async Task LightFailureDoesNotOpenAnotherProvider()
    {
        var failure = new ConversationFailure(ConversationFailureKind.Network);
        var local = new RecordingConversationEngine(true, null) { LastFailure = failure };
        var chat = new RecordingConversationEngine(true, "verified fallback");
        var settings = new StubSettingsService(LauraSettings.Default with { LocalModel = new() { Enabled = true }, GenerativeAi = new() { Enabled = true, LightProvider = LightProviderSelection.Local },
            ChatGpt = new() { Enabled = true, AllowAutomatic = true, ShareContext = true, IncludedPlanOnly = true } });
        using var router = new ConversationProviderRouter(settings, chat, local: local);
        Assert.Null(await router.AskAsync(Turn("Hello"))); Assert.Equal("local", router.ActiveProvider);
        Assert.Equal(1, local.CallCount); Assert.Equal(0, chat.CallCount);
    }
    /// <summary>Even automatic fallback cannot replay a turn after a local adapter dispatched a potentially mutable tool.</summary>
    [Fact]
    public async Task LocalEffectsPreventCloudRetry()
    {
        var local = new RecordingConversationEngine(true, null) { LastFailure = new(ConversationFailureKind.Network, true) }; var chat = new RecordingConversationEngine(true, "cloud");
        var settings = new StubSettingsService(LauraSettings.Default with { LocalModel = new() { Enabled = true }, GenerativeAi = new() { Enabled = true, LightProvider = LightProviderSelection.Local } });
        using var router = new ConversationProviderRouter(settings, chat, local: local);
        Assert.Null(await router.AskAsync(Turn("Hello"))); Assert.Equal(0, chat.CallCount); Assert.True(router.LastFailure!.EffectsMayHaveRun);
    }
    /// <summary>A healthy local model serves routine requests without invoking either configured cloud provider.</summary>
    [Fact]
    public async Task AutomaticRoutineUsesLocalBeforeAuthorizedCloud()
    {
        var local = new RecordingConversationEngine(true, "local"); var chat = new RecordingConversationEngine(true, "chatgpt");
        var settings = new StubSettingsService(LauraSettings.Default with { ProviderSelection = ConversationProviderSelection.Automatic, LocalModel = new() { Enabled = true }, GenerativeAi = new() { Enabled = true, LightProvider = LightProviderSelection.Local } });
        using var router = new ConversationProviderRouter(settings, chat, local: local);
        Assert.Equal("local", await router.AskAsync(Turn("Hello"))); Assert.Equal(0, chat.CallCount);
    }
    /// <summary>Manual local selection survives a complex request while disabled cloud remains untouched.</summary>
    [Fact]
    public async Task ManualOfflineLocalNeverOpensCloud()
    {
        var local = new RecordingConversationEngine(true, "local");
        var settings = new StubSettingsService(LauraSettings.Default with { ProviderSelection = ConversationProviderSelection.Local, LocalModel = new() { Enabled = true } });
        using var router = new ConversationProviderRouter(settings, local: local);
        Assert.True(router.IsAvailable); Assert.Equal("local", await router.AskAsync(Turn("Review this architecture")));
    }
    /// <summary>Explicit requirements raise the recommended floor regardless of prompt length.</summary>
    [Theory]
    [InlineData("debug this race condition", TaskDifficulty.Advanced)]
    [InlineData("crie uma landing page", TaskDifficulty.Standard)]
    [InlineData("Olá", TaskDifficulty.Light)]
    public void DifficultyUsesRequirements(string prompt, TaskDifficulty expected) => Assert.Equal(expected, Turn(prompt).Difficulty);
    /// <summary>Creates an independent typed task.</summary>
    private static ConversationTurn Turn(string prompt) => new(prompt, CultureInfo.GetCultureInfo("en-US"), "Owner") { TurnId = Guid.NewGuid() };
}
