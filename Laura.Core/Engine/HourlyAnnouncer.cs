using Laura.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace Laura.Core.Engine;

/// <summary>
/// Announces each full hour when the option is enabled in settings.
///
/// Replaces the original prototype's polling check: instead of comparing the
/// hour every frame, the service sleeps exactly until the next rollover.
/// </summary>
public sealed class HourlyAnnouncer : IAsyncDisposable
{
    private readonly IAssistantEngine _engine;
    private readonly ISettingsService _settings;
    private readonly GreetingComposer _composer;
    private readonly IClock _clock;
    private readonly ILogger<HourlyAnnouncer> _logger;
    private readonly CancellationTokenSource _lifetime = new();

    private Task? _worker;

    /// <summary>
    /// Initializes the announcer.
    ///
    /// Args:
    ///     engine: Engine used to speak the announcement.
    ///     settings: Settings checked at each hour rollover.
    ///     composer: Announcement text composer.
    ///     clock: Source of the current time.
    ///     logger: Destination for diagnostic logs.
    /// </summary>
    public HourlyAnnouncer(
        IAssistantEngine engine,
        ISettingsService settings,
        GreetingComposer composer,
        IClock clock,
        ILogger<HourlyAnnouncer> logger)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(composer);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        _engine = engine;
        _settings = settings;
        _composer = composer;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>
    /// Starts monitoring full-hour rollovers.
    /// </summary>
    public void Start() => _worker ??= Task.Run(() => RunAsync(_lifetime.Token), CancellationToken.None);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _lifetime.CancelAsync().ConfigureAwait(false);

        if (_worker is not null)
        {
            try
            {
                await _worker.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown.
            }

            _worker = null;
        }

        _lifetime.Dispose();
    }

    /// <summary>
    /// Sleeps until each hour rollover and announces when the option is enabled.
    ///
    /// The setting is read at rollover, not startup, so enabling or
    /// disabling the announcement takes effect without restarting Laura.
    ///
    /// Args:
    ///     cancellationToken: Token that ends monitoring.
    ///
    /// Returns:
    ///     A task completed when monitoring ends.
    /// </summary>
    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(GetDelayUntilNextHour(), cancellationToken).ConfigureAwait(false);

                if (!_settings.Current.AnnounceHourly)
                {
                    continue;
                }

                await _engine
                    .SpeakAsync(_composer.ComposeHourlyAnnouncement(_clock.Now), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to announce the full hour.");
            }
        }
    }

    /// <summary>
    /// Calculates how long remains until the next full hour.
    ///
    /// Returns:
    ///     The interval until the next zero minute, with a one-second cushion so
    ///     the announcement never falls in the previous hour because of rounding.
    /// </summary>
    private TimeSpan GetDelayUntilNextHour()
    {
        DateTimeOffset now = _clock.Now;
        DateTimeOffset nextHour = new DateTimeOffset(
            now.Year, now.Month, now.Day, now.Hour, minute: 0, second: 0, now.Offset).AddHours(1);

        return nextHour - now + TimeSpan.FromSeconds(1);
    }
}
