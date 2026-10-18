using System.Globalization;
using System.Text.Json;
using Laura.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace Laura.Core.Weather;

/// <summary>
/// Fetches observed and hourly forecast weather from wttr.in, a free, key-less service.
///
/// No API key keeps the assistant self-contained; an optional configured place
/// overrides the service's IP-based location detection.
/// </summary>
public sealed class WttrWeatherProvider : IWeatherProvider, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<WttrWeatherProvider> _logger;
    private readonly IClock _clock;
    private readonly ISettingsService? _settings;
    private readonly SemaphoreSlim _cacheGate = new(1, 1);
    private WeatherReport? _cached;
    private DateTimeOffset _cachedAt;
    private string _cacheKey = "";

    /// <summary>Releases the cache gate owned by this provider.</summary>
    public void Dispose() => _cacheGate.Dispose();

    /// <summary>
    /// Initializes the provider.
    ///
    /// Args:
    ///     httpClient: Client used for the request; its timeout bounds the wait.
    ///     logger: Destination for diagnostic logs.
    ///     clock: Local time source for dates and cache expiration.
    ///     settings: Optional source of the user's configured weather place.
    /// </summary>
    public WttrWeatherProvider(HttpClient httpClient, ILogger<WttrWeatherProvider> logger, IClock clock, ISettingsService? settings = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(clock);

        _httpClient = httpClient;
        _logger = logger;
        _clock = clock;
        _settings = settings;
    }

    /// <inheritdoc />
    public async Task<WeatherReport?> GetCurrentAsync(
        CultureInfo culture,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(culture);

        string location = _settings?.Current.WeatherLocation ?? "";
        string key = culture.Name + "|" + location;
        await _cacheGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cached is not null && key == _cacheKey && _clock.Now - _cachedAt < TimeSpan.FromMinutes(20)
                && _cachedAt.Date == _clock.Now.Date) { return _cached; }
            string path = location.Length == 0 ? "" : Uri.EscapeDataString(location);
            string requestUri = $"https://wttr.in/{path}?format=j1";
            await using Stream stream = await _httpClient
                .GetStreamAsync(requestUri, cancellationToken)
                .ConfigureAwait(false);

            using JsonDocument document = await JsonDocument
                .ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            JsonElement root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out JsonElement data) && data.ValueKind == JsonValueKind.Object) { root = data; }
            WeatherReport? report = Parse(root, UsesImperialUnits(culture), DateOnly.FromDateTime(_clock.Now.DateTime));
            if (report is not null) { _cached = report; _cachedAt = _clock.Now; _cacheKey = key; }
            return report;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or OperationCanceledException)
        {
            _logger.LogWarning(exception, "Could not fetch the weather from wttr.in.");
            return null;
        }
        finally { _cacheGate.Release(); }
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

        var report = new WeatherReport(location, description, temperature, feelsLike)
        {
            CurrentCondition = ReadObservedCondition(current),
            ObservedAtLocal = ReadObservationTime(current),
            Alert = ReadAlert(root),
        };
        var forecast = new List<WeatherForecastPeriod>();
        if (root.TryGetProperty("weather", out JsonElement days) && days.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement day in days.EnumerateArray())
            {
                if (day.ValueKind != JsonValueKind.Object || !day.TryGetProperty("date", out JsonElement date)
                    || date.ValueKind != JsonValueKind.String
                    || !DateOnly.TryParseExact(date.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly forecastDate)
                    || forecastDate < today || forecastDate > today.AddDays(1)) { continue; }
                ReadHourly(day, forecastDate, forecast);
                if (forecastDate != today) { continue; }
                string minimum = FormatTemperature(day, imperial ? "mintempF" : "mintempC", imperial);
                string maximum = FormatTemperature(day, imperial ? "maxtempF" : "maxtempC", imperial);
                if (minimum.Length > 0 || maximum.Length > 0)
                { report = report with { ForecastDate = forecastDate, MinimumTemperature = minimum, MaximumTemperature = maximum }; }
            }
        }
        return report with { Forecast = forecast };
    }

    /// <summary>Reads the provider's timestamp for the current observation without using the fetch time as a substitute.</summary>
    private static DateTime? ReadObservationTime(JsonElement current)
    {
        if (!current.TryGetProperty("localObsDateTime", out JsonElement field) || field.ValueKind != JsonValueKind.String) { return null; }
        return DateTime.TryParseExact(field.GetString(), ["yyyy-MM-dd hh:mm tt", "yyyy-MM-dd HH:mm"], CultureInfo.InvariantCulture,
            DateTimeStyles.None, out DateTime observed) ? observed : null;
    }

    /// <summary>Reads a provider warning only when it is explicitly present as text.</summary>
    private static string ReadAlert(JsonElement root)
    {
        if (!root.TryGetProperty("weatherAlert", out JsonElement alerts) || alerts.ValueKind != JsonValueKind.Array || alerts.GetArrayLength() == 0) { return ""; }
        JsonElement first = alerts[0];
        if (first.ValueKind != JsonValueKind.Object || !first.TryGetProperty("headline", out JsonElement headline) || headline.ValueKind != JsonValueKind.String) { return ""; }
        string text = (headline.GetString() ?? "").Trim();
        return text.Length <= 180 ? text : text[..180];
    }

    /// <summary>Reads dated hourly forecasts without treating missing probabilities as zero.</summary>
    private static void ReadHourly(JsonElement day, DateOnly date, List<WeatherForecastPeriod> periods)
    {
        if (!day.TryGetProperty("hourly", out JsonElement hourly) || hourly.ValueKind != JsonValueKind.Array) { return; }
        foreach (JsonElement item in hourly.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || !TryNumber(item, "time", out int time) || time < 0 || time > 2300 || time % 100 != 0) { continue; }
            DateTime localTime = date.ToDateTime(TimeOnly.MinValue).AddHours(time / 100);
            periods.Add(new(localTime, ReadCondition(item), ReadPercent(item, "chanceofrain"), ReadPercent(item, "chanceofthunder"), ReadDecimal(item, "precipMM")));
        }
    }

    /// <summary>Excludes nearby and intermittent events from claims about current local conditions.</summary>
    private static WeatherCondition? ReadObservedCondition(JsonElement item)
    {
        if (!TryNumber(item, "weatherCode", out int code)
            || code is 176 or 179 or 182 or 185 or 200 or 263 or 293 or 299 or 305 or 323 or 329 or 335 or 386 or 392) { return null; }
        return ReadCondition(item);
    }

    /// <summary>Classifies documented weather codes conservatively; unknown codes remain unknown.</summary>
    private static WeatherCondition? ReadCondition(JsonElement item)
    {
        if (!TryNumber(item, "weatherCode", out int code)) { return null; }
        return code switch
        {
            113 => WeatherCondition.Sunny,
            116 => WeatherCondition.PartlyCloudy,
            119 or 122 => WeatherCondition.Cloudy,
            176 or 263 or 266 or 293 or 296 or 299 or 302 or 311 or 353 => WeatherCondition.Rain,
            305 or 308 or 356 or 359 => WeatherCondition.HeavyRain,
            200 or 386 or 389 or 392 or 395 => WeatherCondition.Thunderstorm,
            179 or 227 or 230 or 323 or 326 or 329 or 332 or 335 or 338 or 368 or 371 => WeatherCondition.Snow,
            _ => null,
        };
    }

    /// <summary>Reads a percentage only when the provider supplies a valid value.</summary>
    private static int? ReadPercent(JsonElement item, string name) => TryNumber(item, name, out int value) && value is >= 0 and <= 100 ? value : null;

    /// <summary>Reads a nonnegative decimal value from a provider field.</summary>
    private static decimal? ReadDecimal(JsonElement item, string name) => item.TryGetProperty(name, out JsonElement field)
        && field.ValueKind == JsonValueKind.String && decimal.TryParse(field.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out decimal value) && value >= 0 ? value : null;

    /// <summary>Reads a provider integer without assuming valid JSON fields are numeric JSON values.</summary>
    private static bool TryNumber(JsonElement item, string name, out int value)
    {
        value = 0;
        return item.TryGetProperty(name, out JsonElement field) && field.ValueKind == JsonValueKind.String
            && int.TryParse(field.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
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
