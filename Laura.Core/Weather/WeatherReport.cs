namespace Laura.Core.Weather;

/// <summary>
/// A concise current-weather snapshot, in the units of the requested culture.
/// </summary>
/// <param name="Location">Human-readable place name the report is for.</param>
/// <param name="Description">Short condition text, such as "Partly cloudy".</param>
/// <param name="Temperature">Current temperature, already formatted with its unit.</param>
/// <param name="FeelsLike">Apparent temperature, already formatted with its unit.</param>
public sealed record WeatherReport(string Location, string Description, string Temperature, string FeelsLike);
