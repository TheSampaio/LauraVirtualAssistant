using System.Globalization;
using Laura.Core.Abstractions;
using Laura.Core.Configuration;
using Laura.Core.Conversation;
using Laura.Core.Tests.TestDoubles;

namespace Laura.Core.Tests.Conversation;

/// <summary>Verifies native authority, bounded effects, private local rounds and original-turn preservation independently of model quality.</summary>
public sealed class LocalToolConversationTests
{
    private readonly ScriptedLocalToolInference _inference = new();
    private readonly LocalAgentTools _tools = new();
    private readonly WorkingConversationMemory _memory = new();
    private readonly StubSettingsService _settings = new(LauraSettings.Default);
    private static readonly ConversationTurn Turn = new("Read the actual fixture value.", CultureInfo.GetCultureInfo("en-US"), "Tester") { TurnId = Guid.NewGuid() };

    /// <summary>Retains the actual user objective and provider while exposing only the final answer to UI subscribers.</summary>
    [Fact]
    public async Task DiscoversExactSchemaThenDispatchesActualNativeToolPrivately()
    {
        var agent = Create(); var updates = new List<ConversationStreamUpdate>(); agent.StreamUpdated += (_, update) => updates.Add(update);
        _inference.Reply(Call("describe_tool", "name", "probe")); _inference.Reply(Call("probe", "text", "literal")); _inference.Answer("The value is 731.");
        Assert.Equal("The value is 731.", await agent.AskAsync(Turn));
        Assert.Equal(1, _tools.Calls); Assert.Equal("local", _tools.ActiveProvider); Assert.Null(ProviderExecutionScope.Provider);
        Assert.Equal(Turn.TurnId, _memory.WorkingContext.TurnId); Assert.Equal(Turn.Prompt, _memory.WorkingContext.UserText);
        Assert.Single(updates); Assert.Equal(Turn.TurnId, updates[0].TurnId); Assert.True(updates[0].Completed);
        Assert.All(_inference.Requests, request => Assert.Equal(Turn, request.Turn));
        Assert.Contains("731", _inference.Requests[^1].Exchange[^1].Text, StringComparison.Ordinal);
        Assert.DoesNotContain("function=", updates[0].Text, StringComparison.Ordinal);
    }

    /// <summary>Never invokes an adapter from malformed prose, an undeclared tool, an unknown schema or invalid arguments.</summary>
    [Theory]
    [InlineData("prose")]
    [InlineData("undeclared")]
    [InlineData("unknown")]
    [InlineData("wrong_argument")]
    public async Task RejectsUntrustedCallsBeforeAnyNativeEffect(string scenario)
    {
        var agent = Create();
        if (scenario == "wrong_argument") { _inference.Reply(Call("describe_tool", "name", "probe")); }
        _inference.Reply(scenario switch
        {
            "prose" => "I will execute. " + Call("describe_tool", "name", "probe"),
            "undeclared" => Call("probe", "text", "literal"),
            "unknown" => Call("describe_tool", "name", "unknown"),
            _ => Call("probe", "unknown", "literal"),
        });
        Assert.Null(await agent.AskAsync(Turn)); Assert.Equal(0, _tools.Calls);
        Assert.NotNull(agent.LastFailure); Assert.False(agent.LastFailure.EffectsMayHaveRun);
    }

    /// <summary>Stops tool authority after native denial even when the model attempts another argument variation.</summary>
    [Theory]
    [InlineData("blocked")]
    [InlineData("cancelled")]
    [InlineData("interrupted")]
    public async Task RefusalCannotBeBypassedWithAnotherModelCall(string status)
    {
        var agent = Create(); _tools.Result = new("Native refusal.") { Status = status, Failed = status == "failed" };
        _inference.Reply(Call("describe_tool", "name", "probe")); _inference.Reply(Call("probe", "text", "first")); _inference.Reply(Call("probe", "text", "second"));
        Assert.Null(await agent.AskAsync(Turn)); Assert.Equal(1, _tools.Calls);
        Assert.True(agent.LastFailure!.EffectsMayHaveRun); Assert.Equal(ConversationFailureKind.InvalidResponse, agent.LastFailure.Kind);
    }

    /// <summary>Allows diagnosis after an observed adapter failure while retaining receipts and refusing identical replays.</summary>
    [Fact]
    public async Task ObservedFailureCanBeFollowedByDifferentDiagnosticArguments()
    {
        var agent = Create(); _tools.Result = new("Observed nonzero exit result.") { Status = "failed", Failed = true };
        _inference.Reply(Call("describe_tool", "name", "probe")); _inference.Reply(Call("probe", "text", "first"));
        _inference.Reply(Call("probe", "text", "inspect actual diagnostic")); _inference.Answer("The observed operation failed; diagnostics were read.");
        Assert.NotNull(await agent.AskAsync(Turn)); Assert.Equal(2, _tools.Calls); Assert.Null(agent.LastFailure);
    }

    /// <summary>Does not replay effects after adapter failure or a subsequent generation failure.</summary>
    [Fact]
    public async Task FailureRetainsPossibleEffectsAndNeverReplays()
    {
        var agent = Create(); _inference.Reply(Call("describe_tool", "name", "probe")); _inference.Reply(Call("probe", "text", "literal"));
        _inference.LastFailure = new(ConversationFailureKind.ContextExceeded); _inference.Replies.Enqueue(_ => Task.FromResult<string?>(null));
        Assert.Null(await agent.AskAsync(Turn)); Assert.Equal(1, _tools.Calls);
        Assert.Equal(new ConversationFailure(ConversationFailureKind.ContextExceeded, true), agent.LastFailure);
    }

    /// <summary>Rejects duplicate discovery rather than burning a native task budget or looping forever.</summary>
    [Fact]
    public async Task DuplicateDiscoveryIsBoundedAndDoesNotGrantNativeAuthority()
    {
        var agent = Create(); _inference.Reply(Call("describe_tool", "name", "probe")); _inference.Reply(Call("describe_tool", "name", "probe"));
        Assert.Null(await agent.AskAsync(Turn)); Assert.Equal(ConversationFailureKind.OperationLimit, agent.LastFailure!.Kind); Assert.Equal(0, _tools.Calls);
    }

    /// <summary>Stops duplicate effects while still allowing a final report grounded in the actual retained observation.</summary>
    [Fact]
    public async Task RepeatedEffectIsNotDispatchedAgainAndCanReportRetainedResult()
    {
        var agent = Create(); _inference.Reply(Call("describe_tool", "name", "probe"));
        _inference.Reply(Call("probe", "text", "literal")); _inference.Reply(Call("probe", "text", "literal")); _inference.Answer("The observed value is 731.");
        Assert.Equal("The observed value is 731.", await agent.AskAsync(Turn)); Assert.Equal(1, _tools.Calls);
        Assert.Contains("no repeat was dispatched", _inference.Requests[^1].Exchange[^1].Text, StringComparison.Ordinal);
    }

    /// <summary>Keeps caller cancellation distinct from the provider deadline and prevents an effect after cancellation.</summary>
    [Fact]
    public async Task CancellationBeforeDispatchHasNoEffects()
    {
        var agent = Create(); using var cancellation = new CancellationTokenSource();
        _inference.Replies.Enqueue(_ => { cancellation.Cancel(); return Task.FromResult<string?>(Call("describe_tool", "name", "probe")); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => agent.AskAsync(Turn, cancellation.Token)); Assert.Equal(0, _tools.Calls);
    }

    /// <summary>Distinguishes a local deadline from caller cancellation and leaves uncertain effects explicit.</summary>
    [Fact]
    public async Task DeadlineAfterDispatchReportsTimeoutWithPriorEffects()
    {
        await _settings.UpdateAsync(_settings.Current with { GenerativeAi = _settings.Current.GenerativeAi with { Timeout = TimeSpan.FromSeconds(1) } });
        var agent = Create(); _inference.Reply(Call("describe_tool", "name", "probe")); _inference.Reply(Call("probe", "text", "literal"));
        _tools.Execute = async token => { await Task.Delay(Timeout.Infinite, token); return new("unreachable"); };
        Assert.Null(await agent.AskAsync(Turn)); Assert.Equal(1, _tools.Calls);
        Assert.Equal(new ConversationFailure(ConversationFailureKind.Timeout, true), agent.LastFailure);
    }

    /// <summary>Refuses a stale continuation without replacing the current native working turn.</summary>
    [Fact]
    public async Task StaleContinuationCannotStealCurrentTurn()
    {
        Guid original = Guid.NewGuid(); _memory.SetWorkingContext(new ConversationWorkingContext(original, "Current objective"));
        var agent = Create(); Assert.Null(await agent.AskAsync(Turn with { ContinuingTaskTurnId = Guid.NewGuid() }));
        Assert.Empty(_inference.Requests); Assert.Equal(original, _memory.WorkingContext.TurnId); Assert.Equal(0, _tools.Calls);
    }

    /// <summary>Resumes the original native identity without replacing its objective with generated recovery instructions.</summary>
    [Fact]
    public async Task ExplicitContinuationKeepsOriginalAuthorityContext()
    {
        Guid original = Guid.NewGuid(); _memory.SetWorkingContext(new ConversationWorkingContext(original, "Original user objective"));
        var agent = Create(); _inference.Reply(Call("describe_tool", "name", "probe")); _inference.Reply(Call("probe", "text", "read fresh evidence")); _inference.Answer("Observed 731.");
        Assert.Equal("Observed 731.", await agent.AskAsync(Turn with { ContinuingTaskTurnId = original }));
        Assert.Equal(1, _tools.Calls); Assert.Equal(original, _memory.WorkingContext.TurnId); Assert.Equal("Original user objective", _memory.WorkingContext.UserText);
    }

    /// <summary>Retains legacy text behavior without claiming unvalidated tool support.</summary>
    [Fact]
    public async Task LegacySmallModelRemainsTextOnly()
    {
        await _settings.UpdateAsync(_settings.Current with { GenerativeAi = _settings.Current.GenerativeAi with { LocalModel = "qwen2.5-0.5b-instruct-q4_k_m" } });
        Assert.Equal("Plain answer", await Create().AskAsync(Turn)); Assert.Empty(_inference.Requests); Assert.Equal(0, _tools.Calls);
    }

    /// <summary>Keeps supplied notes, whole retrieved evidence and skill discovery separate from native execution authority.</summary>
    [Fact]
    public void RecallMergesWholeEvidenceWithoutReplacingOriginalContext()
    {
        var original = Turn with { Memories = ["Explicit user note", "Whole retrieved fact"], SkillCatalogue = "Existing inert metadata" };
        var merged = original.WithRecalledContext("Whole retrieved fact", "New inert metadata");
        Assert.Equal(new[] { "Explicit user note", "Whole retrieved fact" }, merged.Memories);
        Assert.Equal("Existing inert metadata\nNew inert metadata", merged.SkillCatalogue);
        Assert.Equal(original.Memories, merged.WithRecalledContext("", "").Memories); Assert.Equal(original.TurnId, merged.TurnId);
    }

    /// <summary>Creates the domain orchestration fixture behind injected native boundaries.</summary>
    private LocalToolConversationEngine Create() => new(_inference, _tools, _memory, _settings);
    /// <summary>Builds one synthetic model call in the reviewed protocol.</summary>
    private static string Call(string name, string parameter, string value) => $"<tool_call>\n<function={name}>\n<parameter={parameter}>\n{value}\n</parameter>\n</function>\n</tool_call>";
}
