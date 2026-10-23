using System.Globalization;
using System.Text.Json;
using Laura.Core.Configuration;
using Laura.Core.Conversation;
using Laura.Core.Tests.TestDoubles;

namespace Laura.Core.Tests.Conversation;

/// <summary>Verifies provider-independent native execution, exact task identity and silent protocol checkpoints.</summary>
public sealed class TextConversationEngineTests
{
    /// <summary>A factual wake briefing stays within a small local context by excluding old dialogue and tool schemas.</summary>
    [Fact]
    public async Task StartupBriefingUsesCompactLocalRequest()
    {
        var client = new RecordingTextModelClient("local") { ContextTokenLimit = 8192 };
        var memory = new WorkingConversationMemory();
        var history = Enumerable.Range(0, 8).Select(index => new ConversationMessage(ConversationMessageSource.UserTyped,
            new string('x', 9000) + index.ToString(CultureInfo.InvariantCulture), DateTimeOffset.UtcNow)).ToArray();
        using var engine = new TextConversationEngine(client, new StubSettingsService(LauraSettings.Default), memory, new LocalAgentTools());
        var turn = Turn() with { Prompt = "Verified day facts: one appointment at 14:00; one reminder at 17:00.",
            History = history, IsStartupBriefing = true };
        Assert.Equal("Verified answer.", await engine.AskAsync(turn));
        TextModelRequest request = Assert.Single(client.Requests);
        Assert.Single(request.Messages);
        Assert.Equal(turn.Prompt, request.Messages[0].Text);
        Assert.Empty(request.Declarations);
        Assert.Equal(1024, request.MaximumOutputTokens);
    }
    /// <summary>The new text adapter cannot transmit a direct image without the independent native authorization service.</summary>
    [Fact]
    public async Task ImageTransmissionFailsClosedWithoutNativeReview()
    {
        var client = new RecordingTextModelClient("chatgpt") { SupportsImages = true };
        using var engine = new TextConversationEngine(client, new StubSettingsService(LauraSettings.Default), new WorkingConversationMemory(), new LocalAgentTools());
        Assert.Null(await engine.AskAsync(Turn() with { Image = [1, 2, 3], ImageMimeType = "image/png" }));
        Assert.Empty(client.Requests); Assert.Equal(ConversationFailureKind.AccessDenied, engine.LastFailure!.Kind);
    }
    /// <summary>Both text protocols receive the same persona and permission-guarded tools under the actual executing recipient.</summary>
    [Theory]
    [InlineData("chatgpt")]
    [InlineData("local")]
    public async Task TextProtocolsSharePersonaToolsAndWorkingTask(string provider)
    {
        var client = new RecordingTextModelClient(provider); var tools = new LocalAgentTools(); var memory = new WorkingConversationMemory();
        client.Replies.Enqueue(Call("first"));
        var settings = new StubSettingsService(LauraSettings.Default with { GenerativeAi = new() { Persona = "Use measured prose." } });
        using var engine = new TextConversationEngine(client, settings, memory, tools);
        var turn = Turn(); Assert.Equal("Verified answer.", await engine.AskAsync(turn));
        Assert.Equal(1, tools.Calls); Assert.Equal(provider, tools.ActiveProvider); Assert.Equal(turn.TurnId, memory.WorkingContext.TurnId);
        Assert.Contains("Use measured prose.", client.Requests[0].Instructions, StringComparison.Ordinal);
        Assert.Contains("Brazilian Portuguese", client.Requests[0].Instructions, StringComparison.Ordinal);
        Assert.Contains(client.Requests[1].Messages, message => message.Role == "tool" && message.CallId == "first");
    }
    /// <summary>Four tool rounds yield a silent retained checkpoint; the next cycle consumes results without replaying effects.</summary>
    [Fact]
    public async Task CheckpointRetainsActualToolResultsWithoutConversationalProgressSpam()
    {
        var client = new RecordingTextModelClient("local"); var tools = new LocalAgentTools(); var memory = new WorkingConversationMemory();
        for (int index = 0; index < 4; index++) { client.Replies.Enqueue(Call("call-" + index)); }
        using var engine = new TextConversationEngine(client, new StubSettingsService(LauraSettings.Default), memory, tools);
        var turn = Turn(); Assert.Null(await engine.AskAsync(turn)); Assert.Null(engine.LastFailure); Assert.NotNull(engine.PendingRound(turn.TurnId));
        Assert.Equal("Verified answer.", await engine.AskAsync(turn with { TurnId = Guid.NewGuid(), ContinuingTaskTurnId = turn.TurnId }));
        Assert.Equal(4, tools.Calls); Assert.Equal(4, client.Requests[^1].Messages.Count(message => message.Role == "tool"));
    }
    /// <summary>A duplicate tool call cannot replay an effect, and failure metadata preserves the observed dispatch.</summary>
    [Fact]
    public async Task DuplicateCallFailsWithoutReplay()
    {
        var client = new RecordingTextModelClient("local"); client.Replies.Enqueue(Call("duplicate")); client.Replies.Enqueue(Call("duplicate"));
        var tools = new LocalAgentTools(); using var engine = new TextConversationEngine(client, new StubSettingsService(LauraSettings.Default), new WorkingConversationMemory(), tools);
        Assert.Null(await engine.AskAsync(Turn())); Assert.Equal(1, tools.Calls); Assert.True(engine.LastFailure!.EffectsMayHaveRun);
    }
    /// <summary>Creates an actual registered synthetic tool request.</summary>
    private static TextModelReply Call(string id) => new("", [new(id, "probe", JsonSerializer.SerializeToElement(new { }))], JsonSerializer.SerializeToElement(new { role = "assistant", content = "" }));
    /// <summary>Creates one independent native owner turn.</summary>
    private static ConversationTurn Turn() => new("Inspect the synthetic fixture.", CultureInfo.GetCultureInfo("pt-BR"), "Owner") { TurnId = Guid.NewGuid() };
}
