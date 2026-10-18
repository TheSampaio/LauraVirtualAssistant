using Laura.Core.Abstractions;
using Laura.Core.Localization;

namespace Laura.Core.Weather;

/// <summary>Turns observed and dated forecast facts into a short local-time greeting sentence.</summary>
public sealed class WeatherGreetingFormatter(ILocalizer localizer, IClock clock)
{
    /// <summary>Composes a supported weather summary without inventing missing observations.</summary>
    public string Format(WeatherReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        DateTime now = clock.Now.DateTime;
        var sentences = new List<string>();
        WeatherCondition? current = report.ObservedAtLocal is { } observed
            && now >= observed.AddMinutes(-10) && now - observed <= TimeSpan.FromHours(2)
                ? report.CurrentCondition : null;
        if (current is { } observedCondition)
        {
            string conditionKey = observedCondition == WeatherCondition.Sunny && (clock.Now.Hour < 6 || clock.Now.Hour >= 18)
                ? LocalizationKeys.Greeting.WeatherClear : ConditionKey(observedCondition);
            sentences.Add(localizer.Get(LocalizationKeys.Greeting.WeatherCurrent, localizer.Get(conditionKey)));
        }

        WeatherForecastPeriod? upcoming = report.Forecast
            .Where(period => period.LocalTime > now && period.LocalTime <= now.AddHours(12))
            .OrderByDescending(Severity).ThenBy(period => period.LocalTime).FirstOrDefault(period => Severity(period) > 0);
        if (upcoming is not null && Severity(upcoming) > CurrentSeverity(current))
        {
            string when = localizer.Get(upcoming.LocalTime <= now.AddHours(4)
                ? LocalizationKeys.Greeting.WeatherNextHours : PeriodKey(upcoming.LocalTime));
            string key = upcoming.Condition == WeatherCondition.Thunderstorm || upcoming.ThunderChance >= 40
                ? LocalizationKeys.Greeting.WeatherThunder
                : upcoming.Condition == WeatherCondition.HeavyRain || upcoming.PrecipitationMillimeters >= 7
                    ? LocalizationKeys.Greeting.WeatherHeavyRain : LocalizationKeys.Greeting.WeatherRain;
            sentences.Add(localizer.Get(key, when));
        }
        else if (current is null
            && report.Forecast.FirstOrDefault(period => period.LocalTime > now && period.LocalTime <= now.AddHours(6) && period.Condition is not null) is { } predicted)
        {
            string conditionKey = predicted.Condition == WeatherCondition.Sunny && (predicted.LocalTime.Hour < 6 || predicted.LocalTime.Hour >= 18)
                ? LocalizationKeys.Greeting.WeatherClear : ConditionKey(predicted.Condition!.Value);
            string when = localizer.Get(predicted.LocalTime <= now.AddHours(4)
                ? LocalizationKeys.Greeting.WeatherNextHours : PeriodKey(predicted.LocalTime));
            sentences.Add(localizer.Get(LocalizationKeys.Greeting.WeatherForecastCondition, localizer.Get(conditionKey), when));
        }
        else if (current is not (WeatherCondition.Rain or WeatherCondition.HeavyRain or WeatherCondition.Thunderstorm)
            && HasSupportedDryForecast(report.Forecast, now))
        {
            sentences.Add(localizer.Get(LocalizationKeys.Greeting.WeatherNoRain));
        }

        if (report.ForecastDate == DateOnly.FromDateTime(now) && clock.Now.Hour < 18)
        {
            if (report.MinimumTemperature.Length > 0 && report.MaximumTemperature.Length > 0)
            { sentences.Add(localizer.Get(LocalizationKeys.Greeting.WeatherTemperatures, report.MinimumTemperature, report.MaximumTemperature)); }
            else if (report.MinimumTemperature.Length > 0)
            { sentences.Add(localizer.Get(LocalizationKeys.Greeting.WeatherMinimum, report.MinimumTemperature)); }
            else if (report.MaximumTemperature.Length > 0)
            { sentences.Add(localizer.Get(LocalizationKeys.Greeting.WeatherMaximum, report.MaximumTemperature)); }
        }
        if (report.Alert.Length > 0) { sentences.Add(localizer.Get(LocalizationKeys.Greeting.WeatherAlert)); }
        return string.Join(" ", sentences);
    }

    /// <summary>Chooses the strongest supported near-term risk while preserving forecast uncertainty.</summary>
    private static int Severity(WeatherForecastPeriod period) => period.Condition == WeatherCondition.Thunderstorm || period.ThunderChance >= 40
        ? 3 : period.Condition == WeatherCondition.HeavyRain || period.PrecipitationMillimeters >= 7
            ? 2 : period.Condition == WeatherCondition.Rain || period.RainChance >= 40 ? 1 : 0;

    /// <summary>Prevents repeating a forecast risk that is already observed now.</summary>
    private static int CurrentSeverity(WeatherCondition? condition) => condition switch
    {
        WeatherCondition.Thunderstorm => 3,
        WeatherCondition.HeavyRain => 2,
        WeatherCondition.Rain => 1,
        _ => 0,
    };

    /// <summary>Requires multiple dated periods before saying rain is not expected over the next several hours.</summary>
    private static bool HasSupportedDryForecast(IReadOnlyList<WeatherForecastPeriod> forecast, DateTime now)
    {
        WeatherForecastPeriod[] periods = [.. forecast.Where(period => period.LocalTime > now && period.LocalTime <= now.AddHours(8)).OrderBy(period => period.LocalTime)];
        return periods.Length >= 2 && periods[0].LocalTime <= now.AddHours(4) && periods[^1].LocalTime >= now.AddHours(5)
            && periods.All(period => period.RainChance is <= 20 && period.ThunderChance is <= 20
                && period.Condition is not (WeatherCondition.Rain or WeatherCondition.HeavyRain or WeatherCondition.Thunderstorm));
    }

    /// <summary>Maps a known code to its translated observed-condition phrase.</summary>
    private static string ConditionKey(WeatherCondition condition) => condition switch
    {
        WeatherCondition.Sunny => LocalizationKeys.Greeting.WeatherSunny,
        WeatherCondition.PartlyCloudy => LocalizationKeys.Greeting.WeatherPartlyCloudy,
        WeatherCondition.Cloudy => LocalizationKeys.Greeting.WeatherCloudy,
        WeatherCondition.Rain => LocalizationKeys.Greeting.WeatherRaining,
        WeatherCondition.HeavyRain => LocalizationKeys.Greeting.WeatherHeavyRaining,
        WeatherCondition.Thunderstorm => LocalizationKeys.Greeting.WeatherStorming,
        _ => LocalizationKeys.Greeting.WeatherSnowing,
    };

    /// <summary>Names the forecast period relative to the user's local calendar day.</summary>
    private string PeriodKey(DateTime forecast) => forecast.Date > clock.Now.Date
        ? forecast.Hour < 6 ? LocalizationKeys.Greeting.WeatherOvernight : LocalizationKeys.Greeting.WeatherTomorrowMorning
        : forecast.Hour < 12 ? LocalizationKeys.Greeting.WeatherMorning
        : forecast.Hour < 18 ? LocalizationKeys.Greeting.WeatherAfternoon : LocalizationKeys.Greeting.WeatherEvening;
}
