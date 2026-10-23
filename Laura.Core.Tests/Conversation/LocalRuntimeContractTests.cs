using System.Text.Json;
using Laura.Core.Configuration;
using Laura.Core.Conversation;
using Laura.Core.Models;

namespace Laura.Core.Tests.Conversation;

/// <summary>Checks bounded local context, endpointing, migrated settings and honest HTTP failures without running native models.</summary>
public sealed class LocalRuntimeContractTests
{
    /// <summary>Structured Google authentication metadata enables safe fallback without reading free-form server messages.</summary>
    [Fact]
    public async Task GoogleStructuredInvalidKeyIsAuthenticationFailure()
    {
        using var response = new HttpResponseMessage(System.Net.HttpStatusCode.BadRequest) { Content = new StringContent("""{"error":{"code":400,"details":[{"reason":"API_KEY_INVALID"}]}}""") };
        var error = await Assert.ThrowsAsync<ConversationProviderException>(() => TextModelHttp.ReadAsync(response, CancellationToken.None));
        Assert.Equal(ConversationFailureKind.Authentication, error.Failure.Kind);
    }
    /// <summary>A future subscription-only provider fails closed on unknown billing, account-only connection and API overrides.</summary>
    [Fact]
    public void FutureSubscriptionAuthorizationRequiresOfficialBillingEvidence()
    {
        var verified = new ProviderAuthorization("claude", ProviderConnectionState.SubscriptionEligible, ProviderBillingMode.Subscription, true);
        Assert.True(verified.CanUseSubscriptionOnly);
        Assert.False((verified with { ApiKeyOverridePresent = true }).CanUseSubscriptionOnly);
        Assert.False((verified with { Billing = ProviderBillingMode.Unknown }).CanUseSubscriptionOnly);
        Assert.False((verified with { State = ProviderConnectionState.Connected }).CanUseSubscriptionOnly);
        Assert.False((verified with { OfficiallySupported = false }).CanUseSubscriptionOnly);
    }
    /// <summary>Schema discovery exposes exact reviewed arguments without executing or accepting invented capabilities.</summary>
    [Fact]
    public void ProgressiveSchemasRemainExactAndBounded()
    {
        object[] declarations = [new { functionDeclarations = Enumerable.Range(0, 30).Select(index => new { name = "probe_" + index, description = "Synthetic capability. Detailed policy stays in its exact schema.", parameters = new { type = "OBJECT", properties = new { proof = new { type = "STRING" } } } }).ToArray() }];
        var catalogue = new TextToolCatalogue(declarations);
        Assert.Empty(catalogue.Selected); Assert.Contains("probe_29", catalogue.Summary, StringComparison.Ordinal);
        Assert.True(catalogue.Select(["probe_2", "probe_29"])); Assert.False(catalogue.Select(["fabricated_tool"]));
        string json = JsonSerializer.Serialize(catalogue.Declarations);
        Assert.Contains("probe_29", json, StringComparison.Ordinal); Assert.Contains("proof", json, StringComparison.Ordinal); Assert.DoesNotContain("probe_28", json, StringComparison.Ordinal);
        Assert.False(catalogue.Select(Enumerable.Range(0, 9).Select(index => "probe_" + index)));
    }
    /// <summary>Silence or a short impulse cannot be transcribed as a valid utterance.</summary>
    [Fact]
    public void EndpointRequiresSpeechAndEndsAfterPause()
    {
        var detector = new SpeechEndpointDetector(650);
        Assert.False(detector.Observe(0, 1000)); Assert.False(detector.HasSpeech);
        Assert.False(detector.Observe(.1f, 80)); Assert.False(detector.HasSpeech);
        Assert.False(detector.Observe(.1f, 80)); Assert.True(detector.HasSpeech);
        Assert.False(detector.Observe(0, 640)); Assert.True(detector.Observe(0, 40));
    }
    /// <summary>Proxy HTML errors retain the HTTP failure category without exposing remote message text.</summary>
    [Theory]
    [InlineData(429, ConversationFailureKind.CapacityLimit)]
    [InlineData(503, ConversationFailureKind.Unavailable)]
    public async Task NonJsonHttpFailureRetainsStatus(int status, ConversationFailureKind expected)
    {
        using var response = new HttpResponseMessage((System.Net.HttpStatusCode)status) { Content = new StringContent("<html>private upstream details</html>") };
        var error = await Assert.ThrowsAsync<ConversationProviderException>(() => TextModelHttp.ReadAsync(response, CancellationToken.None));
        Assert.Equal(expected, error.Failure.Kind); Assert.Equal(status, error.Failure.HttpStatus); Assert.DoesNotContain("private", error.Message, StringComparison.Ordinal);
    }
    /// <summary>Local limits and voice identities are sanitized without granting local or cloud enablement.</summary>
    [Fact]
    public void LocalOptionsKeepCpuBudgetAndLanguageVoices()
    {
        var options = new LocalModelOptions { ContextTokens = int.MaxValue, Threads = 999, GpuLayers = -1, MaximumDifficulty = TaskDifficulty.Max }.Sanitized();
        Assert.False(options.Enabled); Assert.Equal(32768, options.ContextTokens); Assert.InRange(options.Threads, 1, 8); Assert.Equal(-1, options.GpuLayers);
        Assert.NotEqual(TaskDifficulty.Max, options.MaximumDifficulty);
        var voice = new LocalVoiceOptions { PortugueseVoice = "Aoede", EnglishVoice = "cloud-voice" }.Sanitized();
        Assert.Equal("pt_dii", voice.PortugueseVoice); Assert.Equal("af_heart", voice.EnglishVoice);
    }
}
