using System.Globalization;
using System.Net;
using System.Text.Json;
using Laura.Core.Abstractions;
using Laura.Core.Configuration;
using Laura.Core.Conversation;
using Laura.Core.Localization;
using Laura.Core.Tests.TestDoubles;
using Laura.Core.Weather;
using Microsoft.Extensions.Logging.Abstractions;

namespace Laura.Core.Tests.Conversation;

/// <summary>Verifies daily forecast dates, units, failure isolation and startup prompt content without network access.</summary>
public sealed class WeatherBriefingTests
{
    private const string Payload = """
        {
          "current_condition": [{"temp_C":"23","temp_F":"73","FeelsLikeC":"24","FeelsLikeF":"75","weatherDesc":[{"value":"Sunny"}]}],
          "nearest_area": [{"areaName":[{"value":"São Paulo"}]}],
          "weather": [
            {"date":"2026-10-13","mintempC":"1","maxtempC":"2","mintempF":"34","maxtempF":"36"},
            {"date":"2026-10-14","mintempC":"18","maxtempC":"28","mintempF":"64","maxtempF":"82"}
          ]
        }
        """;

    [Theory]
    [InlineData("pt-BR", "18°C", "28°C")]
    [InlineData("en-US", "64°F", "82°F")]
    public async Task StartupIncludesTodayForTheDetectedCityInTheSelectedUnits(string culture, string minimum, string maximum)
    {
        using var http = Client(Payload);
        var clock = new Clock();
        var provider = new WttrWeatherProvider(http, NullLogger<WttrWeatherProvider>.Instance, clock);
        var settings = new StubSettingsService(LauraSettings.Default with { Culture = culture });
        var localizer = new JsonLocalizer(Path.Combine(AppContext.BaseDirectory, "Localization", "Locales"), NullLogger<JsonLocalizer>.Instance);
        localizer.SetCulture(CultureInfo.GetCultureInfo(culture));
        var contributor = new WeatherBriefingContributor(provider, settings, clock);
        var section = await contributor.CollectAsync(CancellationToken.None);
        Assert.NotNull(section);
        using var facts = JsonDocument.Parse(JsonSerializer.Serialize(section.Facts));
        Assert.Equal("São Paulo", facts.RootElement.GetProperty("City").GetString());
        Assert.Equal("2026-10-14", facts.RootElement.GetProperty("Date").GetString());
        Assert.Equal(minimum, facts.RootElement.GetProperty("Minimum").GetString());
        Assert.Equal(maximum, facts.RootElement.GetProperty("Maximum").GetString());
        string request = await new WakeBriefing([contributor], localizer, clock, NullLogger<WakeBriefing>.Instance)
            .CreateRequestAsync("Hello", CancellationToken.None);
        Assert.Contains("wttr.in", request);
        Assert.Contains("Minimum", request);
        Assert.Contains(culture == "pt-BR" ? "temperatura mínima e máxima" : "minimum and maximum temperatures", request);
    }

    [Theory]
    [InlineData("2026-10-14", "2026-10-15")]
    [InlineData("\"mintempC\":\"18\"", "\"mintempC\":\"\"")]
    [InlineData("\"maxtempC\":\"28\"", "\"maxtempC\":\"not-a-number\"")]
    [InlineData("\"maxtempC\":\"28\"", "\"maxtempC\":null")]
    [InlineData("São Paulo", "")]
    public async Task IncompleteOrWrongDayForecastDoesNotEnterTheBriefing(string original, string replacement)
    {
        using var http = Client(Payload.Replace(original, replacement, StringComparison.Ordinal));
        var clock = new Clock();
        var provider = new WttrWeatherProvider(http, NullLogger<WttrWeatherProvider>.Instance, clock);
        var settings = new StubSettingsService(LauraSettings.Default with { Culture = "pt-BR" });
        Assert.Null(await new WeatherBriefingContributor(provider, settings, clock).CollectAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"current_condition\":null}")]
    [InlineData("{\"current_condition\":[null]}")]
    [InlineData("not-json")]
    public async Task MalformedWeatherIsUnavailable(string payload)
    {
        using var http = Client(payload);
        var provider = new WttrWeatherProvider(http, NullLogger<WttrWeatherProvider>.Instance, new Clock());
        Assert.Null(await provider.GetCurrentAsync(CultureInfo.GetCultureInfo("pt-BR")));
    }

    [Fact]
    public async Task CurrentWeatherStillWorksWithoutADailyForecast()
    {
        using var http = Client(Payload.Replace("\"weather\":", "\"absent\":", StringComparison.Ordinal));
        var provider = new WttrWeatherProvider(http, NullLogger<WttrWeatherProvider>.Instance, new Clock());
        WeatherReport? report = await provider.GetCurrentAsync(CultureInfo.GetCultureInfo("pt-BR"));
        Assert.NotNull(report); Assert.Equal("23°C", report.Temperature); Assert.Null(report.ForecastDate);
    }

    [Fact]
    public async Task ExplicitCancellationIsPreserved()
    {
        using var http = Client(Payload);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var provider = new WttrWeatherProvider(http, NullLogger<WttrWeatherProvider>.Instance, new Clock());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.GetCurrentAsync(CultureInfo.GetCultureInfo("pt-BR"), cancellation.Token));
    }

    private static HttpClient Client(string body) => new(new Handler(body));
    private sealed class Handler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }
    // UTC is already tomorrow; the local date must still select October 14.
    private sealed class Clock : IClock { public DateTimeOffset Now => new(2026, 10, 14, 23, 30, 0, TimeSpan.FromHours(-3)); }
}
