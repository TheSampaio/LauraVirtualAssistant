using Laura.App.Views;
using Laura.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Laura.App.Infrastructure;

/// <summary>Opt-in UI render verification that uses isolated storage and never opens a microphone.</summary>
public static class MauiSmokeRunner
{
    /// <summary>Captures the native MAUI window across languages, pages and sizes, then records completion.</summary>
    public static async Task RunAsync(IServiceProvider services, Window window, string directory)
    {
        Directory.CreateDirectory(directory);
        try
        {
            await Task.Delay(1000).ConfigureAwait(true);
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
            foreach (var mode in new[] { Controls.ReactorMode.Listening, Controls.ReactorMode.Speaking })
            { main.PreviewReactorForSmoke(mode); await Task.Delay(180).ConfigureAwait(true); await CaptureAsync(window, Path.Combine(directory, "reactor-" + mode + ".png")).ConfigureAwait(true); }
            main.PreviewReactorForSmoke(null);
            var fullscreenWindow = (Microsoft.UI.Xaml.Window)window.Handler!.PlatformView!;
            fullscreenWindow.AppWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen);
            await Task.Delay(200).ConfigureAwait(true); await CaptureAsync(window, Path.Combine(directory, "hud-fullscreen.png")).ConfigureAwait(true);
            fullscreenWindow.AppWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.Overlapped);
            foreach (string culture in new[] { "en-US", "pt-BR" })
            {
                ISettingsService settings = services.GetRequiredService<ISettingsService>();
                await settings.UpdateAsync(settings.Current with { Culture = culture, UiCulture = culture }).ConfigureAwait(true);
                services.GetRequiredService<IUiLocalizer>().SetCulture(settings.Current.ResolveUiCulture());
                foreach (Type type in new[] { typeof(SetupPage), typeof(MemoryPage) })
                {
                    await navigation.PushAsync((Page)services.GetRequiredService(type), false).ConfigureAwait(true);
                    await Task.Delay(250).ConfigureAwait(true);
                    await CaptureAsync(window, Path.Combine(directory, type.Name + "-" + culture + ".png")).ConfigureAwait(true);
                    if (navigation.CurrentPage is SetupPage setup)
                    {
                        if (culture == "en-US" && Environment.GetCommandLineArgs().Contains("--key-ui-smoke", StringComparer.Ordinal))
                        {
                            await setup.VerifyCredentialInputForSmokeAsync().ConfigureAwait(true);
                            await CaptureAsync(window, Path.Combine(directory, "setup-key-masked.png")).ConfigureAwait(true);
                            await File.WriteAllTextAsync(Path.Combine(directory, "key-ui.txt"), "Valid key verified and masked; saving again preserved it; an edit during validation kept the verified credential and new draft; invalid replacement was rejected without changing the saved credential.").ConfigureAwait(true);
                        }
                        for (int tab = 1; tab < 4; tab++) { setup.SelectTab(tab); await Task.Delay(150).ConfigureAwait(true); await CaptureAsync(window, Path.Combine(directory, "setup-tab-" + tab + "-" + culture + ".png")).ConfigureAwait(true); } setup.SelectTab(0);
                    }
                    if (type == typeof(SetupPage) && culture == "en-US")
                    {
                        var native = (Microsoft.UI.Xaml.Window)window.Handler!.PlatformView!;
                        string[] layout = Describe(native.Content, native.Content).ToArray();
                        await File.WriteAllLinesAsync(Path.Combine(directory, "native-layout.txt"), layout).ConfigureAwait(true);
                    }
                    await navigation.PopAsync(false).ConfigureAwait(true);
                }
            }
            window.Width = 900; window.Height = 720;
            await Task.Delay(350).ConfigureAwait(true);
            await CaptureAsync(window, Path.Combine(directory, "hud-compact.png")).ConfigureAwait(true);
            await File.WriteAllTextAsync(Path.Combine(directory, "result.txt"), "MAUI HUD, navigation, EN/PT pages and compact window rendered successfully.").ConfigureAwait(true);
        }
        catch (Exception exception) { await File.WriteAllTextAsync(Path.Combine(directory, "result.txt"), exception.ToString()).ConfigureAwait(true); }
        finally { services.GetRequiredService<Laura.Core.Abstractions.IShellController>().Shutdown(); }
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
            yield return $"{frame.GetType().Name} {frame.Name} x={position.X:F0} y={position.Y:F0} w={frame.ActualWidth:F0} h={frame.ActualHeight:F0} color={color}";
        }
        for (int index = 0; index < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(element); index++)
        { foreach (string row in Describe(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(element, index), root)) { yield return row; } }
    }
    /// <summary>Captures the application's native WinUI content rather than the user's desktop.</summary>
    private static async Task CaptureAsync(Window window, string path)
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
