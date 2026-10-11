using Laura.App.Platforms.Windows;
using Laura.App.Shell;
using Laura.App.Theming;
using Laura.App.Views;
using Laura.App.Infrastructure;
using Laura.Core.Abstractions;
using Laura.Core.Engine;
using Laura.Platform.Windows.Conversation;
using Microsoft.Extensions.DependencyInjection;

namespace Laura.App;

/// <summary>Owns the MAUI window, assistant lifetime and Windows tray integration.</summary>
public sealed class LauraApplication(IServiceProvider services) : Application, IAsyncDisposable
{
    private WindowsTrayHost? _tray;
    private WindowsShortcutHost? _shortcuts;
    private bool _started;
    private bool _stopping;
    /// <inheritdoc />
    protected override Window CreateWindow(IActivationState? activationState)
    {
        UserAppTheme = AppTheme.Dark;
        var navigation = new NavigationPage(services.GetRequiredService<MainPage>()) { BackgroundColor = Theme.Background, BarBackgroundColor = Theme.Background, BarTextColor = Theme.Text };
        var window = new Window(navigation)
        {
            Title = "Laura Virtual Assistant", Width = 1440, Height = 900, MinimumWidth = 720, MinimumHeight = 600,
            TitleBar = new TitleBar { Title = "Laura Virtual Assistant", BackgroundColor = Theme.Background, ForegroundColor = Colors.White, HeightRequest = 32 }
        };
        window.Created += (_, _) => AttachTray(window);
        window.Destroying += async (_, _) => await StopAsync().ConfigureAwait(true);
        return window;
    }
    /// <summary>Attaches native window services and begins initialization once.</summary>
    private void AttachTray(Window window)
    {
        if (_started) { return; }
        _started = true;
        if (window.Handler?.PlatformView is Microsoft.UI.Xaml.Window native)
        {
            WindowsWindowTheme.Apply(native);
            nint handle = WinRT.Interop.WindowNative.GetWindowHandle(native);
            _tray = new WindowsTrayHost(handle, () => _tray?.Toggle(), () => _ = ExitAsync(window), services.GetRequiredService<MainPage>().SetWindowVisible);
            if (!Environment.GetCommandLineArgs().Contains("--ui-smoke", StringComparer.Ordinal)) { _shortcuts = new WindowsShortcutHost(window, services.GetRequiredService<MainPage>()); }
            services.GetRequiredService<MauiShellController>().Bind(
                () => { _tray.Show(); _ = services.GetRequiredService<MainPage>().OpenSetupAsync(); },
                _tray.Toggle, () => _ = ExitAsync(window));
        }
        _ = StartAsync();
    }
    /// <summary>Loads persisted settings and starts domain services without blocking the UI.</summary>
    private async Task StartAsync()
    {
        try
        {
            ISettingsService settings = services.GetRequiredService<ISettingsService>();
            await settings.LoadAsync().ConfigureAwait(true);
            string[] args = Environment.GetCommandLineArgs();
            int import = Array.IndexOf(args, "--import-mark-lv");
            if (import >= 0 && import + 1 < args.Length) { await services.GetRequiredService<MarkLvConfigurationImporter>().ImportAsync(args[import + 1]).ConfigureAwait(true); }
            int smoke = Array.IndexOf(args, "--ui-smoke");
            if (smoke >= 0) { await settings.UpdateAsync(settings.Current with { GreetOnStartup = false, AnnounceHourly = false }).ConfigureAwait(true); }
            services.GetRequiredService<ILocalizer>().SetCulture(settings.Current.ResolveCulture());
            services.GetRequiredService<IUiLocalizer>().SetCulture(settings.Current.ResolveUiCulture());
            await services.GetRequiredService<IAssistantEngine>().StartAsync().ConfigureAwait(true);
            await services.GetRequiredService<ReminderScheduler>().StartAsync().ConfigureAwait(true);
            services.GetRequiredService<HourlyAnnouncer>().Start();
            services.GetRequiredService<MainPage>().Reload();
            _ = services.GetRequiredService<Laura.Platform.Windows.System.WindowsApplicationCatalog>().RefreshAsync();
            if (args.Contains("--show", StringComparer.Ordinal)) { _tray?.Show(); }
            if (smoke >= 0 && smoke + 1 < args.Length)
            { await MauiSmokeRunner.RunAsync(services, Windows[0], Path.GetFullPath(args[smoke + 1])).ConfigureAwait(true); return; }
            if (!Environment.GetCommandLineArgs().Contains("--show", StringComparer.Ordinal) && string.IsNullOrWhiteSpace(settings.Current.GenerativeAi.Model))
            { await services.GetRequiredService<MainPage>().OpenSetupAsync().ConfigureAwait(true); }
        }
        catch (Exception) { await services.GetRequiredService<MainPage>().DisplayAlertAsync("Laura", "Startup failed. Check settings and the local data directory.", "OK").ConfigureAwait(true); }
    }
    /// <summary>Stops background workers, microphone and speech before destroying the window.</summary>
    private async Task StopAsync()
    {
        if (_stopping) { return; }
        _stopping = true;
        _shortcuts?.Dispose(); _shortcuts = null;
        await services.GetRequiredService<ReminderScheduler>().DisposeAsync().ConfigureAwait(true);
        await services.GetRequiredService<GeminiLiveClient>().ReconfigureAsync().ConfigureAwait(true);
        await services.GetRequiredService<HourlyAnnouncer>().DisposeAsync().ConfigureAwait(true);
        await services.GetRequiredService<IAssistantEngine>().StopAsync().ConfigureAwait(true);
        _tray?.Dispose(); _tray = null;
    }
    /// <summary>Exits only on an explicit domain or tray exit request.</summary>
    private async Task ExitAsync(Window window) { _tray?.AllowExit(); await StopAsync().ConfigureAwait(true); CloseWindow(window); }
    /// <inheritdoc />
    public async ValueTask DisposeAsync() { await StopAsync().ConfigureAwait(true); GC.SuppressFinalize(this); }
}
