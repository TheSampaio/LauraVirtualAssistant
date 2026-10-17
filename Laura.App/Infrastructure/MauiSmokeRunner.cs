using Laura.App.Views;
using Laura.App.Theming;
using Laura.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Laura.App.Infrastructure;

/// <summary>Opt-in UI render verification that uses isolated storage and never opens a microphone.</summary>
public static class MauiSmokeRunner
{
    private static readonly System.Text.Json.JsonSerializerOptions DiagnosticJson = new() { WriteIndented = true };
    private static readonly string[] WorkspaceActionStates = ["Normal", "PointerOver", "Focused", "Pressed", "Disabled"];
    /// <summary>Captures the native MAUI window across languages, pages and sizes, then records completion.</summary>
    public static async Task RunAsync(IServiceProvider services, Window window, string directory)
    {
        Directory.CreateDirectory(directory);
        VerifyReactorTransitions();
        try
        {
            if (Environment.GetCommandLineArgs().Contains("--chatgpt-live-smoke", StringComparer.Ordinal) && Environment.GetCommandLineArgs().Contains("--provider-handoff-smoke", StringComparer.Ordinal))
            { await ProviderHandoffSmokeRunner.RunAsync(services, window, directory).ConfigureAwait(true); return; }
            if (Environment.GetCommandLineArgs().Contains("--chatgpt-live-smoke", StringComparer.Ordinal) && Environment.GetCommandLineArgs().Contains("--rea-application-smoke", StringComparer.Ordinal))
            { await ChatGptApplicationSmokeRunner.RunAsync(services, window, directory).ConfigureAwait(true); return; }
            if (Environment.GetCommandLineArgs().Contains("--chatgpt-live-smoke", StringComparer.Ordinal) && Environment.GetCommandLineArgs().Contains("--rea-continuation-smoke", StringComparer.Ordinal))
            { await ChatGptReaSmokeRunner.RunAsync(services, window, directory).ConfigureAwait(true); return; }
            if (Environment.GetCommandLineArgs().Contains("--chatgpt-live-smoke", StringComparer.Ordinal))
            { await ChatGptLiveSmokeRunner.RunAsync(services, window, directory).ConfigureAwait(true); return; }
            if (Environment.GetCommandLineArgs().Contains("--chatgpt-ui-smoke", StringComparer.Ordinal))
            { await ChatGptUiSmokeRunner.RunAsync(services, window, directory).ConfigureAwait(true); return; }
            if (Environment.GetCommandLineArgs().Contains("--agent-rework-smoke", StringComparer.Ordinal))
            { await AgentReworkSmokeRunner.RunAsync(services, window, directory).ConfigureAwait(true); return; }
            if (Environment.GetCommandLineArgs().Contains("--command-jobs-smoke", StringComparer.Ordinal))
            { await CommandJobsSmokeRunner.RunAsync(services, window, directory).ConfigureAwait(true); return; }
            if (Environment.GetCommandLineArgs().Contains("--task-recovery-smoke", StringComparer.Ordinal))
            { await TaskRecoverySmokeRunner.RunAsync(services, window, directory).ConfigureAwait(true); return; }
            if (Environment.GetCommandLineArgs().Contains("--chat-diagnostics-smoke", StringComparer.Ordinal))
            { await ChatDiagnosticsSmokeRunner.RunAsync(services, window, directory).ConfigureAwait(true); return; }
            if (Environment.GetCommandLineArgs().Contains("--voice-cancellation-smoke", StringComparer.Ordinal))
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                await services.GetRequiredService<Laura.Platform.Windows.Conversation.GeminiLiveClient>().VerifyVoiceCancellationForSmokeAsync(timeout.Token).ConfigureAwait(true);
                await File.WriteAllTextAsync(Path.Combine(directory, "result.txt"), "Eight reconfigurations and four client disposals drained/canceled completed-voice callbacks; no late history notification, retained availability or microphone capture occurred.").ConfigureAwait(true); return;
            }
            if (Environment.GetCommandLineArgs().Contains("--audio-fragment-smoke", StringComparer.Ordinal))
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var metrics = await Laura.Platform.Windows.Conversation.GeminiLiveClient.VerifyFragmentedPlaybackForSmokeAsync(timeout.Token).ConfigureAwait(true);
                await File.WriteAllTextAsync(Path.Combine(directory, "audio-fragment.json"), System.Text.Json.JsonSerializer.Serialize(metrics, DiagnosticJson)).ConfigureAwait(true);
                await File.WriteAllTextAsync(Path.Combine(directory, "result.txt"), "Native fragmented PCM byte preservation measured; alignment counters are in audio-fragment.json.").ConfigureAwait(true); return;
            }
            if (Environment.GetCommandLineArgs().Contains("--brain-performance-smoke", StringComparer.Ordinal))
            { await BrainPerformanceSmokeRunner.RunAsync(services, window, directory).ConfigureAwait(true); return; }
            await Task.Delay(1000).ConfigureAwait(true);
            var shell = services.GetRequiredService<Laura.App.Shell.MauiShellController>();
            Laura.App.Platforms.Windows.WindowsWindowTheme.VerifyCloseAndReopenForSmoke((Microsoft.UI.Xaml.Window)window.Handler!.PlatformView!, shell.RevealForConfirmation);
            var brain = services.GetRequiredService<Laura.Core.Brain.BrainService>();
            var learning = services.GetRequiredService<Laura.Core.Brain.BrainLearning>();
            await learning.LearnAsync("I am working on project Demonstration.").ConfigureAwait(true);
            await learning.LearnAsync("I decided to use CSharp in project Demonstration.").ConfigureAwait(true);
            var brainSkills = services.GetRequiredService<Laura.Core.Brain.BrainSkills>();
            var sampleSkill = await brainSkills.ImportAsync("---\nname: Synthetic prose guide\ndescription: Write an essay with natural phrasing.\n---\nSynthetic instruction: use plain sentences.", "Synthetic prose guide").ConfigureAwait(true);
            var assistantTools = services.GetRequiredService<IAssistantTools>();
            using (var emptyArguments = System.Text.Json.JsonDocument.Parse("{}"))
            {
                var listed = await assistantTools.ExecuteAsync("list_brain_skills", emptyArguments.RootElement, CancellationToken.None).ConfigureAwait(true);
                if (!listed.Text.Contains("Synthetic prose guide", StringComparison.Ordinal)) { throw new InvalidOperationException("Native skill discovery lost disabled imports."); }
            }
            sampleSkill = await brainSkills.SetEnabledAsync(sampleSkill, true).ConfigureAwait(true);
            using (var skillArguments = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(new { name = sampleSkill.Id })))
            {
                var loaded = await assistantTools.ExecuteAsync("read_brain_skill", skillArguments.RootElement, CancellationToken.None).ConfigureAwait(true);
                if (!loaded.Text.Contains("use plain sentences", StringComparison.Ordinal)) { throw new InvalidOperationException("Native skill retrieval did not read approved Markdown."); }
            }
            await File.WriteAllTextAsync(Path.Combine(directory, "brain-skill-tools.txt"), "Native tools discovered a disabled synthetic skill by description and loaded its real instructions after isolated hash approval. No cloud request or private Brain was used.").ConfigureAwait(true);
            var liveClient = services.GetRequiredService<Laura.Platform.Windows.Conversation.GeminiLiveClient>();
            if (!liveClient.IsAvailable)
            {
                string? farewell = await liveClient.AskAsync(new Laura.Core.Conversation.ConversationTurn("stop listening", System.Globalization.CultureInfo.GetCultureInfo("en-US"), "Synthetic owner")).ConfigureAwait(true);
                bool speechWasAttempted = false;
                try { await liveClient.SpeakAsync(farewell!, CancellationToken.None).ConfigureAwait(true); }
                catch (InvalidOperationException) { speechWasAttempted = true; }
                if (!speechWasAttempted) { throw new InvalidOperationException("Standby text was incorrectly marked as already spoken."); }
                await File.WriteAllTextAsync(Path.Combine(directory, "standby-speech.txt"), "Typed standby acknowledgment reaches speech instead of being falsely suppressed as already played. Missing test credentials were respected; no cloud or microphone was used.").ConfigureAwait(true);
            }
            var navigation = (NavigationPage)window.Page!;
            var nativeWindow = (Microsoft.UI.Xaml.Window)window.Handler!.PlatformView!;
            if (nativeWindow.AppWindow.TitleBar.PreferredTheme != Microsoft.UI.Windowing.TitleBarTheme.Dark)
            { throw new InvalidOperationException("Native title bar did not retain the dark theme."); }
            if (window.TitleBar is not TitleBar mauiTitle || mauiTitle.BackgroundColor.ToArgbHex() != "#100D12" || mauiTitle.ForegroundColor.ToArgbHex() != "#FFFFFF")
            { throw new InvalidOperationException("The MAUI title layer must have the exact background and white text, alongside the native caption buttons."); }
            await File.WriteAllTextAsync(Path.Combine(directory, "window-theme.txt"),
                $"MAUI title: {mauiTitle.BackgroundColor.ToArgbHex()} / {mauiTitle.ForegroundColor.ToArgbHex()}; native title theme: {nativeWindow.AppWindow.TitleBar.PreferredTheme}; background: {nativeWindow.AppWindow.TitleBar.BackgroundColor}; buttons: {nativeWindow.AppWindow.TitleBar.ButtonBackgroundColor}").ConfigureAwait(true);
            var gpu = services.GetRequiredService<Laura.Platform.Windows.System.WindowsGpuMonitor>();
            _ = gpu.Sample(); await Task.Delay(1000).ConfigureAwait(true);
            double? gpuUsage = gpu.Sample();
            await File.WriteAllTextAsync(Path.Combine(directory, "gpu.txt"), gpuUsage is { } value ? $"Measured GPU utilization: {value:P2}" : "GPU counters unavailable on this driver.").ConfigureAwait(true);
            var resources = services.GetRequiredService<Laura.Platform.Windows.System.WindowsResourceMonitor>();
            var hardware = await Task.Run(() => resources.Sample(AppContext.BaseDirectory)).ConfigureAwait(true);
            await File.WriteAllTextAsync(Path.Combine(directory, "hardware.json"), System.Text.Json.JsonSerializer.Serialize(hardware, DiagnosticJson)).ConfigureAwait(true);
            var tools = services.GetRequiredService<IAssistantTools>();
            var discovery = new List<string>();
            foreach (string name in new[] { "Steam", "PCSX2" })
            {
                using var arguments = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(new { query = name }));
                var result = await tools.ExecuteAsync("list_apps", arguments.RootElement, CancellationToken.None).ConfigureAwait(true); discovery.Add(name + ": " + result.Text);
                if (Environment.GetCommandLineArgs().Contains("--launch-apps-smoke", StringComparer.Ordinal))
                { using var launch = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(new { name })); var started = await tools.ExecuteAsync("open_app", launch.RootElement, CancellationToken.None).ConfigureAwait(true); discovery.Add("Launch " + name + ": " + started.Text); }
            }
            await File.WriteAllLinesAsync(Path.Combine(directory, "applications.txt"), discovery).ConfigureAwait(true);
            if (Environment.GetCommandLineArgs().Contains("--live-tools-smoke", StringComparer.Ordinal))
            {
                var settings = services.GetRequiredService<ISettingsService>();
                await settings.UpdateAsync(settings.Current with { Culture = "pt-BR", Voice = settings.Current.Voice with { Volume = 0 } }).ConfigureAwait(true);
                await services.GetRequiredService<Laura.Platform.Windows.Conversation.GeminiLiveClient>().ReconfigureAsync().ConfigureAwait(true);
                await services.GetRequiredService<IAssistantEngine>().SubmitCommandAsync("Abra a Steam e o meu emulador de PS2, PCSX2.").ConfigureAwait(true);
                var nativeTools = (MauiAssistantTools)tools;
                if (!nativeTools.StartedApplications.Contains("Steam", StringComparer.OrdinalIgnoreCase) || !nativeTools.StartedApplications.Contains("PCSX2", StringComparer.OrdinalIgnoreCase)) { throw new InvalidOperationException("Gemini did not execute both real application tools."); }
                await File.WriteAllTextAsync(Path.Combine(directory, "live-tools.txt"), "Gemini selected and executed open_app for Steam and PCSX2; native launches confirmed.").ConfigureAwait(true);
            }
            await CaptureAsync(window, Path.Combine(directory, "hud-en-US.png")).ConfigureAwait(true);
            var main = services.GetRequiredService<MainPage>();
            main.VerifyHudForSmoke();
            using (var playbackTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
            { await Laura.Platform.Windows.Conversation.GeminiLiveClient.VerifyPlaybackForSmokeAsync(playbackTimeout.Token).ConfigureAwait(true); }
            await File.WriteAllTextAsync(Path.Combine(directory, "playback-buffer.txt"), "Eight silent native output restarts preserved every PCM byte, including the short leading fragment. No microphone was opened.").ConfigureAwait(true);
            var localWake = services.GetRequiredService<Laura.Platform.Windows.Conversation.WindowsClapListener>();
            await localWake.StartAsync(CancellationToken.None).ConfigureAwait(true);
            if (localWake.IsCapturing) { throw new InvalidOperationException("Wake detection must remain off unless the user explicitly enables it."); }
            main.PreviewConversationForSmoke(); await Task.Delay(200).ConfigureAwait(true);
            await CaptureAsync(window, Path.Combine(directory, "hud-conversation-en-US.png")).ConfigureAwait(true);
            foreach (var mode in new[] { Controls.ReactorMode.Listening, Controls.ReactorMode.Thinking, Controls.ReactorMode.Working, Controls.ReactorMode.Speaking })
            { main.PreviewReactorForSmoke(mode); await Task.Delay(180).ConfigureAwait(true); await CaptureAsync(window, Path.Combine(directory, "reactor-" + mode + ".png")).ConfigureAwait(true); }
            main.PreviewReactorForSmoke(Controls.ReactorMode.Thinking);
            main.PreviewReactorAngleForSmoke(0); await Task.Delay(100).ConfigureAwait(true);
            byte[] thinkingStart = await VisualPixelsAsync(main.ReactorVisualForSmoke()).ConfigureAwait(true);
            await CaptureAsync(window, Path.Combine(directory, "thinking-frame-0.png")).ConfigureAwait(true);
            main.PreviewReactorAngleForSmoke(22.5f); await Task.Delay(100).ConfigureAwait(true);
            byte[] thinkingNext = await VisualPixelsAsync(main.ReactorVisualForSmoke()).ConfigureAwait(true);
            await CaptureAsync(window, Path.Combine(directory, "thinking-frame-1.png")).ConfigureAwait(true);
            if (thinkingStart.Length == 0 || thinkingStart.Length != thinkingNext.Length || thinkingStart.SequenceEqual(thinkingNext))
            { throw new InvalidOperationException("Thinking animation did not render distinct non-empty frames."); }
            main.PreviewReactorAngleForSmoke(Controls.AssistantCoreDrawable.CycleDegrees - .001f); await Task.Delay(100).ConfigureAwait(true);
            byte[] thinkingEnd = await VisualPixelsAsync(main.ReactorVisualForSmoke()).ConfigureAwait(true);
            double thinkingDifference = thinkingStart.Zip(thinkingEnd, (first, last) => Math.Abs(first - last)).Average();
            if (thinkingDifference > .25) { throw new InvalidOperationException("Thinking animation jumps at its cycle boundary."); }
            await File.WriteAllTextAsync(Path.Combine(directory, "thinking-animation.txt"), "Distinct native thinking frames and continuous cycle boundary verified.").ConfigureAwait(true);
            main.PreviewReactorForSmoke(null);
            main.PreviewReactorAngleForSmoke(0); await Task.Delay(100).ConfigureAwait(true);
            byte[] firstFrame = await VisualPixelsAsync(main.ReactorVisualForSmoke()).ConfigureAwait(true);
            main.PreviewReactorAngleForSmoke(Controls.AssistantCoreDrawable.CycleDegrees - .001f); await Task.Delay(100).ConfigureAwait(true);
            byte[] lastFrame = await VisualPixelsAsync(main.ReactorVisualForSmoke()).ConfigureAwait(true);
            if (firstFrame.Length != lastFrame.Length || firstFrame.Length == 0) { throw new InvalidOperationException("Reactor continuity images must have equal non-empty geometry."); }
            double difference = firstFrame.Zip(lastFrame, (first, last) => Math.Abs(first - last)).Average();
            if (difference > .25) { throw new InvalidOperationException("Reactor cycle jumps at its common ring boundary: " + difference); }
            await File.WriteAllTextAsync(Path.Combine(directory, "reactor-continuity.txt"), "Rendered cycle boundary mean channel difference: " + difference.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)).ConfigureAwait(true);
            main.PreviewReactorAngleForSmoke(null);
            var fullscreenWindow = (Microsoft.UI.Xaml.Window)window.Handler!.PlatformView!;
            Laura.App.Platforms.Windows.WindowsWindowTheme.ToggleFullscreen(fullscreenWindow);
            Laura.App.Platforms.Windows.WindowsWindowTheme.VerifyCloseAndReopenForSmoke(fullscreenWindow, shell.RevealForConfirmation);
            await Task.Delay(200).ConfigureAwait(true); await CaptureAsync(window, Path.Combine(directory, "hud-fullscreen.png")).ConfigureAwait(true);
            Laura.App.Platforms.Windows.WindowsWindowTheme.ToggleFullscreen(fullscreenWindow);
            await Task.Delay(200).ConfigureAwait(true);
            Laura.App.Platforms.Windows.WindowsWindowTheme.VerifyWindowForSmoke(fullscreenWindow);
            await File.WriteAllTextAsync(Path.Combine(directory, "window-controls.txt"), "Native caption styles contain no minimize/maximize buttons. Close hides to tray; reopening after moving the window centers it in the monitor work area. The F11 transition enters fullscreen, survives tray reopening and restores a centered close-only window.").ConfigureAwait(true);
            foreach (string culture in new[] { "en-US", "pt-BR" })
            {
                var navigationStyles = new Dictionary<int, string>();
                ISettingsService settings = services.GetRequiredService<ISettingsService>();
                await settings.UpdateAsync(settings.Current with { Culture = culture, UiCulture = culture }).ConfigureAwait(true);
                services.GetRequiredService<IUiLocalizer>().SetCulture(settings.Current.ResolveUiCulture());
                main.Reload(); main.PreviewConversationForSmoke(); await Task.Delay(500).ConfigureAwait(true);
                await CaptureAsync(window, Path.Combine(directory, "hud-conversation-" + culture + ".png")).ConfigureAwait(true);
                foreach (Type type in new[] { typeof(SetupPage), typeof(MemoryPage) })
                {
                    await navigation.PushAsync((Page)services.GetRequiredService(type), false).ConfigureAwait(true);
                    await Task.Delay(350).ConfigureAwait(true);
                    VerifyWorkspaceNavigation(window, navigation.CurrentPage, navigationStyles, services.GetRequiredService<IUiLocalizer>().Get("ui.navigation.backToLaura"));
                    await CaptureAsync(window, Path.Combine(directory, type.Name + "-" + culture + ".png")).ConfigureAwait(true);
                    if (navigation.CurrentPage is MemoryPage memory)
                    {
                        await memory.VerifyFolderExpansionForSmokeAsync().ConfigureAwait(true);
                        await File.WriteAllTextAsync(Path.Combine(directory, "brain-folder-disclosure.txt"), "Native disclosure button opened and closed a folder six times. Selection restoration did not toggle disclosure, reconstruct indefinitely or cover rendered nodes with an empty-state message.").ConfigureAwait(true);
                        foreach (string mode in new[] { "graph", "local", "read", "edit", "split" })
                        { await memory.PreviewBrainForSmokeAsync(mode).ConfigureAwait(true); await Task.Delay(180).ConfigureAwait(true); memory.VerifyBrainForSmoke(); await CaptureAsync(window, Path.Combine(directory, "brain-" + mode + "-" + culture + ".png")).ConfigureAwait(true); }
                        window.Width = 720; window.Height = 600;
                        foreach (string mode in new[] { "graph", "read", "details", "split" })
                        {
                            await memory.PreviewBrainForSmokeAsync(mode).ConfigureAwait(true); await Task.Delay(250).ConfigureAwait(true);
                            VerifyWorkspaceNavigation(window, memory, navigationStyles, services.GetRequiredService<IUiLocalizer>().Get("ui.navigation.backToLaura"));
                            await CaptureAsync(window, Path.Combine(directory, "brain-compact-" + mode + "-" + culture + ".png")).ConfigureAwait(true); memory.VerifyBrainForSmoke();
                            if (mode == "details") { var native = (Microsoft.UI.Xaml.Window)window.Handler!.PlatformView!; string[] geometry = Describe(native.Content, native.Content).ToArray(); await File.WriteAllLinesAsync(Path.Combine(directory, "brain-details-layout-" + culture + ".txt"), geometry).ConfigureAwait(true); }
                        }
                        window.Width = 1440; window.Height = 900;
                        await memory.PreviewBrainForSmokeAsync("graph").ConfigureAwait(true); await Task.Delay(250).ConfigureAwait(true); memory.VerifyBrainForSmoke();
                        await CaptureAsync(window, Path.Combine(directory, "brain-return-" + culture + ".png")).ConfigureAwait(true);
                        await File.WriteAllTextAsync(Path.Combine(directory, "brain.txt"), "Synthetic declarations persisted as Markdown, created attributed graph edges, rendered dark/red global/local graph, reader, editor, split, SVG file tree, selected actions and minimum 720x600 including details; native editor changed the real Markdown file.").ConfigureAwait(true);
                        using var maintenancePage = new BrainMaintenancePage(services.GetRequiredService<Laura.Core.Brain.BrainService>(), services.GetRequiredService<Laura.Core.Brain.BrainMaintenance>(),
                            services.GetRequiredService<ISettingsService>(), services.GetRequiredService<IUiLocalizer>(), _ => Task.CompletedTask);
                        await NavigationMotion.PushAsync(navigation.Navigation, maintenancePage).ConfigureAwait(true); await maintenancePage.AnalyzeForSmokeAsync().ConfigureAwait(true);
                        await Task.Delay(150).ConfigureAwait(true); await CaptureAsync(window, Path.Combine(directory, "maintenance-" + culture + ".png")).ConfigureAwait(true);
                        window.Width = 720; window.Height = 600; await Task.Delay(200).ConfigureAwait(true); await CaptureAsync(window, Path.Combine(directory, "maintenance-compact-" + culture + ".png")).ConfigureAwait(true);
                        await NavigationMotion.PopAsync(navigation.Navigation).ConfigureAwait(true); window.Width = 1440; window.Height = 900;
                    }
                    if (navigation.CurrentPage is SetupPage setup)
                    {
                        setup.VerifySettingsDraftForSmoke(); await Task.Delay(200).ConfigureAwait(true);
                        if (culture == "en-US" && Environment.GetCommandLineArgs().Contains("--key-ui-smoke", StringComparer.Ordinal))
                        {
                            await setup.VerifyCredentialInputForSmokeAsync().ConfigureAwait(true);
                            await CaptureAsync(window, Path.Combine(directory, "setup-key-masked.png")).ConfigureAwait(true);
                            await File.WriteAllTextAsync(Path.Combine(directory, "key-ui.txt"), "Valid key verified and masked; saving again preserved it; an edit during validation kept the verified credential and new draft; invalid replacement was rejected without changing the saved credential.").ConfigureAwait(true);
                        }
                        for (int tab = 0; tab < setup.TabCount; tab++)
                        {
                            setup.SelectTab(tab); await Task.Delay(250).ConfigureAwait(true);
                            if (tab == 3) { var native = (Microsoft.UI.Xaml.Window)window.Handler!.PlatformView!; string[] geometry = Describe(native.Content, native.Content).ToArray(); await File.WriteAllLinesAsync(Path.Combine(directory, "setup-apps-layout-" + culture + ".txt"), geometry).ConfigureAwait(true); }
                            setup.VerifySettingsForSmoke(); await CaptureAsync(window, Path.Combine(directory, "setup-tab-" + tab + "-" + culture + ".png")).ConfigureAwait(true);
                            if (tab is 1 or 2 or 4 or 5)
                            {
                                await setup.VerifySettingsScrollForSmokeAsync(true).ConfigureAwait(true);
                                await CaptureAsync(window, Path.Combine(directory, "setup-bottom-" + tab + "-" + culture + ".png")).ConfigureAwait(true);
                                await setup.VerifySettingsScrollForSmokeAsync(false).ConfigureAwait(true);
                            }
                        }
                        window.Width = 720; window.Height = 600;
                        for (int tab = 0; tab < setup.TabCount; tab++)
                        {
                            setup.SelectTab(tab); await Task.Delay(250).ConfigureAwait(true); setup.VerifySettingsForSmoke();
                            VerifyWorkspaceNavigation(window, setup, navigationStyles, services.GetRequiredService<IUiLocalizer>().Get("ui.navigation.backToLaura"));
                            await CaptureAsync(window, Path.Combine(directory, "setup-compact-" + tab + "-" + culture + ".png")).ConfigureAwait(true);
                            if (tab is 1 or 2 or 4 or 5)
                            {
                                await setup.VerifySettingsScrollForSmokeAsync(true).ConfigureAwait(true);
                                await CaptureAsync(window, Path.Combine(directory, "setup-compact-bottom-" + tab + "-" + culture + ".png")).ConfigureAwait(true);
                                await setup.VerifySettingsScrollForSmokeAsync(false).ConfigureAwait(true);
                            }
                        }
                        window.Width = 1440; window.Height = 900; setup.SelectTab(0); await Task.Delay(250).ConfigureAwait(true); setup.VerifySettingsForSmoke();
                    }
                    if (type == typeof(SetupPage) && culture == "en-US")
                    {
                        var native = (Microsoft.UI.Xaml.Window)window.Handler!.PlatformView!;
                        string[] layout = Describe(native.Content, native.Content).ToArray();
                        await File.WriteAllLinesAsync(Path.Combine(directory, "native-layout.txt"), layout).ConfigureAwait(true);
                    }
                    window.Width = 1280; window.Height = 800; await Task.Delay(350).ConfigureAwait(true);
                    VerifyWorkspaceNavigation(window, navigation.CurrentPage, navigationStyles, services.GetRequiredService<IUiLocalizer>().Get("ui.navigation.backToLaura"));
                    if (navigation.CurrentPage is MemoryPage mediumBrain) { mediumBrain.VerifyBrainForSmoke(); }
                    if (navigation.CurrentPage is SetupPage mediumSettings) { mediumSettings.VerifySettingsForSmoke(); }
                    await CaptureAsync(window, Path.Combine(directory, type.Name + "-medium-" + culture + ".png")).ConfigureAwait(true);
                    window.Width = 1440; window.Height = 900; await Task.Delay(250).ConfigureAwait(true);
                    await navigation.PopAsync(false).ConfigureAwait(true);
                }
            }
            window.Width = 900; window.Height = 720;
            await File.WriteAllTextAsync(Path.Combine(directory, "workspace-navigation.txt"), "Settings and Brain share the same rendered SVG back button, hit target, color, radius, padding, header position, localized description and keyboard/pointer states at full and compact widths.").ConfigureAwait(true);
            await Task.Delay(350).ConfigureAwait(true);
            main.VerifyHudForSmoke();
            await CaptureAsync(window, Path.Combine(directory, "hud-compact.png")).ConfigureAwait(true);
            main.PreviewMonitorForSmoke(true); await Task.Delay(200).ConfigureAwait(true); main.VerifyHudForSmoke();
            await main.VerifyMonitorScrollForSmokeAsync(true).ConfigureAwait(true);
            await CaptureAsync(window, Path.Combine(directory, "hud-monitor-bottom.png")).ConfigureAwait(true);
            await main.VerifyMonitorScrollForSmokeAsync(false).ConfigureAwait(true);
            await CaptureAsync(window, Path.Combine(directory, "hud-compact-monitor.png")).ConfigureAwait(true);
            main.PreviewMonitorForSmoke(false);
            window.Width = 720; window.Height = 600; await Task.Delay(350).ConfigureAwait(true); main.VerifyHudForSmoke();
            await main.VerifyConversationScrollingForSmokeAsync().ConfigureAwait(true);
            await File.WriteAllTextAsync(Path.Combine(directory, "conversation-scrolling.txt"), "Bounded message burst, immediate history replacement, numeric scrolling and preservation of earlier reading verified without a detached element.").ConfigureAwait(true);
            await CaptureAsync(window, Path.Combine(directory, "hud-minimum.png")).ConfigureAwait(true);
            main.PreviewMonitorForSmoke(true); await Task.Delay(200).ConfigureAwait(true); main.VerifyHudForSmoke();
            await CaptureAsync(window, Path.Combine(directory, "hud-minimum-monitor.png")).ConfigureAwait(true);
            main.PreviewMonitorForSmoke(false);
            await main.ToggleMicrophoneAsync().ConfigureAwait(true); await Task.Delay(150).ConfigureAwait(true); main.VerifyHudForSmoke();
            await CaptureAsync(window, Path.Combine(directory, "hud-mic-enabled.png")).ConfigureAwait(true);
            await main.ToggleMicrophoneAsync().ConfigureAwait(true);
            window.Width = 1440; window.Height = 900; await Task.Delay(500).ConfigureAwait(true);
            var navigationTimes = new List<double>();
            for (int visit = 0; visit < 3; visit++)
            {
                var elapsed = System.Diagnostics.Stopwatch.StartNew();
                await main.OpenSetupAsync().ConfigureAwait(true); await NavigationMotion.PopAsync(navigation.Navigation).ConfigureAwait(true);
                navigationTimes.Add(elapsed.Elapsed.TotalMilliseconds);
            }
            await File.WriteAllTextAsync(Path.Combine(directory, "navigation-timings.txt"), string.Join("\n", navigationTimes.Select(time => time.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + " ms (open + return, two 300 ms arrival transitions when system animations are enabled)"))).ConfigureAwait(true);
            for (int visit = 0; visit < 3; visit++) { await main.OpenBrainAsync().ConfigureAwait(true); await NavigationMotion.PopAsync(navigation.Navigation).ConfigureAwait(true); }
            await Task.Delay(150).ConfigureAwait(true);
            main.VerifyHudForSmoke();
            await CaptureAsync(window, Path.Combine(directory, "hud-final-pt-BR.png")).ConfigureAwait(true);
            await File.WriteAllTextAsync(Path.Combine(directory, "result.txt"), "MAUI HUD, dark/red Brain views and SVG file tree, actual Markdown edit/save states, responsive inspector and graph, localized conversations, reactor states, navigation, EN/PT pages, compact monitor access, minimum 720x600 window, accessible actions and microphone safety verified successfully.").ConfigureAwait(true);
        }
        catch (Exception exception) { await File.WriteAllTextAsync(Path.Combine(directory, "result.txt"), exception.ToString()).ConfigureAwait(true); }
        finally { await PerformanceTrace.WriteAsync(directory).ConfigureAwait(true); services.GetRequiredService<Laura.Core.Abstractions.IShellController>().Shutdown(); }
    }
    /// <summary>Compares rendered workspace navigation at each window width, independently of its factory.</summary>
    private static void VerifyWorkspaceNavigation(Window window, Page page, Dictionary<int, string> appearances, string description)
    {
        var back = VisualDescendants(page).OfType<ImageButton>().SingleOrDefault(button => button.AutomationId == "workspace-back")
            ?? throw new InvalidOperationException("The workspace has no unique back action.");
        if (Math.Abs(back.Width - Theme.TargetSize) > 1 || Math.Abs(back.Height - Theme.TargetSize) > 1)
        { throw new InvalidOperationException("The workspace back action has an inconsistent hit target."); }
        if (SemanticProperties.GetDescription(back) != description || ToolTipProperties.GetText(back) as string != description)
        { throw new InvalidOperationException("The workspace back action has an inconsistent accessible label."); }
        var states = VisualStateManager.GetVisualStateGroups(back).SelectMany(group => group.States).Select(state => state.Name).ToHashSet(StringComparer.Ordinal);
        if (!states.IsSupersetOf(WorkspaceActionStates))
        { throw new InvalidOperationException("The workspace back action lacks shared keyboard and pointer feedback."); }
        var nativeWindow = (Microsoft.UI.Xaml.Window)window.Handler!.PlatformView!;
        var native = (Microsoft.UI.Xaml.FrameworkElement)back.Handler!.PlatformView!;
        var point = native.TransformToVisual(nativeWindow.Content).TransformPoint(new global::Windows.Foundation.Point(0, 0));
        string appearance = $"{back.Width:F0}x{back.Height:F0};{back.CornerRadius};{back.Padding};{back.BackgroundColor};{(back.Source as FileImageSource)?.File};{point.X:F0},{point.Y:F0}";
        int width = (int)Math.Round(page.Width);
        if (appearances.TryGetValue(width, out string? previous) && previous != appearance)
        { throw new InvalidOperationException("Settings and Brain rendered different navigation controls: " + previous + " / " + appearance); }
        appearances[width] = appearance;
    }
    /// <summary>Walks the live MAUI visual tree without reading settings, credentials or authored content.</summary>
    private static IEnumerable<IVisualTreeElement> VisualDescendants(IVisualTreeElement element)
    {
        foreach (IVisualTreeElement child in element.GetVisualChildren())
        { yield return child; foreach (IVisualTreeElement nested in VisualDescendants(child)) { yield return nested; } }
    }
    /// <summary>Reads one native visual's pixels for actual drawable continuity verification.</summary>
    private static async Task<byte[]> VisualPixelsAsync(Microsoft.UI.Xaml.UIElement visual)
    {
        var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(visual); var buffer = await bitmap.GetPixelsAsync();
        byte[] pixels = new byte[buffer.Length]; using var reader = DataReader.FromBuffer(buffer); reader.ReadBytes(pixels); return pixels;
    }
    /// <summary>Records only native element geometry and colors for UI layout diagnostics.</summary>
    private static IEnumerable<string> Describe(Microsoft.UI.Xaml.DependencyObject element, Microsoft.UI.Xaml.UIElement root)
    {
        if (element is Microsoft.UI.Xaml.FrameworkElement frame)
        {
            var position = frame.TransformToVisual(root).TransformPoint(new global::Windows.Foundation.Point(0, 0));
            Microsoft.UI.Xaml.Media.Brush? brush = frame switch
            {
                Microsoft.UI.Xaml.Controls.Panel panel => panel.Background,
                Microsoft.UI.Xaml.Controls.Control control => control.Background,
                Microsoft.UI.Xaml.Controls.Border border => border.Background,
                _ => null,
            };
            string color = brush is Microsoft.UI.Xaml.Media.SolidColorBrush solid ? solid.Color.ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
            string clip = frame.Clip is Microsoft.UI.Xaml.Media.RectangleGeometry rectangle ? rectangle.Rect.ToString(System.Globalization.CultureInfo.InvariantCulture) : frame.Clip?.GetType().Name ?? "";
            string viewport = frame is Microsoft.UI.Xaml.Controls.ScrollViewer viewer ? $" viewport={viewer.ViewportWidth:F0}x{viewer.ViewportHeight:F0} extent={viewer.ExtentWidth:F0}x{viewer.ExtentHeight:F0}" : "";
            yield return $"{frame.GetType().Name} {frame.Name} x={position.X:F0} y={position.Y:F0} w={frame.ActualWidth:F0} h={frame.ActualHeight:F0} color={color} clip={clip}{viewport}";
        }
        for (int index = 0; index < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(element); index++)
        { foreach (string row in Describe(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(element, index), root)) { yield return row; } }
    }
    /// <summary>Checks elapsed-time equivalence, rapid state changes and reduced-motion settling on the actual UI drawable.</summary>
    private static void VerifyReactorTransitions()
    {
        var coarse = new Controls.AssistantCoreDrawable { Mode = Controls.ReactorMode.Thinking };
        var fine = new Controls.AssistantCoreDrawable { Mode = Controls.ReactorMode.Thinking };
        coarse.Advance(.1f, true);
        for (int frame = 0; frame < 10; frame++) { fine.Advance(.01f, true); }
        if (MathF.Abs(coarse.ThinkingAmount - fine.ThinkingAmount) > .00001f || coarse.ThinkingAmount is <= 0 or >= 1)
        { throw new InvalidOperationException("Reactor transition depends on frame count instead of elapsed time."); }
        foreach (Controls.ReactorMode mode in Enum.GetValues<Controls.ReactorMode>())
        {
            fine.Mode = mode; fine.Advance(.016f, true);
            if (!float.IsFinite(fine.ActivityAmount) || fine.ActivityAmount is < 0 or > 1 || fine.ThinkingAmount is < 0 or > 1 || fine.SpeakingAmount is < 0 or > 1)
            { throw new InvalidOperationException("A rapid reactor state transition exceeded its visual bounds."); }
        }
        fine.Mode = Controls.ReactorMode.Idle; fine.Advance(0, false);
        if (fine.ActivityAmount != 0 || fine.ThinkingAmount != 0 || fine.SpeakingAmount != 0)
        { throw new InvalidOperationException("Reduced-motion reactor did not settle immediately."); }
    }
    /// <summary>Captures the application's native WinUI content rather than the user's desktop.</summary>
    internal static async Task CaptureAsync(Window window, string path)
    {
        var native = (Microsoft.UI.Xaml.Window)window.Handler!.PlatformView!;
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(native.Content);
        var buffer = await bitmap.GetPixelsAsync();
        byte[] pixels = new byte[buffer.Length];
        using (var reader = DataReader.FromBuffer(buffer)) { reader.ReadBytes(pixels); }
        using var stream = new InMemoryRandomAccessStream();
        BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels);
        await encoder.FlushAsync(); stream.Seek(0);
        await using Stream input = stream.AsStreamForRead();
        await using FileStream output = File.Create(path);
        await input.CopyToAsync(output).ConfigureAwait(true);
    }
}
