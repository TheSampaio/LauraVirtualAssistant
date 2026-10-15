using System.Globalization;
using System.Net;
using Laura.Core.Abstractions;
using Laura.Core.Brain;
using Laura.Core.Conversation;
using Laura.Core.Memory;

namespace Laura.Core.Tests.Brain;

public sealed partial class BrainTests
{
    [Fact]
    public async Task GptUsesSharedMarkdownRetrievalAndSessionClearPreservesKnowledge()
    {
        BrainService brain = Create(); var note = await brain.SaveAsync(Note("Knowledge/Editor.md", "Editor preference", "My preferred editor is Synthetic Editor."));
        await _settings.UpdateAsync(_settings.Current with { GenerativeAi = new() { Provider = "openai" } });
        using var handler = new BrainGptTransport(); using var http = new HttpClient(handler);
        await using var learning = new BrainLearning(brain, _settings, _localizer, Microsoft.Extensions.Logging.Abstractions.NullLogger<BrainLearning>.Instance);
        var memory = new BrainConversationMemory(brain, learning, new SqliteConversationMemory(Path.Combine(_root, "gpt.db")));
        using var provider = new OpenAiConversationEngine(http, _settings, new BrainGptKey(), memory, new BrainGptTools(), new BrainContextBuilder(brain, _settings));
        await provider.AskAsync(new("Which editor do I use?", CultureInfo.GetCultureInfo("en-US"), "User"));
        Assert.Contains("Synthetic Editor", handler.Request); Assert.Contains("untrusted", handler.Request);
        await provider.ResetSessionAsync(() => { memory.ClearHistory(); return Task.CompletedTask; });
        Assert.Equal(Guid.Empty, memory.WorkingTurnId); Assert.Equal(note.Hash, brain.Snapshot.Notes.Single(n => n.Id == note.Id).Hash);
        await _settings.UpdateAsync(_settings.Current with { RememberConversations = false });
        await provider.AskAsync(new("Which editor do I use?", CultureInfo.GetCultureInfo("en-US"), "User"));
        Assert.DoesNotContain("Synthetic Editor", handler.Request);
    }
    private sealed class BrainGptTransport : HttpMessageHandler
    {
        public string Request { get; private set; } = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new(HttpStatusCode.OK) { Content = new StringContent("data: {\"type\":\"response.completed\",\"response\":{\"status\":\"completed\",\"output\":[]}}\n\n") };
        }
    }
    private sealed class BrainGptKey : IApiKeyStore { public string Read() => "synthetic"; public void Save(string key) { } }
    private sealed class BrainGptTools : IAssistantTools
    {
        public object[] Declarations => []; public Task<AssistantToolResult> ExecuteAsync(string name, System.Text.Json.JsonElement arguments, CancellationToken cancellationToken) => throw new InvalidOperationException("No tools requested.");
    }
}
