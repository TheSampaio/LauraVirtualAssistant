using System.Net;
using System.Text;
using Laura.Core.Models;

namespace Laura.Core.Tests.Models;

/// <summary>Exercises authenticated provider discovery without live credentials or network access.</summary>
public sealed class ProviderModelCatalogTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "laura-provider-catalogue-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task StructuredGoogleInvalidKeyIsDistinguishedFromAProviderOutage()
    {
        using var transport = new Transport(); using var http = new HttpClient(transport); using var catalogue = new ProviderModelCatalog(http, _directory);
        transport.Responses.Enqueue(new(HttpStatusCode.BadRequest) { Content = new StringContent("""{"error":{"details":[{"reason":"API_KEY_INVALID"}]}}""") });
        var result = await catalogue.ListAsync("gemini", "synthetic-invalid-key");
        Assert.Equal("invalid_credentials", result.Error); Assert.True(result.Stale); Assert.Empty(result.Models);
    }
    [Fact]
    public void ReviewedDeprecationDoesNotInventTheStatusOfAnUnreviewedFutureModel()
    {
        Assert.True(ProviderModelCompatibility.IsDeprecated("gpt-4.1-nano"));
        Assert.True(ProviderModelCompatibility.IsDeprecated("gpt-5-mini-2025-08-07"));
        Assert.False(ProviderModelCompatibility.IsDeprecated("gpt-5.4-mini"));
        Assert.False(ProviderModelCompatibility.IsDeprecated("future-model"));
    }
    [Fact]
    public async Task GeminiPaginatesAndNeverInfersLiveFromOrdinaryGenerationOrAnUnreviewedName()
    {
        using var transport = new Transport(); using var http = new HttpClient(transport);
        transport.Responses.Enqueue(Json("""{"models":[{"name":"models/gemini-3.1-flash-live-preview","displayName":"Reviewed Live"},{"name":"models/gemini-text","supportedGenerationMethods":["generateContent"]}],"nextPageToken":"page + two"}"""));
        transport.Responses.Enqueue(Json("""{"models":[{"name":"models/gemini-unreviewed-live","supportedGenerationMethods":["generateContent","bidiGenerateContent"]},{"name":"models/gemini-embedding-test","supportedGenerationMethods":["embedContent"]}]}"""));
        using var catalogue = new ProviderModelCatalog(http, _directory); var result = await catalogue.ListAsync("gemini", "synthetic-google-key");
        Assert.Equal(4, result.Models.Count); Assert.False(result.Stale); Assert.Null(result.Error);
        Assert.True(result.Models.Single(model => model.Id == "gemini-3.1-flash-live-preview").Capabilities.HasFlag(ModelCapabilities.LiveAudio));
        Assert.False(result.Models.Single(model => model.Id == "gemini-unreviewed-live").Capabilities.HasFlag(ModelCapabilities.LiveAudio));
        Assert.Equal(ModelCapabilities.Embedding, result.Models.Single(model => model.Id == "gemini-embedding-test").Capabilities);
        Assert.Contains("pageToken=page%20%2B%20two", transport.Requests[1].Uri);
        Assert.All(transport.Requests, request => { Assert.Equal("synthetic-google-key", request.Key); Assert.DoesNotContain("synthetic-google-key", request.Uri); });
        Assert.DoesNotContain("synthetic-google-key", await File.ReadAllTextAsync(Assert.Single(Directory.GetFiles(_directory))));
    }

    [Fact]
    public async Task OpenAiCapabilitiesAreReviewedAndDoNotDependOnTheGptPrefix()
    {
        using var transport = new Transport(); using var http = new HttpClient(transport);
        transport.Responses.Enqueue(Json("""{"data":[{"id":"o3"},{"id":"gpt-5.4-mini-2026-03-17"},{"id":"gpt-image-1"},{"id":"text-embedding-3-small"},{"id":"gpt-unverified"}]}"""));
        using var catalogue = new ProviderModelCatalog(http, _directory); var result = await catalogue.ListAsync("openai", "synthetic-openai-key");
        Assert.Equal("Bearer synthetic-openai-key", Assert.Single(transport.Requests).Key);
        Assert.True(result.Models.Single(model => model.Id == "o3").Capabilities.HasFlag(ModelCapabilities.Tools));
        Assert.True(result.Models.Single(model => model.Id == "gpt-5.4-mini-2026-03-17").Capabilities.HasFlag(ModelCapabilities.Images));
        Assert.All(result.Models.Where(model => model.Id is "gpt-image-1" or "text-embedding-3-small" or "gpt-unverified"), model => Assert.Equal(ModelCapabilities.None, model.Capabilities));
    }

    [Theory]
    [InlineData(401, "invalid_credentials")]
    [InlineData(429, "rate_limit")]
    [InlineData(503, "provider_unavailable")]
    public async Task FailedRefreshUsesTheSameCredentialCacheAndNeverPublishesAPartialCatalogue(int status, string error)
    {
        using var transport = new Transport(); using var http = new HttpClient(transport); var clock = new TestTime();
        using var catalogue = new ProviderModelCatalog(http, _directory, clock);
        transport.Responses.Enqueue(Json("""{"data":[{"id":"gpt-5.4-mini"}]}"""));
        var original = await catalogue.ListAsync("openai", "key-one");
        Assert.False((await catalogue.ListAsync("openai", "key-one")).Stale); Assert.Single(transport.Requests);
        clock.Now = clock.Now.AddDays(2); transport.Responses.Enqueue(new((HttpStatusCode)status));
        var stale = await catalogue.ListAsync("openai", "key-one"); Assert.True(stale.Stale); Assert.Equal(error, stale.Error); Assert.Equal(original.Models, stale.Models); Assert.Equal(original.FetchedAt, stale.FetchedAt);
        transport.Responses.Enqueue(new((HttpStatusCode)status)); var changed = await catalogue.ListAsync("openai", "key-two"); Assert.Empty(changed.Models);
        transport.Responses.Enqueue(new(HttpStatusCode.ServiceUnavailable));
        using var restarted = new ProviderModelCatalog(http, _directory, clock); Assert.Single((await restarted.ListAsync("openai", "key-one")).Models);
    }

    [Fact]
    public async Task InvalidPagesDoNotReplaceTheLastValidCacheAndCallerCancellationPropagates()
    {
        using var transport = new Transport(); using var http = new HttpClient(transport); using var catalogue = new ProviderModelCatalog(http, _directory);
        transport.Responses.Enqueue(Json("""{"models":[{"name":"models/gemini-3.1-flash-live-preview"}]}""")); await catalogue.ListAsync("gemini", "key");
        transport.Responses.Enqueue(Json("""{"models":[{"name":"models/gemini-partial"}],"nextPageToken":"same"}"""));
        transport.Responses.Enqueue(Json("""{"models":[],"nextPageToken":"same"}"""));
        var result = await catalogue.ListAsync("gemini", "key", true); Assert.True(result.Stale); Assert.Equal("invalid_response", result.Error);
        Assert.Equal("gemini-3.1-flash-live-preview", Assert.Single(result.Models).Id);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => catalogue.ListAsync("gemini", "key", true, cancelled.Token));
    }

    /// <summary>Creates an isolated UTF-8 provider response.</summary>
    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    /// <inheritdoc />
    public void Dispose() { if (Directory.Exists(_directory)) { Directory.Delete(_directory, true); } }

    /// <summary>Records only synthetic headers/URLs and returns deterministic account catalogues.</summary>
    private sealed class Transport : HttpMessageHandler
    {
        internal Queue<HttpResponseMessage> Responses { get; } = new();
        internal List<(string Uri, string Key)> Requests { get; } = [];
        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.RequestUri!.AbsoluteUri, request.Headers.TryGetValues("x-goog-api-key", out var key) ? key.Single() : request.Headers.Authorization!.ToString()));
            return Task.FromResult(Responses.Dequeue());
        }
    }
    /// <summary>Advances expiry deterministically without delaying a test.</summary>
    private sealed class TestTime : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = new(2026, 10, 16, 0, 0, 0, TimeSpan.Zero);
        /// <inheritdoc />
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
