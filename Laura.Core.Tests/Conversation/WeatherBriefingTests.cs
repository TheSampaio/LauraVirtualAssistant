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
          "current_condition": [{"temp_C":"23","temp_F":"73","FeelsLikeC":"24","FeelsLikeF":"75","weatherCode":"113","localObsDateTime":"2026-10-14 11:00 PM","weatherDesc":[{"value":"Sunny"}]}],
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
        var contributor = new WeatherBriefingContributor(provider, settings, clock, new WeatherGreetingFormatter(localizer, clock));
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
        Assert.Contains("SpokenSummary", request);
    }

    [Theory]
    [InlineData("\"date\":\"2026-10-14\"", "\"date\":\"2026-10-15\"")]
    [InlineData("\"mintempC\":\"18\"", "\"mintempC\":\"\"")]
    [InlineData("\"maxtempC\":\"28\"", "\"maxtempC\":\"not-a-number\"")]
    [InlineData("\"maxtempC\":\"28\"", "\"maxtempC\":null")]
    [InlineData("São Paulo", "")]
    public async Task IncompleteOrWrongDayForecastDoesNotBecomeATemperatureClaim(string original, string replacement)
    {
        using var http = Client(Payload.Replace(original, replacement, StringComparison.Ordinal));
        var clock = new Clock();
        var provider = new WttrWeatherProvider(http, NullLogger<WttrWeatherProvider>.Instance, clock);
        var settings = new StubSettingsService(LauraSettings.Default with { Culture = "pt-BR" });
        var localizer = new JsonLocalizer(Path.Combine(AppContext.BaseDirectory, "Localization", "Locales"), NullLogger<JsonLocalizer>.Instance);
        var section = await new WeatherBriefingContributor(provider, settings, clock, new WeatherGreetingFormatter(localizer, clock)).CollectAsync(CancellationToken.None);
        if (replacement.Length == 0) { Assert.Null(section); return; }
        Assert.NotNull(section);
        using var facts = JsonDocument.Parse(JsonSerializer.Serialize(section.Facts));
        string minimum = facts.RootElement.GetProperty("Minimum").GetString() ?? "";
        string maximum = facts.RootElement.GetProperty("Maximum").GetString() ?? "";
        Assert.True(minimum.Length == 0 || maximum.Length == 0);
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

    /// <summary>The configured city is encoded in the request, and repeated reads reuse the fresh report.</summary>
    [Fact]
    public async Task ConfiguredCityUsesOneCachedRequestAndParsesOvernightRain()
    {
        const string payload = """
            {"current_condition":[{"weatherCode":"122","localObsDateTime":"2026-10-14 11:00 PM","weatherDesc":[{"value":"Overcast"}]}],
             "nearest_area":[{"areaName":[{"value":"São Paulo"}]}],
             "weather":[{"date":"2026-10-15","hourly":[{"time":"300","weatherCode":"386","chanceofrain":"80","chanceofthunder":"65","precipMM":"4.2"}]}]}
            """;
        var handler = new CountingHandler(payload);
        using var http = new HttpClient(handler);
        var settings = new StubSettingsService(LauraSettings.Default with { WeatherLocation = "São Paulo" });
        using var provider = new WttrWeatherProvider(http, NullLogger<WttrWeatherProvider>.Instance, new Clock(), settings);
        WeatherReport? first = await provider.GetCurrentAsync(CultureInfo.GetCultureInfo("pt-BR"));
        WeatherReport? second = await provider.GetCurrentAsync(CultureInfo.GetCultureInfo("pt-BR"));
        Assert.Same(first, second);
        Assert.Equal(1, handler.Requests);
        Assert.Contains("S%C3%A3o%20Paulo", handler.LastUri?.AbsoluteUri);
        Assert.Equal(WeatherCondition.Cloudy, first?.CurrentCondition);
        Assert.Equal(new DateTime(2026, 10, 14, 23, 0, 0), first?.ObservedAtLocal);
        WeatherForecastPeriod period = Assert.Single(first!.Forecast);
        Assert.Equal(WeatherCondition.Thunderstorm, period.Condition);
        Assert.Equal(80, period.RainChance);
        Assert.Equal(65, period.ThunderChance);
    }

    /// <summary>A nearby shower code is forecast context, not evidence of rain at the user's location now.</summary>
    [Fact]
    public async Task NearbyRainIsNotClassifiedAsObservedRain()
    {
        string payload = Payload.Replace("\"weatherCode\":\"113\"", "\"weatherCode\":\"176\"", StringComparison.Ordinal);
        using var http = Client(payload);
        using var provider = new WttrWeatherProvider(http, NullLogger<WttrWeatherProvider>.Instance, new Clock());
        WeatherReport? report = await provider.GetCurrentAsync(CultureInfo.GetCultureInfo("pt-BR"));
        Assert.NotNull(report);
        Assert.Null(report.CurrentCondition);
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
    /// <summary>Records URL selection and cache hits without network access.</summary>
    private sealed class CountingHandler(string body) : HttpMessageHandler
    {
        public int Requests { get; private set; }
        public Uri? LastUri { get; private set; }
        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            LastUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }
    // UTC is already tomorrow; the local date must still select October 14.
    private sealed class Clock : IClock { public DateTimeOffset Now => new(2026, 10, 14, 23, 30, 0, TimeSpan.FromHours(-3)); }
}
