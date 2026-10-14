using Laura.Core.Abstractions;
using Laura.Core.Weather;

namespace Laura.Core.Conversation;

/// <summary>Supplies today's forecast for the city detected by the existing weather service.</summary>
public sealed class WeatherBriefingContributor(IWeatherProvider weather, ISettingsService settings, IClock clock) : IBriefingContributor
{
    /// <inheritdoc />
    public async Task<BriefingSection?> CollectAsync(CancellationToken cancellationToken)
    {
        WeatherReport? report = await weather.GetCurrentAsync(settings.Current.ResolveCulture(), cancellationToken).ConfigureAwait(false);
        if (report is null || string.IsNullOrWhiteSpace(report.Location)
            || report.ForecastDate != DateOnly.FromDateTime(clock.Now.DateTime)
            || string.IsNullOrWhiteSpace(report.MinimumTemperature) || string.IsNullOrWhiteSpace(report.MaximumTemperature)) { return null; }
        return new("Weather", "wttr.in", clock.Now, new
        {
            City = report.Location, Date = report.ForecastDate, Minimum = report.MinimumTemperature, Maximum = report.MaximumTemperature,
            LocationMethod = "City detected from the public IP address; daily forecast, not observed extremes.",
        });
    }
}
