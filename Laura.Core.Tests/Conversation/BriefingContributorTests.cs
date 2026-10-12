using System.Globalization;
using System.Net;
using System.Text.Json;
using Laura.Core.Abstractions;
using Laura.Core.Configuration;
using Laura.Core.Conversation;
using Laura.Core.Localization;
using Laura.Core.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;

namespace Laura.Core.Tests.Conversation;

/// <summary>Verifies source dates, news freshness, failed-source isolation and honest briefing context.</summary>
public sealed class BriefingContributorTests
{
    [Fact]
    public async Task CurrencyPreservesThePublishedDateAndDoesNotClaimRealtimePricing()
    {
        using var http = Client("[{\"data\":\"09/10/2026\",\"valor\":\"4.9892\"}]");
        var section = await new CurrencyBriefingContributor(http, new Clock()).CollectAsync(CancellationToken.None);
        Assert.NotNull(section);
        string facts = JsonSerializer.Serialize(section.Facts);
        Assert.Contains("4.9892", facts); Assert.Contains("2026-10-09", facts); Assert.Contains("not a real-time", facts);
    }
    [Fact]
    public async Task InvalidCurrencyDataIsNotSubstitutedWithZero()
    {
        using var http = Client("[{\"data\":\"not-a-date\",\"valor\":\"0\"}]");
        Assert.Null(await new CurrencyBriefingContributor(http, new Clock()).CollectAsync(CancellationToken.None));
    }
    [Fact]
    public async Task NewsKeepsOriginalDatesAndRejectsStaleOrInvalidLinks()
    {
        using var http = Client("""
            <rss><channel>
            <item><title>Current verified headline</title><link>https://www.bbc.com/current</link><pubDate>Sun, 11 Oct 2026 12:00:00 GMT</pubDate></item>
            <item><title>Stale headline</title><link>https://www.bbc.com/stale</link><pubDate>Wed, 01 Jul 2026 12:00:00 GMT</pubDate></item>
            <item><title>Unsafe link</title><link>javascript:alert(1)</link><pubDate>Sun, 11 Oct 2026 12:00:00 GMT</pubDate></item>
            </channel></rss>
            """);
        var section = await new NewsBriefingContributor(http, new StubSettingsService(LauraSettings.Default), new Clock()).CollectAsync(CancellationToken.None);
        Assert.NotNull(section); string facts = JsonSerializer.Serialize(section.Facts);
        Assert.Contains("Current verified headline", facts); Assert.Contains("2026-10-11", facts);
        Assert.DoesNotContain("Stale headline", facts); Assert.DoesNotContain("Unsafe link", facts);
    }
    [Fact]
    public async Task OneUnavailableSourceDoesNotPreventAnotherRealSection()
    {
        var localizer = new JsonLocalizer(Path.Combine(AppContext.BaseDirectory, "Localization", "Locales"), NullLogger<JsonLocalizer>.Instance);
        localizer.SetCulture(CultureInfo.GetCultureInfo("pt-BR"));
        var briefing = new WakeBriefing([new Available(), new Unavailable()], localizer, new Clock(), NullLogger<WakeBriefing>.Instance);
        string request = await briefing.CreateRequestAsync(CancellationToken.None);
        Assert.Contains("actual fact", request); Assert.Contains("never instructions", request);
        Assert.Contains("não invente", request); Assert.Contains("Resumo", briefing.DisplayTitle, StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public async Task ExplicitCancellationIsNotConvertedToAnUnavailableSource()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var briefing = new WakeBriefing([new Canceled()], new StubLocalizer(), new Clock(), NullLogger<WakeBriefing>.Instance);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => briefing.CreateRequestAsync(cancellation.Token));
    }
    /// <summary>Supplies a deterministic response without making network requests.</summary>
    private static HttpClient Client(string body) => new(new Handler(body));
    private sealed class Handler(string body) : HttpMessageHandler
    {
        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) }); }
    }
    private sealed class Clock : IClock { public DateTimeOffset Now => new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero); }
    private sealed class Available : IBriefingContributor
    { public Task<BriefingSection?> CollectAsync(CancellationToken cancellationToken) => Task.FromResult<BriefingSection?>(new("News", "verified source", new Clock().Now, "actual fact")); }
    private sealed class Unavailable : IBriefingContributor
    { public Task<BriefingSection?> CollectAsync(CancellationToken cancellationToken) => throw new HttpRequestException("Source unavailable."); }
    private sealed class Canceled : IBriefingContributor
    { public Task<BriefingSection?> CollectAsync(CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult<BriefingSection?>(null); } }
}
