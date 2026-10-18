using Laura.Core.Abstractions;
using Laura.Core.Weather;

namespace Laura.Core.Conversation;

/// <summary>Supplies today's forecast for the city detected by the existing weather service.</summary>
public sealed class WeatherBriefingContributor(IWeatherProvider weather, ISettingsService settings, IClock clock, WeatherGreetingFormatter formatter) : IBriefingContributor
{
    /// <inheritdoc />
    public async Task<BriefingSection?> CollectAsync(CancellationToken cancellationToken)
    {
        WeatherReport? report = await weather.GetCurrentAsync(settings.Current.ResolveCulture(), cancellationToken).ConfigureAwait(false);
        if (report is null || string.IsNullOrWhiteSpace(report.Location)) { return null; }
        string summary = formatter.Format(report);
        if (summary.Length == 0) { return null; }
        return new("Weather", "wttr.in", clock.Now, new
        {
            City = report.Location, Date = report.ForecastDate, Minimum = report.MinimumTemperature, Maximum = report.MaximumTemperature,
            SpokenSummary = summary,
            LocationMethod = settings.Current.WeatherLocation.Length == 0 ? "City detected from the public IP address." : "User-configured city.",
        });
    }
}
