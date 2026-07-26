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
/// Application execution context: keeps Laura alive in the tray without a window
/// main window open.
///
/// Replaces the original prototype's <c>while</c> loop with an event-driven model
/// of tray, hotkey, and engine messages, which is what allows the interface to
/// never freeze while Laura speaks or waits for a model.
/// </summary>
public sealed class LauraApplicationContext : ApplicationContext
{
    /// <summary>Key that, together with Alt, opens and closes the settings window.</summary>
    public const Keys ToggleKey = Keys.L;

    /// <summary>Global hotkey modifier.</summary>
    public const HotkeyModifiers ToggleModifiers = HotkeyModifiers.Alt | HotkeyModifiers.NoRepeat;

    private readonly IServiceProvider _services;
    private readonly WinFormsShellController _shellController;
    private readonly IAssistantEngine _engine;
    private readonly HourlyAnnouncer _hourlyAnnouncer;
    private readonly ILocalizer _localizer;
    private readonly ILogger<LauraApplicationContext> _logger;

    private readonly TrayIcon _trayIcon;
    private readonly GlobalHotkey? _hotkey;

    private SettingsForm? _settingsForm;
    private bool _shuttingDown;

    /// <summary>
    /// Composes the tray, hotkey, and starts Laura services.
    ///
    /// Args:
    ///     services: Application dependency provider.
    /// </summary>
    public LauraApplicationContext(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        _services = services;
        _shellController = services.GetRequiredService<WinFormsShellController>();
        _engine = services.GetRequiredService<IAssistantEngine>();
        _hourlyAnnouncer = services.GetRequiredService<HourlyAnnouncer>();
        _localizer = services.GetRequiredService<ILocalizer>();
        _logger = services.GetRequiredService<ILogger<LauraApplicationContext>>();

        _trayIcon = new TrayIcon(_localizer, ShowSettings, ExitApplication);
        _hotkey = RegisterToggleHotkey();

        _shellController.Bind(ShowSettings, ToggleSettings, ExitApplication);
        _ = StartServicesAsync();
    }

    /// <summary>
    /// Registers Alt + L to toggle the settings window.
    ///
    /// Returns:
    ///     The hotkey, or <see langword="null"/> when the combination is already
    ///     taken - in that case Laura remains accessible from the tray.
    /// </summary>
    private GlobalHotkey? RegisterToggleHotkey()
    {
        GlobalHotkey? hotkey = GlobalHotkey.TryRegister(ToggleModifiers, ToggleKey);

        if (hotkey is null)
        {
            _logger.LogWarning("Alt+L is already in use; the window will open only from the tray.");
            return null;
        }

        hotkey.Pressed += (_, _) => ToggleSettings();
        return hotkey;
    }

    /// <summary>
    /// Starts the assistant engine, the hourly announcer, and prepares the window.
    ///
    /// The window is built here, with the application already idle, not on the first
    /// Alt + L: building it on demand would make the shortcut feel frozen while the
    /// Windows speech service enumerates the installed voices.
    ///
    /// Returns:
    ///     A task completed when the services have started.
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
            _logger.LogError(exception, "Failed to start assistant services.");
        }
    }

    // === Window Actions ===

    /// <summary>
    /// Shows the settings window, creating it the first time.
    /// </summary>
    private void ShowSettings() => EnsureSettingsForm().ShowFromTray();

    /// <summary>
    /// Toggles settings window visibility.
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
    /// Resolves the settings window from the container on first need.
    ///
    /// Returns:
    ///     The single window instance, reused between openings.
    /// </summary>
    private SettingsForm EnsureSettingsForm() => _settingsForm ??= _services.GetRequiredService<SettingsForm>();

    // === Shutdown ===

    /// <summary>
    /// Shuts Laura down cleanly: stops services and releases resources.
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
    /// Stops the engine and announcer before ending the message loop.
    ///
    /// Returns:
    ///     A task completed when shutdown ends.
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
            _logger.LogError(exception, "Failed to stop assistant services.");
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
            _hotkey?.Dispose();
            _trayIcon.Dispose();
            _settingsForm?.CloseToExit();
        }

        base.Dispose(disposing);
    }
}
