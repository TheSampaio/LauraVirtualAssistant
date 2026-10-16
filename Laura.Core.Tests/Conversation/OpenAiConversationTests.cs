using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Laura.Core.Abstractions;
using Laura.Core.Configuration;
using Laura.Core.Conversation;
using Laura.Core.Memory;
using Laura.Core.Permissions;
using Laura.Core.Tests.TestDoubles;

namespace Laura.Core.Tests.Conversation;

public sealed class OpenAiConversationTests
{
    [Fact]
    public async Task CurrentTurnIsNotReplayedAndToolDisclosureUsesTheOpenAiRuntime()
    {
        using var fixture = new Fixture(); Guid current = Guid.NewGuid(), earlier = Guid.NewGuid();
        fixture.Tools.Execute = _ =>
        { Assert.Equal("api.openai.com", ProviderExecutionScope.Destination); return Task.FromResult(new AssistantToolResult("Observed")); };
        fixture.Transport.Replies.Enqueue(_ => Reply([Call("read")])); fixture.Transport.Replies.Enqueue(_ => Reply([], "Answer"));
        await fixture.Provider.AskAsync(Turn with { TurnId = current, History = [
            new(ConversationMessageSource.UserTyped, "Same question", DateTimeOffset.UtcNow) { TurnId = earlier },
            new(ConversationMessageSource.UserTyped, "Display title", DateTimeOffset.UtcNow) { TurnId = current }] });
        var input = fixture.Transport.Requests[0].GetProperty("input").EnumerateArray().ToArray();
        Assert.Equal(2, input.Length); Assert.Equal("Same question", input[0].GetProperty("content").GetString());
        Assert.Equal(Turn.Prompt, input[1].GetProperty("content").GetString()); Assert.Null(ProviderExecutionScope.Provider);
    }

    [Fact]
    public async Task StreamsVisibleTextReplaysCompleteToolOutputAndUsesNativeOutcomes()
    {
        using var fixture = new Fixture();
        fixture.Transport.Replies.Enqueue(_ => Reply(new object[] { new { type = "reasoning", id = "rs_1", encrypted_content = "opaque", summary = Array.Empty<object>() }, Call("one") }, "Investigando."));
        fixture.Transport.Replies.Enqueue(_ => Reply([Call("two")], "Verificando."));
        fixture.Transport.Replies.Enqueue(_ => Reply([], "Concluído."));
        var streamed = new List<string>(); fixture.Provider.StreamUpdated += (_, update) => streamed.Add(update.Text);
        string? answer = await fixture.Provider.AskAsync(Turn);
        Assert.Equal("Investigando.\nVerificando.\nConcluído.", answer); Assert.Equal(2, fixture.Tools.Executed);
        Assert.Contains(streamed, value => value.StartsWith("Investigando.", StringComparison.Ordinal));
        Assert.All(fixture.Transport.Requests, request =>
        {
            Assert.False(request.GetProperty("store").GetBoolean()); Assert.False(request.GetProperty("parallel_tool_calls").GetBoolean());
            Assert.Equal("gpt-5.4-mini", request.GetProperty("model").GetString());
            Assert.Equal("object", request.GetProperty("tools")[0].GetProperty("parameters").GetProperty("type").GetString());
            Assert.Equal("string", request.GetProperty("tools")[0].GetProperty("parameters").GetProperty("properties").GetProperty("query").GetProperty("type").GetString());
        });
        var input = fixture.Transport.Requests[1].GetProperty("input");
        Assert.Contains(input.EnumerateArray(), item => item.TryGetProperty("encrypted_content", out var encrypted) && encrypted.GetString() == "opaque");
        var output = input.EnumerateArray().Single(item => item.TryGetProperty("type", out var type) && type.GetString() == "function_call_output");
        Assert.Equal("one", output.GetProperty("call_id").GetString());
        using var observed = JsonDocument.Parse(output.GetProperty("output").GetString()!);
        Assert.False(observed.RootElement.GetProperty("goalVerified").GetBoolean());
        Assert.False(observed.RootElement.GetProperty("executionVerified").GetBoolean());
        Assert.Empty(fixture.Memory.ReadAllHistory()); // Engine owns completed-message persistence.
    }
    [Theory]
    [InlineData("blocked")]
    [InlineData("cancelled")]
    public async Task DenialStopsRemainingBatchAndDisablesFurtherTools(string status)
    {
        using var fixture = new Fixture(); fixture.Tools.Result = new("Denied") { Failed = true, Status = status };
        fixture.Transport.Replies.Enqueue(_ => Reply([Call("first"), Call("second")]));
        fixture.Transport.Replies.Enqueue(_ => Reply([], "A ação foi recusada."));
        Assert.Equal("A ação foi recusada.", await fixture.Provider.AskAsync(Turn));
        Assert.Equal(1, fixture.Tools.Executed); Assert.Equal("none", fixture.Transport.Requests[1].GetProperty("tool_choice").GetString());
    }
    [Theory]
    [InlineData("unregistered-secret-name", "{}")]
    [InlineData("recall_memory", "invalid-json")]
    public async Task InvalidCallsCannotExecute(string name, string arguments)
    {
        using var fixture = new Fixture();
        fixture.Transport.Replies.Enqueue(_ => Reply([new { type = "function_call", call_id = "id", name, arguments }]));
        fixture.Transport.Replies.Enqueue(_ => Reply([], "Unable."));
        await fixture.Provider.AskAsync(Turn); Assert.Equal(0, fixture.Tools.Executed);
    }
    [Fact]
    public async Task DuplicateCallIdentityCannotRepeatAnEffect()
    {
        using var fixture = new Fixture(); fixture.Transport.Replies.Enqueue(_ => Reply([Call("same"), Call("same")]));
        fixture.Transport.Replies.Enqueue(_ => Reply([], "Stopped."));
        await fixture.Provider.AskAsync(Turn); Assert.Equal(1, fixture.Tools.Executed);
    }
    [Fact]
    public async Task TransportBudgetBoundsAProviderThatNeverFinishes()
    {
        using var fixture = new Fixture();
        for (int index = 0; index < 33; index++) { int call = index; fixture.Transport.Replies.Enqueue(_ => Reply([Call("id" + call)])); }
        fixture.Transport.Replies.Enqueue(_ => Reply([], "Partial."));
        Assert.Equal("Partial.", await fixture.Provider.AskAsync(Turn)); Assert.Equal(32, fixture.Tools.Executed);
        Assert.Equal("none", fixture.Transport.Requests[^1].GetProperty("tool_choice").GetString());
    }
    [Theory]
    [InlineData(401)]
    [InlineData(429)]
    [InlineData(500)]
    public async Task HttpFailureIsSafeAndDoesNotSwitchProviders(int code)
    {
        using var fixture = new Fixture(); fixture.Transport.Replies.Enqueue(_ => new((HttpStatusCode)code) { Content = new StringContent("private-error") });
        var other = new ProviderStub(); using var router = new ConversationProviderRouter(fixture.Settings, other, fixture.Provider);
        Assert.Null(await router.AskAsync(Turn)); Assert.Equal(0, other.Asked); Assert.Single(fixture.Transport.Requests);
    }
    [Theory]
    [InlineData("data: {\"type\":\"response.output_text.delta\",\"delta\":\"unfinished\"}\n\n")]
    [InlineData("data: {\"type\":\"response.incomplete\"}\n\n")]
    [InlineData("data: {invalid}\n\n")]
    [InlineData("data: {\"type\":\"response.completed\",\"response\":{\"status\":\"completed\"}}\n\n")]
    public async Task IncompleteOrMalformedStreamsDoNotCommitOrExecute(string payload)
    {
        using var fixture = new Fixture(); fixture.Transport.Replies.Enqueue(_ => Raw(payload)); int interrupted = 0;
        fixture.Provider.Interrupted += (_, _) => interrupted++;
        Assert.Null(await fixture.Provider.AskAsync(Turn)); Assert.Equal(0, fixture.Tools.Executed); Assert.Equal(1, interrupted); Assert.Empty(fixture.Memory.ReadAllHistory());
    }
    [Fact]
    public async Task OversizedEventsAreBounded()
    {
        using var fixture = new Fixture(); fixture.Transport.Replies.Enqueue(_ => Raw("data: " + new string('x', 2 * 1024 * 1024)));
        Assert.Null(await fixture.Provider.AskAsync(Turn)); Assert.Equal(0, fixture.Tools.Executed);
    }
    [Fact]
    public async Task ResetCancelsStreamFencesCallbackAndLeavesNoRemoteHistory()
    {
        using var fixture = new Fixture(); var streamed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Provider.StreamUpdated += (_, update) => { if (!update.Completed) { streamed.TrySetResult(); } };
        fixture.Transport.Replies.Enqueue(_ => new(HttpStatusCode.OK) { Content = new StreamContent(new HangingStream()) });
        Task<string?> pending = fixture.Provider.AskAsync(Turn); await streamed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await fixture.Provider.ResetSessionAsync(async () =>
        {
            Assert.False(fixture.Provider.IsAvailable); Assert.Null(await fixture.Provider.AskAsync(Turn)); fixture.Memory.ClearHistory();
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        fixture.Transport.Replies.Enqueue(_ => Reply([], "New session."));
        Assert.Equal("New session.", await fixture.Provider.AskAsync(Turn)); Assert.Single(fixture.Transport.Requests[^1].GetProperty("input").EnumerateArray());
    }
    [Fact]
    public async Task SwitchingDrainsSelectedProviderAndResetsBothBeforeUpdatingSettings()
    {
        using var fixture = new Fixture(); var other = new ProviderStub(); using var router = new ConversationProviderRouter(fixture.Settings, other, fixture.Provider);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Provider.StreamUpdated += (_, _) => started.TrySetResult();
        fixture.Transport.Replies.Enqueue(_ => new(HttpStatusCode.OK) { Content = new StreamContent(new HangingStream()) });
        Task<string?> pending = router.AskAsync(Turn); await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await router.ResetSessionAsync(async () =>
        {
            Assert.False(router.IsAvailable); Assert.False(fixture.Provider.IsAvailable); Assert.Null(await router.AskAsync(Turn));
            await fixture.Settings.UpdateAsync(fixture.Settings.Current with { GenerativeAi = fixture.Settings.Current.GenerativeAi with { Provider = "gemini" } });
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal("gemini", await router.AskAsync(Turn)); Assert.Equal(1, other.Reset); Assert.Equal(1, other.Asked);
    }
    [Fact]
    public async Task ImageDenialChecksOpenAiDestinationBeforeAnyNetworkSend()
    {
        using var fixture = new Fixture(); using var manager = new PermissionManager(new PermissionStore(Path.Combine(fixture.Root, "permissions.json")), () => new PermissionOptions());
        manager.Changed += (_, _) => { foreach (var request in manager.PendingRequests) { Assert.Equal("api.openai.com", request.Action.Destination); manager.Decide(request.Id, ApprovalChoice.Deny); } };
        using var provider = new OpenAiConversationEngine(fixture.Http, fixture.Settings, fixture.Keys, fixture.Memory, fixture.Tools, permissions: manager);
        Assert.Null(await provider.AskAsync(Turn with { Image = [1, 2, 3] })); Assert.Empty(fixture.Transport.Requests);
    }
    [Fact]
    public async Task MissingOptionalKeyDoesNotUseAnotherCredential()
    {
        using var fixture = new Fixture(); fixture.Keys.Value = "";
        Assert.False(fixture.Provider.IsAvailable); Assert.Null(await fixture.Provider.AskAsync(Turn)); Assert.Empty(fixture.Transport.Requests);
    }
    [Fact]
    public async Task TimeoutCancelsNetworkWithoutRetryOrLateOutput()
    {
        using var fixture = new Fixture(); await fixture.Settings.UpdateAsync(fixture.Settings.Current with { GenerativeAi = fixture.Settings.Current.GenerativeAi with { Timeout = TimeSpan.FromMilliseconds(100) } });
        fixture.Transport.Replies.Enqueue(_ => new(HttpStatusCode.OK) { Content = new StreamContent(new HangingStream()) });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Provider.AskAsync(Turn)); Assert.Single(fixture.Transport.Requests);
    }
    [Fact]
    public async Task ResetWaitsForAnUncooperativeToolAndDiscardsItsLateResult()
    {
        using var fixture = new Fixture(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Tools.Execute = async _ => { entered.SetResult(); await release.Task; return new("Late result"); };
        fixture.Transport.Replies.Enqueue(_ => Reply([Call("first")]));
        Task<string?> pending = fixture.Provider.AskAsync(Turn); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        bool callback = false; Task reset = fixture.Provider.ResetSessionAsync(() => { callback = true; return Task.CompletedTask; });
        Assert.False(callback); Assert.False(reset.IsCompleted); release.SetResult(); await reset.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending); Assert.Single(fixture.Transport.Requests); Assert.True(callback);
    }
    [Fact]
    public async Task ImageApprovalIsBoundToDestinationAndEachActualSend()
    {
        using var fixture = new Fixture(); using var manager = new PermissionManager(new PermissionStore(Path.Combine(fixture.Root, "permissions.json")), () => new PermissionOptions());
        int approvals = 0;
        manager.Changed += (_, _) => { foreach (var request in manager.PendingRequests) { Assert.Equal("api.openai.com", request.Action.Destination); approvals++; manager.Decide(request.Id, ApprovalChoice.Once); } };
        fixture.Transport.Replies.Enqueue(_ => Reply([Call("first")])); fixture.Transport.Replies.Enqueue(_ => Reply([], "Pixels received."));
        using var provider = new OpenAiConversationEngine(fixture.Http, fixture.Settings, fixture.Keys, fixture.Memory, fixture.Tools, permissions: manager);
        Assert.Equal("Pixels received.", await provider.AskAsync(Turn with { Image = [1, 2, 3] }));
        Assert.Equal(2, approvals);
        Assert.All(fixture.Transport.Requests, request => Assert.Contains("data:image/png;base64,AQID", request.GetRawText()));
    }
    [Fact]
    public async Task OnlyVisibleCanonicalMessagesReachTheAnswer()
    {
        using var fixture = new Fixture();
        fixture.Transport.Replies.Enqueue(_ => Reply([new { type = "reasoning", encrypted_content = "private opaque reasoning", summary = new[] { new { type = "summary_text", text = "hidden" } } }], "Resposta pública."));
        var updates = new List<string>(); fixture.Provider.StreamUpdated += (_, update) => updates.Add(update.Text);
        Assert.Equal("Resposta pública.", await fixture.Provider.AskAsync(Turn)); Assert.All(updates, text => { Assert.DoesNotContain("hidden", text); Assert.DoesNotContain("opaque", text); });
    }
    [Fact]
    public async Task NativePendingWorkHasOnlyFourContinuationsAndKeepsOriginalGrounding()
    {
        using var fixture = new Fixture(); fixture.Tools.Pending = true;
        for (int index = 0; index < 5; index++) { fixture.Transport.Replies.Enqueue(_ => Reply([], "Partial.")); }
        await fixture.Provider.AskAsync(Turn); Assert.Equal(5, fixture.Transport.Requests.Count); Assert.Equal(Turn.Prompt, fixture.Memory.WorkingUserText);
    }
    [Fact]
    public async Task NonpersistentChatKeepsBoundedSessionContextUntilReset()
    {
        using var fixture = new Fixture(); await fixture.Settings.UpdateAsync(fixture.Settings.Current with { RememberConversations = false });
        for (int index = 0; index < 7; index++) { fixture.Transport.Replies.Enqueue(_ => Reply([], "Acknowledged.")); }
        await fixture.Provider.AskAsync(Turn with { Prompt = "oldest-session-marker" });
        await fixture.Provider.AskAsync(Turn with { Prompt = "second-session-marker" });
        Assert.Contains("oldest-session-marker", fixture.Transport.Requests[1].GetRawText());
        for (int index = 0; index < 4; index++) { await fixture.Provider.AskAsync(Turn); }
        Assert.DoesNotContain("oldest-session-marker", fixture.Transport.Requests[^1].GetRawText());
        Assert.Empty(fixture.Memory.ReadAllHistory()); await fixture.Provider.ResetSessionAsync();
        await fixture.Provider.AskAsync(Turn); Assert.Single(fixture.Transport.Requests[^1].GetProperty("input").EnumerateArray());
    }
    [Fact]
    public async Task ConfigurationRoundTripsIndependentModelsAndDefaultsToLocalFirstWithoutCloudPermission()
    {
        using var fixture = new Fixture(); var store = new IniSettingsStore(Path.Combine(fixture.Root, "settings.ini"), Microsoft.Extensions.Logging.Abstractions.NullLogger<IniSettingsStore>.Instance);
        var options = new GenerativeAiOptions { Provider = "OPENAI", Model = "gemini-3.1-flash-live-preview", OpenAiModel = "gpt-5.4-mini" }.Sanitized();
        await store.SaveAsync(new LauraSettings { GenerativeAi = options }); var loaded = await store.LoadAsync(); Assert.Equal(options, loaded.GenerativeAi);
        Assert.Equal("auto", GenerativeAiOptions.Default.Provider); Assert.Equal("auto", (options with { Provider = "unknown" }).Sanitized().Provider); Assert.False(GenerativeAiOptions.Default.AllowCloudFallback);
        Assert.Equal(GenerativeAiOptions.Default.OpenAiModel, (options with { OpenAiModel = "../../invalid" }).Sanitized().OpenAiModel);
    }
    private static readonly ConversationTurn Turn = new("Synthetic objective", CultureInfo.GetCultureInfo("pt-BR"), "User");
    private static object Call(string id) => new { type = "function_call", call_id = id, name = "recall_memory", arguments = "{\"query\":\"test\"}" };
    private static HttpResponseMessage Reply(object[] output, string text = "")
    {
        var items = output.ToList();
        if (text.Length > 0) { items.Add(new { type = "message", role = "assistant", phase = "final_answer", content = new[] { new { type = "output_text", text } } }); }
        string delta = text.Length == 0 ? "" : "data: " + JsonSerializer.Serialize(new { type = "response.output_text.delta", delta = text }) + "\n\n";
        return Raw(delta + "data: " + JsonSerializer.Serialize(new { type = "response.completed", response = new { status = "completed", output = items } }) + "\n\n");
    }
    private static HttpResponseMessage Raw(string payload) => new(HttpStatusCode.OK) { Content = new StringContent(payload, Encoding.UTF8, "text/event-stream") };
    private sealed class Transport : HttpMessageHandler
    {
        public Queue<Func<CancellationToken, HttpResponseMessage>> Replies { get; } = new();
        public List<JsonElement> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("api.openai.com", request.RequestUri!.Host); Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            using var data = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken)); Requests.Add(data.RootElement.Clone());
            return Replies.Dequeue()(cancellationToken);
        }
    }
    private sealed class Keys : IApiKeyStore { public string Value { get; set; } = "synthetic-openai-key"; public string Read() => Value; public void Save(string key) => Value = key; }
    private sealed class Tools : IAssistantTools
    {
        public int Executed { get; private set; }
        public AssistantToolResult Result { get; set; } = new("Native observation");
        public Func<CancellationToken, Task<AssistantToolResult>>? Execute { get; set; }
        public bool Pending { get; set; }
        public object[] Declarations => [new { googleSearch = new { } }, new { functionDeclarations = new[] { new { name = "recall_memory", description = "Recall", parameters = new { type = "OBJECT", properties = new { query = new { type = "STRING" } }, required = new[] { "query" } } } } }];
        public bool IsRegisteredTool(string name) => name == "recall_memory";
        public Task<AssistantToolResult> ExecuteAsync(string name, JsonElement arguments, CancellationToken cancellationToken) { Executed++; return Execute?.Invoke(cancellationToken) ?? Task.FromResult(Result); }
        public Task<string?> PendingTaskContinuationAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(Pending ? "Continue original objective using native evidence." : null);
    }
    private sealed class HangingStream() : MemoryStream(Encoding.UTF8.GetBytes("data: {\"type\":\"response.output_text.delta\",\"delta\":\"partial\"}\n\n"))
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        { if (Position < Length) { return await base.ReadAsync(buffer, cancellationToken); } await Task.Delay(Timeout.Infinite, cancellationToken); return 0; }
    }
    private sealed class ProviderStub : IConversationEngine
    {
        public int Asked { get; private set; } public int Reset { get; private set; } public bool IsAvailable => true;
        public Task<string?> AskAsync(ConversationTurn turn, CancellationToken cancellationToken = default) { Asked++; return Task.FromResult<string?>("gemini"); }
        public Task ResetSessionAsync() { Reset++; return Task.CompletedTask; }
    }
    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "laura-gpt-" + Guid.NewGuid().ToString("N"));
        public Transport Transport { get; } = new(); public HttpClient Http { get; }
        public StubSettingsService Settings { get; } = new(new LauraSettings { GenerativeAi = new() { Provider = "openai" } });
        public Keys Keys { get; } = new(); public Tools Tools { get; } = new(); public TestMemory Memory { get; } = new();
        public OpenAiConversationEngine Provider { get; }
        public Fixture() { Http = new(Transport); Provider = new(Http, Settings, Keys, Memory, Tools); }
        public void Dispose() { Provider.Dispose(); Http.Dispose(); if (Directory.Exists(Root)) { Directory.Delete(Root, true); } }
    }
    private sealed class TestMemory : IConversationMemory
    {
        public ConversationWorkingContext WorkingContext { get; private set; } = new(Guid.Empty, "");
        public string WorkingUserText => WorkingContext.UserText;
        public void SetWorkingContext(string text) => WorkingContext = new(Guid.NewGuid(), text);
        public void Initialize() { } public void Append(ConversationMessage message) { }
        public IReadOnlyList<ConversationMessage> ReadRecent(int limit) => [];
        public IReadOnlyList<ConversationMessage> ReadAllHistory() => [];
        public IReadOnlyList<MemoryFact> Search(string query) => [];
        public void SetFact(string key, string text) { } public void DeleteFact(string key) { }
        public void ClearHistory() => WorkingContext = new(Guid.Empty, "");
    }
}
