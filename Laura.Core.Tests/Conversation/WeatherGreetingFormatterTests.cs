using System.Globalization;
using Laura.Core.Abstractions;
using Laura.Core.Localization;
using Laura.Core.Weather;
using Microsoft.Extensions.Logging.Abstractions;

namespace Laura.Core.Tests.Conversation;

/// <summary>Checks observed conditions, forecast timing, severity and omitted data.</summary>
public sealed class WeatherGreetingFormatterTests
{
    /// <summary>A morning observation stays separate from an afternoon forecast.</summary>
    [Fact]
    public void MorningDescribesObservedCloudsAndPossibleAfternoonRain()
    {
        var clock = new Clock(9);
        string summary = Formatter(clock).Format(Report(clock, WeatherCondition.Cloudy, 15, WeatherCondition.Rain, 70));
        Assert.Contains("No momento, o tempo está nublado", summary);
        Assert.Contains("possibilidade de chuva à tarde", summary);
        Assert.Contains("18°C e 28°C", summary);
    }

    /// <summary>An afternoon forecast describes evening thunderstorms as a risk.</summary>
    [Fact]
    public void AfternoonUsesFutureTenseForEveningThunderstorms()
    {
        var clock = new Clock(14);
        string summary = Formatter(clock).Format(Report(clock, WeatherCondition.Sunny, 19, WeatherCondition.Thunderstorm, 60));
        Assert.Contains("tempo está ensolarado", summary);
        Assert.Contains("possibilidade de trovoadas à noite", summary);
        Assert.DoesNotContain("tempo está com trovoadas", summary);
    }

    /// <summary>At night a next-day early forecast is identified as overnight.</summary>
    [Fact]
    public void NightNamesTheOvernightForecastAndSkipsTodaysTemperatureRange()
    {
        var clock = new Clock(21);
        WeatherReport report = Report(clock, WeatherCondition.Cloudy, 3, WeatherCondition.Rain, 80) with
        { Forecast = [new(clock.Now.Date.AddDays(1).AddHours(3), WeatherCondition.Rain, 80, 0, 2)] };
        string summary = Formatter(clock).Format(report);
        Assert.Contains("tempo está nublado", summary);
        Assert.Contains("durante a madrugada", summary);
        Assert.DoesNotContain("temperatura deve variar", summary);
    }

    /// <summary>No rain claim requires explicit low probabilities for all near-term periods.</summary>
    [Fact]
    public void MissingProbabilitiesDoNotBecomeANoRainClaim()
    {
        var clock = new Clock(20);
        WeatherReport report = Report(clock, WeatherCondition.Sunny, 22, WeatherCondition.Cloudy, null);
        string summary = Formatter(clock).Format(report);
        Assert.Contains("céu limpo", summary);
        Assert.DoesNotContain("Não há previsão de chuva", summary);
    }

    /// <summary>One dry forecast slot cannot support a claim about the next several hours.</summary>
    [Fact]
    public void OneDryPeriodDoesNotSupportANoRainClaim()
    {
        var clock = new Clock(20);
        string summary = Formatter(clock).Format(Report(clock, WeatherCondition.Cloudy, 23, WeatherCondition.Cloudy, 0));
        Assert.DoesNotContain("Não há previsão de chuva", summary);
    }

    /// <summary>Several explicitly dry periods support a short no-rain outlook.</summary>
    [Fact]
    public void MultipleDryPeriodsSupportANoRainOutlook()
    {
        var clock = new Clock(10);
        WeatherReport report = Report(clock, WeatherCondition.Cloudy, 12, WeatherCondition.Cloudy, 0) with
        { Forecast = [new(clock.Now.Date.AddHours(12), WeatherCondition.Cloudy, 0, 0, 0),
            new(clock.Now.Date.AddHours(15), WeatherCondition.Cloudy, 10, 0, 0)] };
        string summary = Formatter(clock).Format(report);
        Assert.Contains("Não há previsão de chuva nas próximas horas", summary);
    }

    /// <summary>A supported forecast can still describe the day when the current observation is absent.</summary>
    [Fact]
    public void ForecastConditionIsLabeledAsForecastWhenObservationIsUnavailable()
    {
        var clock = new Clock(8);
        string summary = Formatter(clock).Format(Report(clock, null, 10, WeatherCondition.PartlyCloudy, null));
        Assert.Contains("A previsão indica tempo parcialmente nublado nas próximas horas", summary);
        Assert.DoesNotContain("No momento", summary);
    }

    /// <summary>An actual provider alert is signaled without repeating untrusted alert text.</summary>
    [Fact]
    public void AlertIsBriefAndDoesNotRepeatProviderText()
    {
        var clock = new Clock(10);
        WeatherReport report = Report(clock, WeatherCondition.Cloudy, 15, null, null) with { Alert = "Untrusted detail" };
        string summary = Formatter(clock).Format(report);
        Assert.Contains("alerta meteorológico", summary);
        Assert.DoesNotContain("Untrusted detail", summary);
    }

    /// <summary>A matching current and near-term rain condition is mentioned only once.</summary>
    [Fact]
    public void ContinuingRainDoesNotRepeatTheSameCondition()
    {
        var clock = new Clock(14);
        string summary = Formatter(clock).Format(Report(clock, WeatherCondition.Rain, 16, WeatherCondition.Rain, 80));
        Assert.Contains("tempo está chuvoso", summary);
        Assert.DoesNotContain("possibilidade de chuva", summary);
    }

    /// <summary>A stale observation cannot be spoken as the current sky condition.</summary>
    [Fact]
    public void StaleObservationIsNotPresentedAsCurrent()
    {
        var clock = new Clock(14);
        WeatherReport report = Report(clock, WeatherCondition.Sunny, 19, WeatherCondition.Rain, 70) with
        { ObservedAtLocal = clock.Now.DateTime.AddHours(-6) };
        string summary = Formatter(clock).Format(report);
        Assert.DoesNotContain("No momento", summary);
        Assert.Contains("possibilidade de chuva à noite", summary);
    }

    /// <summary>A lone valid daily extreme is spoken without inventing its missing counterpart.</summary>
    [Fact]
    public void SingleAvailableTemperatureIsUsed()
    {
        var clock = new Clock(9);
        WeatherReport report = Report(clock, WeatherCondition.Cloudy, 15, null, null) with { MaximumTemperature = "" };
        string summary = Formatter(clock).Format(report);
        Assert.Contains("mínima prevista para hoje é de 18°C", summary);
        Assert.DoesNotContain("temperatura deve variar", summary);
    }

    /// <summary>Creates a predictable provider report with a dated forecast period.</summary>
    private static WeatherReport Report(Clock clock, WeatherCondition? current, int hour, WeatherCondition? forecast, int? rainChance) =>
        new("São Paulo", "", "23°C", "24°C")
        {
            CurrentCondition = current,
            ObservedAtLocal = clock.Now.DateTime,
            ForecastDate = DateOnly.FromDateTime(clock.Now.DateTime),
            MinimumTemperature = "18°C",
            MaximumTemperature = "28°C",
            Forecast = [new(clock.Now.Date.AddHours(hour), forecast, rainChance, forecast == WeatherCondition.Thunderstorm ? 70 : 0, 1)],
        };

    /// <summary>Loads the production Portuguese phrases for a stable voice summary.</summary>
    private static WeatherGreetingFormatter Formatter(Clock clock)
    {
        var localizer = new JsonLocalizer(Path.Combine(AppContext.BaseDirectory, "Localization", "Locales"), NullLogger<JsonLocalizer>.Instance);
        localizer.SetCulture(CultureInfo.GetCultureInfo("pt-BR"));
        return new(localizer, clock);
    }

    /// <summary>Supplies a fixed local time.</summary>
    private sealed class Clock(int hour) : IClock { public DateTimeOffset Now => new(2026, 10, 14, hour, 0, 0, TimeSpan.FromHours(-3)); }
}
