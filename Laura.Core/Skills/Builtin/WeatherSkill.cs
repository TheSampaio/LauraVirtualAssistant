using Laura.Core.Abstractions;
using Laura.Core.Localization;
using Laura.Core.Weather;

namespace Laura.Core.Skills.Builtin;

/// <summary>
/// Answers questions about the current weather.
///
/// It needs the internet but not the generative model, so it stays available with
/// the AI mode turned off.
/// </summary>
public sealed class WeatherSkill : PhraseSkillBase
{
    private readonly IWeatherProvider _weatherProvider;

    /// <summary>
    /// Initializes the skill.
    ///
    /// Args:
    ///     localizer: Source of the phrases and responses.
    ///     weatherProvider: Service that fetches the current weather.
    /// </summary>
    public WeatherSkill(ILocalizer localizer, IWeatherProvider weatherProvider)
        : base(localizer)
    {
        ArgumentNullException.ThrowIfNull(weatherProvider);
        _weatherProvider = weatherProvider;
    }

    /// <inheritdoc />
    public override string Id => "builtin.weather";

    /// <inheritdoc />
    public override int Priority => 25;

    /// <inheritdoc />
    protected override string PhrasesKey => LocalizationKeys.Skills.WeatherPhrases;

    /// <inheritdoc />
    public override async Task<SkillResponse> ExecuteAsync(
        SkillRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        WeatherReport? report = await _weatherProvider
            .GetCurrentAsync(request.Culture, cancellationToken)
            .ConfigureAwait(false);

        if (report is null)
        {
            return SkillResponse.Speak(Localizer.Get(LocalizationKeys.Skills.WeatherUnavailable));
        }

        return SkillResponse.Speak(Localizer.Get(
            LocalizationKeys.Skills.WeatherResponse,
            report.Description,
            report.Temperature,
            report.FeelsLike,
            report.Location));
    }
}
