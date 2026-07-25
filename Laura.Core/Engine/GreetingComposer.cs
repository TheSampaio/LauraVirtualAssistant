using Laura.Core.Abstractions;
using Laura.Core.Localization;

namespace Laura.Core.Engine;

/// <summary>
/// Composes the date- and time-dependent utterances.
///
/// The greeting appears at startup, in the greeting skill and in the full-hour
/// announcement; concentrating it here keeps three versions of the same text from
/// drifting apart.
/// </summary>
public sealed class GreetingComposer
{
    private const int AfternoonStartHour = 12;
    private const int EveningStartHour = 18;
    private const int NoonHour = 12;

    private readonly ILocalizer _localizer;
    private readonly IUserContext _userContext;
    private readonly IClock _clock;

    /// <summary>
    /// Initializes the composer.
    ///
    /// Args:
    ///     localizer: Source of the texts in the active language.
    ///     userContext: The name the user is addressed by.
    ///     clock: Source of date and time.
    /// </summary>
    public GreetingComposer(ILocalizer localizer, IUserContext userContext, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(userContext);
        ArgumentNullException.ThrowIfNull(clock);

        _localizer = localizer;
        _userContext = userContext;
        _clock = clock;
    }

    /// <summary>
    /// Composes the greeting appropriate to the time of day.
    ///
    /// Returns:
    ///     A greeting already carrying the user's name, such as "Good morning, Kellvyn.".
    /// </summary>
    public string ComposeGreeting()
    {
        string key = _clock.Now.Hour switch
        {
            >= EveningStartHour => LocalizationKeys.Greeting.Evening,
            >= AfternoonStartHour => LocalizationKeys.Greeting.Afternoon,
            _ => LocalizationKeys.Greeting.Morning,
        };

        return _localizer.Get(key, _userContext.DisplayName);
    }

    /// <summary>
    /// Composes the full opening: greeting followed by date and time.
    ///
    /// Returns:
    ///     The text Laura speaks at startup.
    /// </summary>
    public string ComposeStartupBriefing() =>
        $"{ComposeGreeting()} {_localizer.Get(LocalizationKeys.Greeting.Summary, _clock.Now)}";

    /// <summary>
    /// Composes the announcement of a full hour.
    ///
    /// Args:
    ///     instant: Instant of the full hour to announce.
    ///
    /// Returns:
    ///     The announcement, with its own wording for midnight and noon.
    /// </summary>
    public string ComposeHourlyAnnouncement(DateTimeOffset instant) => instant.Hour switch
    {
        0 => _localizer.Get(LocalizationKeys.Greeting.Midnight),
        NoonHour => _localizer.Get(LocalizationKeys.Greeting.Noon),
        _ => _localizer.Get(LocalizationKeys.Greeting.HourlyChime, instant),
    };
}
