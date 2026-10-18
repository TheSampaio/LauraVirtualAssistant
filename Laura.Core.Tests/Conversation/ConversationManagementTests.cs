using System.Text.Json;
using Laura.Core.Conversation;
using Laura.Core.Memory;

namespace Laura.Core.Tests.Conversation;

public sealed class ConversationManagementTests
{
    [Theory]
    [InlineData("md")]
    [InlineData("txt")]
    [InlineData("json")]
    public void ExportPreservesBodiesOrderAndVoiceWhileRemovingRecognizableCredentials(string format)
    {
        const string body = "  código Unicode Ω\n\n```csharp\nConsole.WriteLine(\"Olá\");\n```\nhttps://example.org/路径";
        const string secret = "configured-secret-phrase-1234";
        var now = DateTimeOffset.UtcNow;
        ConversationMessage[] messages = [new(ConversationMessageSource.Assistant, "second " + secret + "\napi_key=private-value\nBearer abcdefghijklmnop\nsk-proj-abcdefghijklmnop1234", now.AddSeconds(1)), new(ConversationMessageSource.UserVoice, body, now)];
        string text = ConversationExport.Render(messages, format, [secret]);
        Assert.DoesNotContain(secret, text); Assert.DoesNotContain("private-value", text); Assert.DoesNotContain("abcdefghijklmnop", text);
        if (format == "json") { using var json = JsonDocument.Parse(text); Assert.Equal(body, json.RootElement.GetProperty("Messages")[0].GetProperty("Text").GetString()); }
        else { Assert.Contains(body, text); Assert.True(text.IndexOf(body, StringComparison.Ordinal) < text.IndexOf("second", StringComparison.Ordinal)); Assert.Contains("voz", text); }
        Assert.Equal(body, messages[1].Text);
    }
    [Fact]
    public void ManualReadingSurvivesNewMessagesAndLayoutUntilReturningToTheEnd()
    {
        var state = new ConversationScrollState(); state.MessageArrived(); Assert.True(state.Following);
        state.ManualPosition(200); state.MessageArrived(); state.MessageArrived(); Assert.False(state.Following); Assert.Equal(2, state.Unread);
        state.ManualPosition(40); Assert.False(state.Following);
        state.ManualPosition(0); Assert.True(state.Following); Assert.Equal(0, state.Unread);
        state.ManualPosition(300); state.Follow(); Assert.True(state.Following);
    }
    [Fact]
    public async Task ExistingAgentLoopStopsAtTheCallBudgetAndRecordsTechnicalLimitsWithoutArguments()
    {
        using var capture = Laura.Core.Diagnostics.LauraDiagnostics.Capture();
        var task = new Laura.Core.Engine.CognitiveTaskRun("Synthetic objective", new HashSet<string> { "recall_memory" }, maximumCalls: 1);
        using var args = JsonDocument.Parse("{\"query\":\"private fixture text\"}");
        await task.ExecuteAsync("recall_memory", args.RootElement, _ => Task.FromResult(new Laura.Core.Abstractions.AssistantToolResult("private fixture result")), CancellationToken.None);
        var denied = await task.ExecuteAsync("recall_memory", args.RootElement, _ => throw new InvalidOperationException("Must not execute beyond the existing budget"), CancellationToken.None);
        Assert.True(denied.Failed); Assert.Equal("blocked", denied.Status);
        Assert.Contains(Laura.Core.Diagnostics.LauraDiagnostics.Snapshot(), row => row.Operation == "agent.limit" && row.Outcome == "calls");
    }
    [Fact]
    public void FullRetainedTranscriptIncludesUnrenderedMessagesAndUntruncatedLongText()
    {
        string root = Path.Combine(Path.GetTempPath(), "laura-export-" + Guid.NewGuid().ToString("N"));
        try
        {
            var memory = new SqliteConversationMemory(Path.Combine(root, "memory.db")); memory.SetFact("durable", "Keep this fact");
            var now = DateTimeOffset.UtcNow;
            for (int index = 0; index < 230; index++) { memory.Append(new(ConversationMessageSource.UserTyped, "Message " + index, now.AddSeconds(index))); }
            string longText = new('Ω', 50000); memory.Append(new(ConversationMessageSource.Assistant, longText, now.AddSeconds(231)));
            Assert.Equal(200, memory.ReadRecent(500).Count); Assert.Equal(231, memory.ReadAllHistory().Count); Assert.Equal(longText, memory.ReadAllHistory()[^1].Text);
            memory.ClearHistory(); Assert.Empty(memory.ReadAllHistory()); Assert.Single(memory.Search("Keep"));
        }
        finally { if (Directory.Exists(root)) { Directory.Delete(root, true); } }
    }
}
