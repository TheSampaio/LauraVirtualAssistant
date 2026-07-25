using System.Windows.Forms;
using Laura.App.Interop;
using Laura.App.Views;
using Laura.Core.Abstractions;
using Laura.Core.Configuration;
using Laura.Core.Engine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Laura.App.Shell;

/// <summary>
/// Contexto de execução da aplicação: mantém Laura viva na bandeja sem uma janela
/// principal aberta.
///
/// Substitui o laço <c>while</c> do protótipo original por um modelo dirigido a
/// eventos — mensagens da bandeja, da tecla de atalho e do motor —, que é o que
/// permite à interface nunca travar enquanto Laura ouve ou fala.
/// </summary>
public sealed class LauraApplicationContext : ApplicationContext
{
    /// <summary>Tecla que, junto de Alt, abre e fecha a janela de configurações.</summary>
    public const Keys ToggleKey = Keys.L;

    /// <summary>Modificador da tecla de atalho global.</summary>
    public const HotkeyModifiers ToggleModifiers = HotkeyModifiers.Alt | HotkeyModifiers.NoRepeat;

    private readonly IServiceProvider _services;
    private readonly WinFormsShellController _shellController;
    private readonly IAssistantEngine _engine;
    private readonly HourlyAnnouncer _hourlyAnnouncer;
    private readonly ISettingsService _settingsService;
    private readonly ILocalizer _localizer;
    private readonly ILogger<LauraApplicationContext> _logger;

    private readonly TrayIcon _trayIcon;
    private readonly GlobalHotkey? _hotkey;

    private SettingsForm? _settingsForm;
    private bool _shuttingDown;

    /// <summary>
    /// Compõe a bandeja, a tecla de atalho e inicia os serviços de Laura.
    ///
    /// Args:
    ///     services: Provedor de dependências da aplicação.
    /// </summary>
    public LauraApplicationContext(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        _services = services;
        _shellController = services.GetRequiredService<WinFormsShellController>();
        _engine = services.GetRequiredService<IAssistantEngine>();
        _hourlyAnnouncer = services.GetRequiredService<HourlyAnnouncer>();
        _settingsService = services.GetRequiredService<ISettingsService>();
        _localizer = services.GetRequiredService<ILocalizer>();
        _logger = services.GetRequiredService<ILogger<LauraApplicationContext>>();

        _trayIcon = new TrayIcon(_localizer, ShowSettings, TogglePause, ExitApplication);
        _hotkey = RegisterToggleHotkey();

        _shellController.Bind(ShowSettings, ToggleSettings, ExitApplication);
        _settingsService.Changed += OnSettingsChanged;
        _trayIcon.SetPaused(!_settingsService.Current.Recognition.Enabled);

        _ = StartServicesAsync();
    }

    /// <summary>
    /// Registra Alt + L para alternar a janela de configurações.
    ///
    /// Returns:
    ///     A tecla de atalho, ou <see langword="null"/> quando a combinação já está
    ///     tomada — nesse caso Laura segue acessível pela bandeja.
    /// </summary>
    private GlobalHotkey? RegisterToggleHotkey()
    {
        GlobalHotkey? hotkey = GlobalHotkey.TryRegister(ToggleModifiers, ToggleKey);

        if (hotkey is null)
        {
            _logger.LogWarning("Alt+L já está em uso; a janela abrirá apenas pela bandeja.");
            return null;
        }

        hotkey.Pressed += (_, _) => ToggleSettings();
        return hotkey;
    }

    /// <summary>
    /// Inicia o motor da assistente, o anunciador de horas e prepara a janela.
    ///
    /// A janela é construída aqui, com a aplicação já ociosa, e não no primeiro
    /// Alt + L: montá-la sob demanda faria o atalho parecer travado enquanto o
    /// Windows enumera as vozes instaladas.
    ///
    /// Returns:
    ///     Uma tarefa concluída quando os serviços foram iniciados.
    /// </summary>
    private async Task StartServicesAsync()
    {
        try
        {
            await _engine.StartAsync().ConfigureAwait(true);
            _hourlyAnnouncer.Start();

            _ = EnsureSettingsForm();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Falha ao iniciar os serviços da assistente.");
        }
    }

    // === Ações da janela ===

    /// <summary>
    /// Exibe a janela de configurações, criando-a na primeira vez.
    /// </summary>
    private void ShowSettings() => EnsureSettingsForm().ShowFromTray();

    /// <summary>
    /// Alterna a visibilidade da janela de configurações.
    /// </summary>
    private void ToggleSettings()
    {
        SettingsForm form = EnsureSettingsForm();

        if (form.Visible)
        {
            form.Hide();
        }
        else
        {
            form.ShowFromTray();
        }
    }

    /// <summary>
    /// Resolve a janela de configurações do contêiner na primeira necessidade.
    ///
    /// Returns:
    ///     A instância única da janela, reutilizada entre aberturas.
    /// </summary>
    private SettingsForm EnsureSettingsForm() => _settingsForm ??= _services.GetRequiredService<SettingsForm>();

    /// <summary>
    /// Liga ou desliga a escuta por voz a partir da bandeja.
    /// </summary>
    private void TogglePause()
    {
        LauraSettings current = _settingsService.Current;
        LauraSettings updated = current with
        {
            Recognition = current.Recognition with { Enabled = !current.Recognition.Enabled },
        };

        _ = UpdateSettingsAsync(updated);
    }

    /// <summary>
    /// Persiste uma alteração de configuração feita fora da janela.
    ///
    /// Args:
    ///     settings: Configurações a aplicar.
    ///
    /// Returns:
    ///     Uma tarefa concluída quando a alteração foi persistida.
    /// </summary>
    private async Task UpdateSettingsAsync(LauraSettings settings)
    {
        try
        {
            await _settingsService.UpdateAsync(settings).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Falha ao atualizar as configurações pela bandeja.");
        }
    }

    /// <summary>
    /// Reage a mudanças de configuração atualizando o estado visível da bandeja.
    ///
    /// Args:
    ///     sender: Serviço de configurações.
    ///     settings: Configurações já vigentes.
    /// </summary>
    private void OnSettingsChanged(object? sender, LauraSettings settings) =>
        _trayIcon.SetPaused(!settings.Recognition.Enabled);

    // === Encerramento ===

    /// <summary>
    /// Encerra Laura de forma ordenada: para os serviços e libera os recursos.
    /// </summary>
    private void ExitApplication()
    {
        if (_shuttingDown)
        {
            return;
        }

        _shuttingDown = true;
        _ = ShutdownAsync();
    }

    /// <summary>
    /// Para o motor e o anunciador antes de encerrar o laço de mensagens.
    ///
    /// Returns:
    ///     Uma tarefa concluída quando o encerramento termina.
    /// </summary>
    private async Task ShutdownAsync()
    {
        try
        {
            await _engine.StopAsync().ConfigureAwait(true);
            await _hourlyAnnouncer.DisposeAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Falha ao encerrar os serviços da assistente.");
        }
        finally
        {
            ExitThread();
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _settingsService.Changed -= OnSettingsChanged;

            _hotkey?.Dispose();
            _trayIcon.Dispose();
            _settingsForm?.CloseToExit();
        }

        base.Dispose(disposing);
    }
}
