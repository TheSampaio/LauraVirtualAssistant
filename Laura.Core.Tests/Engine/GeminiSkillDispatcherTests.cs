using System.Globalization;
using Laura.Core.Configuration;
using Laura.Core.Conversation;
using Laura.Core.Engine;
using Laura.Core.Memory;
using Laura.Core.Skills;
using Laura.Core.Tests.TestDoubles;

namespace Laura.Core.Tests.Engine;

/// <summary>Checks natural app routing and privacy boundaries through the selected provider.</summary>
public sealed class GeminiSkillDispatcherTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "laura-dispatch-" + Guid.NewGuid().ToString("N"));
    /// <summary>Provides actual bounded SQLite history for dispatch integration checks.</summary>
    private SqliteConversationMemory CreateMemory()
    {
        Directory.CreateDirectory(_directory);
        var memory = new SqliteConversationMemory(Path.Combine(_directory, "memory.db")); memory.Initialize();
        for (int index = 0; index < 12; index++) { memory.Append(new(ConversationMessageSource.UserTyped, "Previous " + index.ToString(CultureInfo.InvariantCulture), DateTimeOffset.Now)); }
        return memory;
    }
    [Theory]
    [InlineData(true, 8)]
    [InlineData(false, 0)]
    public async Task Dispatch_AppRequestUsesGeminiWithBrazilianLocaleAndRespectsMemoryPreference(bool remember, int expectedHistory)
    {
        var conversation = new RecordingConversationEngine(true, "Aplicativo aberto.");
        var dispatcher = new GeminiSkillDispatcher(conversation, new StubSettingsService(LauraSettings.Default with { RememberConversations = remember }),
            new StubUserContext(), CreateMemory(), new StubLocalizer());
        var request = SkillRequest.Create("Abra o meu emulador de PS2", CultureInfo.GetCultureInfo("pt-BR"), DateTimeOffset.Now, SkillRequestSource.Text);
        var answer = await dispatcher.DispatchAsync(request);
        Assert.Equal("Aplicativo aberto.", answer.SpokenText); Assert.Equal(1, conversation.CallCount);
        Assert.Equal(request.RawText, conversation.LastTurn!.Prompt); Assert.Equal("pt-BR", conversation.LastTurn.Culture.Name);
        Assert.Equal(expectedHistory, conversation.LastTurn.History.Count);
    }
    [Fact]
    public async Task Dispatch_ExcludesTheCurrentIdentityAndKeepsAnIdenticalEarlierQuestion()
    {
        var memory = CreateMemory(); Guid current = Guid.NewGuid(), earlier = Guid.NewGuid();
        memory.Append(new(ConversationMessageSource.UserTyped, "Repeat this question", DateTimeOffset.UtcNow) { TurnId = earlier });
        memory.Append(new(ConversationMessageSource.UserTyped, "Short display title", DateTimeOffset.UtcNow) { TurnId = current });
        var conversation = new RecordingConversationEngine(true, "Answer");
        var dispatcher = new GeminiSkillDispatcher(conversation, new StubSettingsService(LauraSettings.Default), new StubUserContext(), memory, new StubLocalizer());
        await dispatcher.DispatchAsync(SkillRequest.Create("Repeat this question", CultureInfo.GetCultureInfo("en-US"), DateTimeOffset.UtcNow, SkillRequestSource.Context) with { TurnId = current });
        Assert.Equal(current, conversation.LastTurn!.TurnId);
        Assert.DoesNotContain(conversation.LastTurn.History, message => message.TurnId == current);
        Assert.Contains(conversation.LastTurn.History, message => message.TurnId == earlier && message.Text == "Repeat this question");
    }
    [Fact]
    public async Task Dispatch_UnconfiguredSelectedProviderRequestsKeyInsteadOfCallingAProvider()
    {
        var conversation = new RecordingConversationEngine(false, null);
        var dispatcher = new GeminiSkillDispatcher(conversation, new StubSettingsService(LauraSettings.Default), new StubUserContext(), CreateMemory(), new StubLocalizer());
        var response = await dispatcher.DispatchAsync(SkillRequest.Create("Open Steam", CultureInfo.GetCultureInfo("en-US"), DateTimeOffset.Now, SkillRequestSource.Text));
        Assert.Equal("ui.ai.configureProvider", response.SpokenText); Assert.Equal(0, conversation.CallCount);
    }
    /// <inheritdoc />
    public void Dispose() { if (Directory.Exists(_directory)) { Directory.Delete(_directory, true); } }
}
