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

    /// <summary>
    /// Initializes the provider.
    ///
    /// Args:
    ///     httpClient: Client used for the request; its timeout bounds the wait.
    ///     logger: Destination for diagnostic logs.
    /// </summary>
    public WttrWeatherProvider(HttpClient httpClient, ILogger<WttrWeatherProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClient = httpClient;
        _logger = logger;
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

            return Parse(document.RootElement, UsesImperialUnits(culture));
        }
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
    ///
    /// Returns:
    ///     The parsed report, or <see langword="null"/> when the payload is missing
    ///     the expected fields.
    /// </summary>
    private static WeatherReport? Parse(JsonElement root, bool imperial)
    {
        if (!root.TryGetProperty("current_condition", out JsonElement conditions)
            || conditions.GetArrayLength() == 0)
        {
            return null;
        }

        JsonElement current = conditions[0];

        string description = current.TryGetProperty("weatherDesc", out JsonElement desc) && desc.GetArrayLength() > 0
            ? desc[0].GetProperty("value").GetString() ?? string.Empty
            : string.Empty;

        string temperature = FormatTemperature(current, imperial ? "temp_F" : "temp_C", imperial);
        string feelsLike = FormatTemperature(current, imperial ? "FeelsLikeF" : "FeelsLikeC", imperial);
        string location = ReadLocation(root);

        return new WeatherReport(location, description, temperature, feelsLike);
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

        string unit = imperial ? "°F" : "°C";
        return $"{value.GetString()}{unit}";
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
        if (!root.TryGetProperty("nearest_area", out JsonElement areas) || areas.GetArrayLength() == 0)
        {
            return string.Empty;
        }

        JsonElement area = areas[0];
        return area.TryGetProperty("areaName", out JsonElement names) && names.GetArrayLength() > 0
            ? names[0].GetProperty("value").GetString() ?? string.Empty
            : string.Empty;
    }

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
