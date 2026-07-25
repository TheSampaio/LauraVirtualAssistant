using Laura.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace Laura.Core.Engine;

/// <summary>
/// Anuncia cada hora cheia, quando a opção está ligada nas configurações.
///
/// Sucede a verificação por sondagem do protótipo original: em vez de comparar a
/// hora a cada quadro, o serviço dorme exatamente até a próxima virada.
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
    /// Inicializa o anunciador.
    ///
    /// Args:
    ///     engine: Motor usado para falar o anúncio.
    ///     settings: Configurações consultadas a cada virada de hora.
    ///     composer: Compositor do texto do anúncio.
    ///     clock: Fonte da hora atual.
    ///     logger: Destino dos registros de diagnóstico.
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
    /// Passa a acompanhar as viradas de hora.
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
                // Encerramento normal.
            }

            _worker = null;
        }

        _lifetime.Dispose();
    }

    /// <summary>
    /// Dorme até cada virada de hora e anuncia quando a opção está ligada.
    ///
    /// A configuração é lida na virada, e não na inicialização, para que ligar ou
    /// desligar o anúncio tenha efeito sem reiniciar Laura.
    ///
    /// Args:
    ///     cancellationToken: Token que encerra o acompanhamento.
    ///
    /// Returns:
    ///     Uma tarefa concluída quando o acompanhamento termina.
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
                _logger.LogError(exception, "Falha ao anunciar a hora cheia.");
            }
        }
    }

    /// <summary>
    /// Calcula quanto falta para a próxima hora cheia.
    ///
    /// Returns:
    ///     O intervalo até o próximo minuto zero, com uma folga de um segundo para
    ///     que o anúncio nunca caia na hora anterior por arredondamento.
    /// </summary>
    private TimeSpan GetDelayUntilNextHour()
    {
        DateTimeOffset now = _clock.Now;
        DateTimeOffset nextHour = new DateTimeOffset(
            now.Year, now.Month, now.Day, now.Hour, minute: 0, second: 0, now.Offset).AddHours(1);

        return nextHour - now + TimeSpan.FromSeconds(1);
    }
}
