using Laura.App.Platforms.Windows;
using Laura.App.Shell;
using Laura.App.Theming;
using Laura.App.Views;
using Laura.App.Infrastructure;
using Laura.Core.Abstractions;
using Laura.Core.Engine;
using Laura.Platform.Windows.Conversation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Laura.App;

/// <summary>Owns the MAUI window, assistant lifetime and Windows tray integration.</summary>
public sealed class LauraApplication(IServiceProvider services) : Application, IAsyncDisposable
{
    private WindowsTrayHost? _tray;
    private WindowsShortcutHost? _shortcuts;
    private WindowsScreenshotShortcuts? _screenshotShortcuts;
    private bool _started;
    private bool _stopping;
    private Task? _brainStartup;
    private Laura.Core.Permissions.PermissionOptions? _permissionOptions;
    /// <inheritdoc />
    protected override Window CreateWindow(IActivationState? activationState)
    {
        using var performance = PerformanceTrace.Measure("window.resolve-and-create");
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
            var approvals = services.GetRequiredService<WindowsApprovalCenter>();
            _tray.BindApprovals(approvals.Show);
            services.GetRequiredService<LauraNotifications>().Bind(_tray.Notify);
            approvals.Start((title, message) => services.GetRequiredService<LauraNotifications>().Publish(title, message, LauraNotificationKind.Approval),
                () => _tray?.IsHiddenOrMinimized != false, () => { _tray?.Show(); _ = services.GetRequiredService<MainPage>().OpenSetupAsync(); });
            services.GetRequiredService<Laura.Core.Permissions.PermissionManager>().Changed += OnApprovalsChanged;
            if (!Environment.GetCommandLineArgs().Contains("--ui-smoke", StringComparer.Ordinal) && !Environment.GetCommandLineArgs().Contains("--permissions-smoke", StringComparer.Ordinal) && !Environment.GetCommandLineArgs().Contains("--screenshot-smoke", StringComparer.Ordinal) && !Environment.GetCommandLineArgs().Contains("--brain-smoke", StringComparer.Ordinal)) { _shortcuts = new WindowsShortcutHost(window, services.GetRequiredService<MainPage>()); }
            services.GetRequiredService<MauiShellController>().Bind(
                () => { _tray.Show(); _ = services.GetRequiredService<MainPage>().OpenSetupAsync(); },
                _tray.Toggle, () => _ = ExitAsync(window), _tray.Show);
        }
        _ = StartAsync();
    }
    /// <summary>Loads persisted settings and starts domain services without blocking the UI.</summary>
    private async Task StartAsync()
    {
        string[] args = Environment.GetCommandLineArgs();
        var interactive = PerformanceTrace.Measure("startup.interactive");
        try
        {
            ISettingsService settings = services.GetRequiredService<ISettingsService>();
            services.GetRequiredService<RuntimeDiagnosticContext>();
            using (PerformanceTrace.Measure("startup.settings")) { await settings.LoadAsync().ConfigureAwait(true); }
            if (Environment.GetCommandLineArgs().Any(argument => argument is "--ui-smoke" or "--permissions-smoke" or "--screenshot-smoke" or "--brain-smoke")
                && settings.Current.Brain.Directory.Length > 0
                && !Laura.Core.Permissions.PermissionPolicy.Within(settings.Current.Brain.Directory, services.GetRequiredService<AssistantStorage>().Directory))
            { throw new InvalidOperationException("Diagnostic settings cannot point at a Brain outside isolated storage."); }
            try { await services.GetRequiredService<IChatGptConnection>().ReadAsync().ConfigureAwait(true); }
            catch (Laura.Core.ChatGpt.ChatGptConnectionException) { }
            await Task.Run(services.GetRequiredService<Laura.Core.Files.UserFileWorkspace>().EnsureCreated).ConfigureAwait(true);
            try { await Task.Run(services.GetRequiredService<LegacyProviderCleanup>().RetireCredential).ConfigureAwait(true); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or Laura.Core.Permissions.PermissionDeniedException)
            { services.GetRequiredService<ILogger<LauraApplication>>().LogWarning("Retired credential cleanup failed ({ErrorType}).", error.GetType().Name); }
            _permissionOptions = settings.Current.Permissions;
            settings.Changed += OnPermissionSettingsChanged;
            await PersistPermissionOptionsAsync().ConfigureAwait(true);
            if (_stopping) { interactive.Dispose(); return; }
            int smoke = Array.IndexOf(args, "--ui-smoke");
            int permissionSmoke = Array.IndexOf(args, "--permissions-smoke");
            int screenshotSmoke = Array.IndexOf(args, "--screenshot-smoke");
            int brainSmoke = Array.IndexOf(args, "--brain-smoke");
            if (smoke >= 0 || permissionSmoke >= 0 || screenshotSmoke >= 0 || brainSmoke >= 0) { await settings.UpdateAsync(settings.Current with { GreetOnStartup = false, AnnounceHourly = false, ClapWakeEnabled = false }).ConfigureAwait(true); }
            services.GetRequiredService<ILocalizer>().SetCulture(settings.Current.ResolveCulture());
            services.GetRequiredService<IUiLocalizer>().SetCulture(settings.Current.ResolveUiCulture());
            _brainStartup = WarmBrainAsync();
            services.GetRequiredService<IAssistantEngine>().StateChanged += OnEngineStateChanged;
            using (PerformanceTrace.Measure("startup.engine-and-history")) { await services.GetRequiredService<IAssistantEngine>().StartAsync().ConfigureAwait(true); }
            await services.GetRequiredService<ConversationActions>().InitializeAsync().ConfigureAwait(true);
            if (_stopping) { interactive.Dispose(); return; }
            await services.GetRequiredService<ReminderScheduler>().StartAsync().ConfigureAwait(true);
            services.GetRequiredService<HourlyAnnouncer>().Start();
            if (smoke < 0 && permissionSmoke < 0 && screenshotSmoke < 0 && brainSmoke < 0) { services.GetRequiredService<ClapWakeCoordinator>().Start(); }
            services.GetRequiredService<MainPage>().Reload();
            interactive.Dispose();
            await _brainStartup.ConfigureAwait(true);
            if (_stopping) { return; }
            if (brainSmoke >= 0 && brainSmoke + 1 < args.Length) { await BrainSmokeRunner.RunAsync(services, args[brainSmoke + 1]).ConfigureAwait(true); return; }
            if (screenshotSmoke >= 0 && screenshotSmoke + 1 < args.Length) { await ScreenshotSmokeRunner.RunAsync(services, Windows[0], args[screenshotSmoke + 1]).ConfigureAwait(true); return; }
            if (smoke < 0 && permissionSmoke < 0 && !args.Contains("--screenshot-smoke", StringComparer.Ordinal) && Windows[0].Handler?.PlatformView is Microsoft.UI.Xaml.Window screenshotNative)
            { _screenshotShortcuts = new(WinRT.Interop.WindowNative.GetWindowHandle(screenshotNative), settings, services.GetRequiredService<ScreenshotCoordinator>(), services.GetRequiredService<IUiLocalizer>(), services.GetRequiredService<LauraNotifications>()); }
            if (permissionSmoke >= 0 && permissionSmoke + 1 < args.Length)
            { await PermissionSmokeRunner.RunAsync(services, Windows[0], args[permissionSmoke + 1]).ConfigureAwait(true); return; }
            _ = services.GetRequiredService<Laura.Platform.Windows.System.WindowsApplicationCatalog>().RefreshAsync();
            if (smoke >= 0 && smoke + 1 < args.Length && args.Contains("--verify-background-startup", StringComparer.Ordinal))
            {
                if (_tray is null || _tray.IsVisible) { throw new InvalidOperationException("Laura must start hidden in the tray."); }
                _tray.Toggle(); await Task.Delay(150).ConfigureAwait(true);
                if (!_tray.IsVisible) { throw new InvalidOperationException("The tray visibility toggle must show Laura."); }
                WindowsWindowTheme.VerifyWindowForSmoke((Microsoft.UI.Xaml.Window)Windows[0].Handler!.PlatformView!);
                _tray.Toggle(); if (_tray.IsVisible) { throw new InvalidOperationException("The second visibility toggle must hide Laura."); }
                services.GetRequiredService<MauiShellController>().RevealForConfirmation();
                if (!_tray.IsVisible) { throw new InvalidOperationException("A requested skill review must reveal a hidden window."); }
                _tray.Hide();
                string directory = Path.GetFullPath(args[smoke + 1]); Directory.CreateDirectory(directory);
                await File.WriteAllTextAsync(Path.Combine(directory, "background-startup.txt"), "Native startup remained hidden; the same toggle used by Alt+L showed and hid the window successfully.").ConfigureAwait(true);
                _tray.Show();
            }
            if (args.Contains("--show", StringComparer.Ordinal)) { _tray?.Show(); }
            if (args.Contains("--announce-ready", StringComparer.Ordinal) && smoke < 0 && permissionSmoke < 0)
            {
                var speech = services.GetRequiredService<ISpeechSynthesizer>();
                if (services.GetRequiredService<Laura.Platform.Windows.Conversation.LlamaCppTextClient>().IsAvailable
                    && services.GetRequiredService<IChatGptInference>().IsAvailable && speech.GetAvailableVoices().Count > 0)
                {
                    using var announcement = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                    await speech.SpeakAsync(new Laura.Core.Speech.SpeechRequest(services.GetRequiredService<ILocalizer>().Get("ui.local.readySpeech"), settings.Current.Voice, settings.Current.Culture)
                        { OutputDevice = settings.Current.OutputDevice }, announcement.Token).ConfigureAwait(true);
                }
            }
            if (smoke >= 0 && smoke + 1 < args.Length)
            { await MauiSmokeRunner.RunAsync(services, Windows[0], Path.GetFullPath(args[smoke + 1])).ConfigureAwait(true); return; }
        }
        catch (Exception error)
        {
            services.GetRequiredService<ILogger<LauraApplication>>().LogError("Startup failed ({ErrorType}).", error.GetType().Name);
            int smoke = Array.FindIndex(args, argument => argument is "--ui-smoke" or "--permissions-smoke" or "--screenshot-smoke" or "--brain-smoke");
            if (smoke >= 0 && smoke + 1 < args.Length)
            {
                string directory = Path.GetFullPath(args[smoke + 1]); Directory.CreateDirectory(directory);
                string result = args.Contains("--brain-restart-smoke", StringComparer.Ordinal) ? "restart-result.txt"
                    : args.Contains("--github-restart", StringComparer.Ordinal) ? "github-restart-result.txt" : "result.txt";
                await File.WriteAllTextAsync(Path.Combine(directory, result), "FAIL: startup (" + error.GetType().Name + "). No raw error or credential was logged.").ConfigureAwait(true);
                services.GetRequiredService<Laura.Core.Abstractions.IShellController>().Shutdown();
            }
            else if (args.Contains("--show", StringComparer.Ordinal))
            { await Laura.App.Platforms.Windows.WindowsDialogs.AlertAsync("Laura", "Startup failed. Check settings and the local data directory.", "OK").ConfigureAwait(true); }
        }
    }
    /// <summary>Includes startup source collection in the same delayed voice feedback as model processing.</summary>
    private void OnEngineStateChanged(object? sender, AssistantState state)
    {
        var live = services.GetRequiredService<Laura.Platform.Windows.Speech.LocalVoiceSession>();
        if (state == AssistantState.Thinking) { live.SetThinking(); }
        else if ((state is AssistantState.Ready or AssistantState.Speaking or AssistantState.Stopped) && live.IsThinking)
        { live.ReturnToReady(); }
    }
    /// <summary>Prepares Markdown and legacy facts away from the critical interactive startup path.</summary>
    private async Task WarmBrainAsync()
    {
        using var performance = PerformanceTrace.Measure("startup.brain");
        try
        {
            await services.GetRequiredService<Laura.Core.Brain.BrainService>().InitializeAsync().ConfigureAwait(false);
            await Task.Run(services.GetRequiredService<Laura.Core.Brain.BrainConversationMemory>().Initialize).ConfigureAwait(false);
            await services.GetRequiredService<Laura.Core.Brain.BrainExperiences>().RecoverAsync().ConfigureAwait(false);
            services.GetRequiredService<Laura.Core.Brain.BrainMaintenance>().Start();
        }
        catch (Exception error) { services.GetRequiredService<ILogger<LauraApplication>>().LogWarning("Brain warmup unavailable ({ErrorType}).", error.GetType().Name); }
    }
    /// <summary>Stops background workers, microphone and speech before destroying the window.</summary>
    private async Task StopAsync()
    {
        if (_stopping) { return; }
        _stopping = true;
        services.GetRequiredService<ISettingsService>().Changed -= OnPermissionSettingsChanged;
        services.GetRequiredService<Laura.Core.Permissions.PermissionManager>().Changed -= OnApprovalsChanged;
        services.GetRequiredService<Laura.Core.Permissions.PermissionManager>().Stop();
        services.GetRequiredService<WindowsApprovalCenter>().Dispose();
        WindowsDialogs.CloseAll();
        services.GetRequiredService<IAssistantEngine>().StateChanged -= OnEngineStateChanged;
        _shortcuts?.Dispose(); _shortcuts = null;
        _screenshotShortcuts?.Dispose(); _screenshotShortcuts = null;
        await services.GetRequiredService<ScreenshotCoordinator>().StopAsync().ConfigureAwait(true);
        await services.GetRequiredService<Laura.Core.Mcp.McpRuntime>().DisposeAsync().ConfigureAwait(true);
        await services.GetRequiredService<ClapWakeCoordinator>().DisposeAsync().ConfigureAwait(true);
        await services.GetRequiredService<ReminderScheduler>().DisposeAsync().ConfigureAwait(true);
        await services.GetRequiredService<Laura.Platform.Windows.Speech.LocalVoiceSession>().DisposeAsync().ConfigureAwait(true);
        await services.GetRequiredService<HourlyAnnouncer>().DisposeAsync().ConfigureAwait(true);
        await services.GetRequiredService<IAssistantEngine>().StopAsync().ConfigureAwait(true);
        await services.GetRequiredService<Laura.App.Controls.VulkanReactorHost>().DisposeAsync().ConfigureAwait(true);
        await services.GetRequiredService<Laura.Platform.Windows.Conversation.LlamaCppRuntime>().DisposeAsync().ConfigureAwait(true);
        await services.GetRequiredService<ISpeechSynthesizer>().DisposeAsync().ConfigureAwait(true);
        await services.GetRequiredService<Laura.Platform.Windows.Speech.LocalVoiceWorkerClient>().DisposeAsync().ConfigureAwait(true);
        await services.GetRequiredService<Laura.Core.Execution.CommandJobs>().DisposeAsync().ConfigureAwait(true);
        if (_brainStartup is not null) { await _brainStartup.ConfigureAwait(true); }
        await services.GetRequiredService<Laura.Core.Brain.BrainLearning>().DisposeAsync().ConfigureAwait(true);
        await services.GetRequiredService<Laura.Core.Brain.BrainMaintenance>().DisposeAsync().ConfigureAwait(true);
        await services.GetRequiredService<Laura.Core.Brain.BrainService>().DisposeAsync().ConfigureAwait(true);
        services.GetRequiredService<RuntimeDiagnosticContext>().Dispose();
        await services.GetRequiredService<Laura.Core.Diagnostics.RuntimeJournal>().DisposeAsync().ConfigureAwait(true);
        _tray?.Dispose(); _tray = null;
    }
    /// <summary>Exits only on an explicit domain or tray exit request.</summary>
    private async Task ExitAsync(Window window) { _tray?.AllowExit(); await StopAsync().ConfigureAwait(true); CloseWindow(window); }
    private void OnApprovalsChanged(object? sender, EventArgs args) => MainThread.BeginInvokeOnMainThread(() =>
        _tray?.SetApprovalCount(services.GetRequiredService<Laura.Core.Permissions.PermissionManager>().PendingRequests.Count));
    private void OnPermissionSettingsChanged(object? sender, Laura.Core.Configuration.LauraSettings settings)
    {
        if (_permissionOptions == settings.Permissions) { return; }
        Laura.Core.Diagnostics.LauraDiagnostics.Record("permission.mode_changed", outcome: settings.Permissions.Mode.ToString());
        _permissionOptions = settings.Permissions; _ = PersistPermissionOptionsAsync();
    }
    private async Task PersistPermissionOptionsAsync()
    {
        try { await Task.Run(services.GetRequiredService<Laura.Core.Permissions.PermissionManager>().RefreshOptions).ConfigureAwait(false); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or Laura.Core.Permissions.PermissionDeniedException)
        { services.GetRequiredService<ILogger<LauraApplication>>().LogWarning("Permission retention could not be persisted ({ErrorType}).", error.GetType().Name); }
    }
    /// <inheritdoc />
    public async ValueTask DisposeAsync() { await StopAsync().ConfigureAwait(true); GC.SuppressFinalize(this); }
}
