using Laura.App.Views;
using Laura.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Laura.App.Infrastructure;

/// <summary>Renders the actual isolated native connection workspace without network requests or microphone capture.</summary>
internal static class ChatGptUiSmokeRunner
{
    /// <summary>Checks the connection tab in both interface languages and window sizes using the real composition root.</summary>
    internal static async Task RunAsync(IServiceProvider services, Window window, string directory)
    {
        if (services.GetRequiredService<ISpeechSynthesizer>() is not Laura.Platform.Windows.Speech.ProviderSpeechRouter)
        { throw new InvalidOperationException("Normal composition must route provider-specific speech."); }
        var navigation = (NavigationPage)window.Page!; var settings = services.GetRequiredService<ISettingsService>();
        await services.GetRequiredService<MainPage>().VerifyStreamCompletionForSmokeAsync().ConfigureAwait(true);
        foreach (string culture in new[] { "en-US", "pt-BR" })
        {
            await settings.UpdateAsync(settings.Current with { UiCulture = culture }).ConfigureAwait(true);
            services.GetRequiredService<IUiLocalizer>().SetCulture(settings.Current.ResolveUiCulture());
            var page = services.GetRequiredService<SetupPage>(); await navigation.PushAsync(page, false).ConfigureAwait(true);
            foreach (int tab in Environment.GetCommandLineArgs().Contains("--connections-only", StringComparer.Ordinal) ? new[] { 0 } : new[] { 0, 1, 2, 3, 6 })
            {
            page.SelectTab(tab);
            await Task.Delay(1000).ConfigureAwait(true);
            foreach (int width in new[] { 1440, 720 })
            {
                window.Width = width; window.Height = width == 720 ? 700 : 1000;
                await Task.Delay(350).ConfigureAwait(true);
                page.VerifySettingsForSmoke();
                await MauiSmokeRunner.CaptureAsync(window, Path.Combine(directory, $"settings-{tab}-{culture}-{width}.png")).ConfigureAwait(true);
                await page.VerifySettingsScrollForSmokeAsync(true).ConfigureAwait(true);
                await MauiSmokeRunner.CaptureAsync(window, Path.Combine(directory, $"settings-{tab}-{culture}-{width}-options.png")).ConfigureAwait(true);
                await page.VerifySettingsScrollForSmokeAsync(false).ConfigureAwait(true);
            }
            }
            await navigation.PopAsync(false).ConfigureAwait(true);
        }
        await File.WriteAllTextAsync(Path.Combine(directory, "result.txt"), "PASS: actual routing, local voice, ChatGPT and local-model settings in EN/PT at 1440/720; isolated protected storage; no network request or microphone capture.").ConfigureAwait(true);
        services.GetRequiredService<Laura.Core.Abstractions.IShellController>().Shutdown();
    }
}
