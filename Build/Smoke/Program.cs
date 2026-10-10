using System.Globalization;
using Laura.App.Composition;
using Laura.App.Views;
using Laura.App.Controls;
using Laura.Core.Abstractions;
using Laura.Core.Configuration;
using Laura.Core.Conversation;
using Laura.Platform.Windows.System;
using Laura.Platform.Windows.Speech;
using Laura.Core.Models;
using NAudio.Wave;
using Windows.Media.SpeechSynthesis;
using Microsoft.Extensions.DependencyInjection;

/// <summary>Opt-in real native inference and UI smoke. Uses isolated ignored storage; never captures microphone audio.</summary>
internal static class Program
{
    private static void SaveView(SettingsForm form, string directory, string name)
    {
        Application.DoEvents();
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
        bitmap.Save(Path.Combine(directory, name));
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (Control descendant in Descendants(child)) { yield return descendant; }
        }
    }

    private static void RenderSettings(SettingsForm form, string directory, string culture)
    {
        int index = 0;
        foreach (NavButton tab in Descendants(form).OfType<NavButton>().ToArray())
        {
            tab.PerformClick();
            SaveView(form, directory, $"settings-{index++}-{culture}.png");
            if (!tab.Selected) { throw new InvalidOperationException("HUD settings navigation failed: " + tab.Caption); }
        }
        var home = Descendants(form).OfType<Button>().First(button => button.Text == "LAURA");
        home.PerformClick();
        form.ClientSize = new Size(1100, 720);
        SaveView(form, directory, $"hud-small-{culture}.png");
        form.ShowModelSetup();
        SaveView(form, directory, $"models-small-{culture}.png");
        form.ClientSize = new Size(1280, 800);
        home.PerformClick();
        Console.WriteLine("HUD navigation and render passed: " + culture);
    }

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 0) { Console.Error.WriteLine("Usage: Laura.Smoke <isolated-output-directory> [--download] [--native] [--speech] [--require-local-crt]"); return 2; }
        string directory = Path.GetFullPath(args[0]);
        Directory.CreateDirectory(directory);
        NativeRuntimeBootstrap.Initialize();
        ApplicationConfiguration.Initialize();
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        ServiceProvider provider = new ServiceCollection().AddLauraServices(directory).BuildServiceProvider();
        try
        {
            var settings = provider.GetRequiredService<ISettingsService>();
            settings.LoadAsync(timeout.Token).GetAwaiter().GetResult();
            settings.UpdateAsync(settings.Current with { UiCulture = "en-US", Culture = "en-US" }, timeout.Token).GetAwaiter().GetResult();
            provider.GetRequiredService<IUiLocalizer>().SetCulture(CultureInfo.GetCultureInfo("en-US"));
            var models = provider.GetRequiredService<ILocalModelManager>();
            var speech = provider.GetRequiredService<ISpeechInput>();
            Console.WriteLine("Hardware: " + WindowsHardwareProbe.Detect(Path.Combine(directory, "Models")));
            Console.WriteLine("Offline recognition: " + string.Join(", ", speech.Languages));
            Console.WriteLine($"Audio: {speech.InputDevices.Count} input / {speech.OutputDevices.Count} output devices");
            SettingsForm form = provider.GetRequiredService<SettingsForm>();
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-32000, -32000);
            form.ShowInTaskbar = false;
            form.Opacity = 0;
            form.Show();
            Application.DoEvents();
            SaveView(form, directory, "hud-en-US.png");
            form.ShowModelSetup();
            using (var bitmap = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                bitmap.Save(Path.Combine(directory, "models-en-US.png"));
            }
            RenderSettings(form, directory, "en-US");
            form.Hide();
            if (args.Contains("--download", StringComparer.Ordinal))
            {
                models.InstallAsync(models.Catalog[0], null, timeout.Token).GetAwaiter().GetResult();
                Console.WriteLine("Verified model installed.");
            }
            if (args.Contains("--native", StringComparer.Ordinal))
            {
                settings.UpdateAsync(settings.Current with { GreetOnStartup = false, AnnounceHourly = false,
                    GenerativeAi = settings.Current.GenerativeAi with { Enabled = true, Model = models.Catalog[0].Id } }, timeout.Token).GetAwaiter().GetResult();
                var inference = provider.GetRequiredService<ILocalInferenceEngine>();
                int tokens = 0;
                inference.TokenReceived += (_, _) => tokens++;
                string? answer = inference.AskAsync(new ConversationTurn("Say hello in one short sentence.", CultureInfo.GetCultureInfo("en-US"), "Tester"), timeout.Token).GetAwaiter().GetResult();
                if (string.IsNullOrWhiteSpace(answer) || tokens == 0) { throw new InvalidOperationException("Native inference returned no streamed answer: " + inference.Status); }
                Console.WriteLine($"Native response ({tokens} chunks): {answer}");
                string? portuguese = inference.AskAsync(new ConversationTurn("Diga olá em uma frase curta.", CultureInfo.GetCultureInfo("pt-BR"), "Tester"), timeout.Token).GetAwaiter().GetResult();
                if (string.IsNullOrWhiteSpace(portuguese)) { throw new InvalidOperationException("Portuguese inference returned no response."); }
                Console.WriteLine("Portuguese native response: " + portuguese);
                EventHandler<string> cancelAfterFirstChunk = (_, _) => inference.Cancel();
                inference.TokenReceived += cancelAfterFirstChunk;
                try
                {
                    try
                    {
                        _ = inference.AskAsync(new ConversationTurn("Write a long story about the ocean.", CultureInfo.GetCultureInfo("en-US"), "Tester"), timeout.Token).GetAwaiter().GetResult();
                        throw new InvalidOperationException("Native cancellation did not interrupt generation.");
                    }
                    catch (OperationCanceledException) { Console.WriteLine("Native cancellation passed."); }
                }
                finally { inference.TokenReceived -= cancelAfterFirstChunk; }
                string? recovery = inference.AskAsync(new ConversationTurn("Say OK.", CultureInfo.GetCultureInfo("en-US"), "Tester"), timeout.Token).GetAwaiter().GetResult();
                if (string.IsNullOrWhiteSpace(recovery)) { throw new InvalidOperationException("Native generation did not recover after cancellation."); }
                Console.WriteLine("Native cancellation recovery: " + recovery);
                inference.UnloadAsync(timeout.Token).GetAwaiter().GetResult();
                if (inference.LoadedModel is not null) { throw new InvalidOperationException("Native weights were not unloaded."); }
                Console.WriteLine("Native lifecycle passed.");
            }
            if (args.Contains("--speech", StringComparer.Ordinal))
            {
                models.InstallAsync(SupportedModels.Speech, null, timeout.Token).GetAwaiter().GetResult();
                using var synthesizer = new SpeechSynthesizer();
                Console.WriteLine("Installed TTS languages: " + string.Join(", ", SpeechSynthesizer.AllVoices.Select(static voice => voice.Language).Distinct()));
                using SpeechSynthesisStream synthesis = synthesizer.SynthesizeTextToStreamAsync("The quick brown fox jumps over the lazy dog.").AsTask().GetAwaiter().GetResult();
                using Stream input = synthesis.AsStreamForRead();
                using var reader = new WaveFileReader(input);
                using var resampler = new MediaFoundationResampler(reader, new WaveFormat(16000, 16, 1));
                using var wav = new MemoryStream();
                WaveFileWriter.WriteWavFileToStream(wav, resampler);
                string recognized = ((WindowsSpeechInput)speech).TranscribeAsync(wav.ToArray(), "en-US", timeout.Token).GetAwaiter().GetResult();
                if (!recognized.Contains("fox", StringComparison.OrdinalIgnoreCase)) { throw new InvalidOperationException("Offline speech smoke failed: " + recognized); }
                Console.WriteLine("Offline speech response: " + recognized);
                synthesizer.Voice = SpeechSynthesizer.AllVoices.First(static voice => voice.Language == "pt-BR");
                using SpeechSynthesisStream portugueseSynthesis = synthesizer.SynthesizeTextToStreamAsync("Bom dia. A Laura trabalha no meu computador.").AsTask().GetAwaiter().GetResult();
                using Stream portugueseInput = portugueseSynthesis.AsStreamForRead();
                using var portugueseReader = new WaveFileReader(portugueseInput);
                using var portugueseResampler = new MediaFoundationResampler(portugueseReader, new WaveFormat(16000, 16, 1));
                using var portugueseWav = new MemoryStream();
                WaveFileWriter.WriteWavFileToStream(portugueseWav, portugueseResampler);
                string portugueseRecognition = ((WindowsSpeechInput)speech).TranscribeAsync(portugueseWav.ToArray(), "pt-BR", timeout.Token).GetAwaiter().GetResult();
                if (!portugueseRecognition.Contains("computador", StringComparison.OrdinalIgnoreCase)) { throw new InvalidOperationException("Portuguese speech smoke failed: " + portugueseRecognition); }
                Console.WriteLine("Portuguese offline speech response: " + portugueseRecognition);
                Console.WriteLine("Speech smoke passed; no microphone audio was captured.");
            }
            settings.UpdateAsync(settings.Current with { UiCulture = "pt-BR", Culture = "en-US" }, timeout.Token).GetAwaiter().GetResult();
            provider.GetRequiredService<IUiLocalizer>().SetCulture(CultureInfo.GetCultureInfo("pt-BR"));
            form.ShowFromTray();
            SaveView(form, directory, "hud-pt-BR.png");
            form.ShowModelSetup();
            form.Show();
            Application.DoEvents();
            using (var bitmap = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                bitmap.Save(Path.Combine(directory, "models-pt-BR.png"));
            }
            RenderSettings(form, directory, "pt-BR");
            form.Hide();
            settings.LoadAsync(timeout.Token).GetAwaiter().GetResult();
            if (settings.Current.UiCulture != "pt-BR" || settings.Current.Culture != "en-US") { throw new InvalidOperationException("Independent language persistence failed."); }
            if (args.Contains("--require-local-crt", StringComparer.Ordinal))
            {
                using var process = System.Diagnostics.Process.GetCurrentProcess();
                foreach (string name in new[] { "msvcp140.dll", "vcruntime140.dll", "vcruntime140_1.dll", "vcomp140.dll" })
                {
                    System.Diagnostics.ProcessModule module = process.Modules.Cast<System.Diagnostics.ProcessModule>().First(module => module.ModuleName.Equals(name, StringComparison.OrdinalIgnoreCase));
                    if (!module.FileName.StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase)) { throw new InvalidOperationException("System-wide CRT was used: " + module.FileName); }
                    Console.WriteLine("App-local runtime: " + module.FileName);
                }
            }
            Console.WriteLine("Smoke passed.");
            return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
        finally { provider.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
    }
}
