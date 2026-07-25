using System.Globalization;
using Laura.Core.Weather;

namespace Laura.Core.Abstractions;

/// <summary>
/// Provides the current weather for the user's location.
///
/// Weather needs the internet but not the generative model, so it stays a native
/// skill: it works with the AI mode turned off.
/// </summary>
public interface IWeatherProvider
{
    /// <summary>
    /// Fetches the current weather.
    ///
    /// Args:
    ///     culture: Culture used to choose measurement units (metric vs. imperial).
    ///     cancellationToken: Token that aborts the request.
    ///
    /// Returns:
    ///     The current weather, or <see langword="null"/> when the service could
    ///     not be reached. Failures never throw so the skill can fall back to a
    ///     spoken apology.
    /// </summary>
    Task<WeatherReport?> GetCurrentAsync(CultureInfo culture, CancellationToken cancellationToken = default);
}
