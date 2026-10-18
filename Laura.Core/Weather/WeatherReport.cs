namespace Laura.Core.Weather;

/// <summary>
/// A concise observed weather snapshot and dated forecast, in the requested units.
/// </summary>
/// <param name="Location">Human-readable place name the report is for.</param>
/// <param name="Description">Short condition text, such as "Partly cloudy".</param>
/// <param name="Temperature">Current temperature, already formatted with its unit.</param>
/// <param name="FeelsLike">Apparent temperature, already formatted with its unit.</param>
public sealed record WeatherReport(string Location, string Description, string Temperature, string FeelsLike)
{
    /// <summary>Gets the classified observed condition when the provider supplies a recognized code.</summary>
    public WeatherCondition? CurrentCondition { get; init; }
    /// <summary>Gets the provider's local observation time, when supplied and valid.</summary>
    public DateTime? ObservedAtLocal { get; init; }
    /// <summary>Gets dated local forecast periods supplied by the provider.</summary>
    public IReadOnlyList<WeatherForecastPeriod> Forecast { get; init; } = [];
    /// <summary>Gets a provider-issued severe-weather alert, when one is supplied.</summary>
    public string Alert { get; init; } = "";
    /// <summary>Gets the local calendar date of the daily forecast, when available.</summary>
    public DateOnly? ForecastDate { get; init; }
    /// <summary>Gets today's predicted minimum, including its unit.</summary>
    public string MinimumTemperature { get; init; } = "";
    /// <summary>Gets today's predicted maximum, including its unit.</summary>
    public string MaximumTemperature { get; init; } = "";
}

/// <summary>Weather categories supported by the native greeting formatter.</summary>
public enum WeatherCondition
{
    /// <summary>Clear or sunny conditions.</summary>
    Sunny,
    /// <summary>Partially cloudy conditions.</summary>
    PartlyCloudy,
    /// <summary>Cloudy or overcast conditions.</summary>
    Cloudy,
    /// <summary>Rain or showers.</summary>
    Rain,
    /// <summary>Heavy rain.</summary>
    HeavyRain,
    /// <summary>Thunderstorms.</summary>
    Thunderstorm,
    /// <summary>Snow or sleet.</summary>
    Snow,
}

/// <summary>A forecast for a local three-hour period with optional precipitation probabilities.</summary>
public sealed record WeatherForecastPeriod(DateTime LocalTime, WeatherCondition? Condition, int? RainChance, int? ThunderChance, decimal? PrecipitationMillimeters);
