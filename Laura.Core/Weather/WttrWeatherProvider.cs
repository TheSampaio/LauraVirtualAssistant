using System.Globalization;
using System.Text.Json;
using Laura.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace Laura.Core.Weather;

/// <summary>
/// Fetches the current weather from wttr.in, a free, key-less service that detects
/// the location from the caller's IP address.
///
/// No API key keeps the assistant self-contained; IP-based location keeps the skill
/// usable without asking the user to configure coordinates.
/// </summary>
public sealed class WttrWeatherProvider : IWeatherProvider
{
    private const string RequestUri = "https://wttr.in/?format=j1";

    private readonly HttpClient _httpClient;
    private readonly ILogger<WttrWeatherProvider> _logger;
    private readonly IClock _clock;

    /// <summary>
    /// Initializes the provider.
    ///
    /// Args:
    ///     httpClient: Client used for the request; its timeout bounds the wait.
    ///     logger: Destination for diagnostic logs.
    /// </summary>
    public WttrWeatherProvider(HttpClient httpClient, ILogger<WttrWeatherProvider> logger, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(clock);

        _httpClient = httpClient;
        _logger = logger;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<WeatherReport?> GetCurrentAsync(
        CultureInfo culture,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(culture);

        try
        {
            await using Stream stream = await _httpClient
                .GetStreamAsync(RequestUri, cancellationToken)
                .ConfigureAwait(false);

            using JsonDocument document = await JsonDocument
                .ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return Parse(document.RootElement, UsesImperialUnits(culture), DateOnly.FromDateTime(_clock.Now.DateTime));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or OperationCanceledException)
        {
            _logger.LogWarning(exception, "Could not fetch the weather from wttr.in.");
            return null;
        }
    }

    /// <summary>
    /// Extracts a concise report from the wttr.in JSON payload.
    ///
    /// Args:
    ///     root: Root element of the wttr.in response.
    ///     imperial: <see langword="true"/> to report in Fahrenheit and the imperial
    ///     wording, otherwise Celsius.
    ///     today: Local calendar day required for the daily forecast.
    ///
    /// Returns:
    ///     The parsed report, or <see langword="null"/> when the payload is missing
    ///     the expected fields.
    /// </summary>
    private static WeatherReport? Parse(JsonElement root, bool imperial, DateOnly today)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("current_condition", out JsonElement conditions)
            || conditions.ValueKind != JsonValueKind.Array
            || conditions.GetArrayLength() == 0)
        {
            return null;
        }

        JsonElement current = conditions[0];

        if (current.ValueKind != JsonValueKind.Object) { return null; }
        string description = ReadFirstValue(current, "weatherDesc");

        string temperature = FormatTemperature(current, imperial ? "temp_F" : "temp_C", imperial);
        string feelsLike = FormatTemperature(current, imperial ? "FeelsLikeF" : "FeelsLikeC", imperial);
        string location = ReadLocation(root);

        var report = new WeatherReport(location, description, temperature, feelsLike);
        if (root.TryGetProperty("weather", out JsonElement days) && days.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement day in days.EnumerateArray())
            {
                if (day.ValueKind != JsonValueKind.Object || !day.TryGetProperty("date", out JsonElement date)
                    || date.ValueKind != JsonValueKind.String
                    || !DateOnly.TryParseExact(date.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly forecastDate)
                    || forecastDate != today) { continue; }
                string minimum = FormatTemperature(day, imperial ? "mintempF" : "mintempC", imperial);
                string maximum = FormatTemperature(day, imperial ? "maxtempF" : "maxtempC", imperial);
                if (minimum.Length > 0 && maximum.Length > 0)
                { report = report with { ForecastDate = forecastDate, MinimumTemperature = minimum, MaximumTemperature = maximum }; }
                break;
            }
        }
        return report;
    }

    /// <summary>
    /// Formats a temperature field with its unit.
    ///
    /// Args:
    ///     current: Current-condition element.
    ///     field: Name of the temperature field to read.
    ///     imperial: <see langword="true"/> for Fahrenheit, otherwise Celsius.
    ///
    /// Returns:
    ///     The temperature with its unit, such as "20°C", or an empty string when
    ///     the field is absent.
    /// </summary>
    private static string FormatTemperature(JsonElement current, string field, bool imperial)
    {
        if (!current.TryGetProperty(field, out JsonElement value))
        {
            return string.Empty;
        }

        if (value.ValueKind != JsonValueKind.String
            || !decimal.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out decimal temperature)) { return string.Empty; }
        string unit = imperial ? "°F" : "°C";
        return $"{temperature.ToString(CultureInfo.InvariantCulture)}{unit}";
    }

    /// <summary>
    /// Reads the nearest-area name from the payload.
    ///
    /// Args:
    ///     root: Root element of the wttr.in response.
    ///
    /// Returns:
    ///     The area name, or an empty string when it is not present.
    /// </summary>
    private static string ReadLocation(JsonElement root)
    {
        if (!root.TryGetProperty("nearest_area", out JsonElement areas) || areas.ValueKind != JsonValueKind.Array || areas.GetArrayLength() == 0)
        {
            return string.Empty;
        }

        JsonElement area = areas[0];
        return ReadFirstValue(area, "areaName");
    }

    /// <summary>Reads optional provider text without assuming malformed values have the expected shape.</summary>
    private static string ReadFirstValue(JsonElement parent, string field) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(field, out JsonElement values)
        && values.ValueKind == JsonValueKind.Array && values.GetArrayLength() > 0
        && values[0].ValueKind == JsonValueKind.Object && values[0].TryGetProperty("value", out JsonElement value)
        && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    /// <summary>
    /// Decides whether a culture prefers imperial units.
    ///
    /// Args:
    ///     culture: Culture to inspect.
    ///
    /// Returns:
    ///     <see langword="true"/> for the handful of regions that use Fahrenheit.
    /// </summary>
    private static bool UsesImperialUnits(CultureInfo culture)
    {
        string region = culture.Name.Contains('-', StringComparison.Ordinal)
            ? culture.Name[(culture.Name.IndexOf('-', StringComparison.Ordinal) + 1)..]
            : culture.Name;

        return region.Equals("US", StringComparison.OrdinalIgnoreCase);
    }
}
