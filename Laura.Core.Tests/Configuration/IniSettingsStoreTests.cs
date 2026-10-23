using Laura.Core.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Laura.Core.Tests.Configuration;

/// <summary>
/// Tests INI persistence: value round-tripping and tolerance for junk.
/// </summary>
public sealed class IniSettingsStoreTests : IDisposable
{
    /// <summary>Local integrity/resource options and independent voice paths survive BOM UTF-8 INI without changing cloud consent.</summary>
    [Fact]
    public async Task LocalRuntimeAndVoiceRoundTripIndependently()
    {
        var model = new LocalModelOptions { Enabled = true, Executable = "D:/Synthetic/llama-server.exe", ModelPath = "D:/Synthetic/Qwen.gguf", ExecutableHash = new string('A', 64), ModelHash = new string('B', 64), ContextTokens = 12288, Threads = 3, IdleMinutes = 2, MaximumDifficulty = Laura.Core.Models.TaskDifficulty.Standard };
        var voice = new LocalVoiceOptions { KokoroDirectory = "D:/Synthetic/Models", KokoroDataDirectory = "D:/Synthetic/Runtime/espeak-ng-data", WhisperDirectory = "D:/Synthetic/Models", EnglishVoice = "af_bella", DetectLanguage = true };
        var store = CreateStore(); await store.SaveAsync(LauraSettings.Default with { LocalModel = model, LocalVoice = voice, VulkanInterface = true }); var loaded = await store.LoadAsync();
        Assert.True(loaded.VulkanInterface); Assert.Equal(-1, loaded.LocalModel.GpuLayers);
        Assert.Equal(model, loaded.LocalModel); Assert.Equal(voice, loaded.LocalVoice); Assert.False(loaded.GenerativeAi.Enabled); Assert.False(loaded.ChatGpt.Enabled);
        Assert.Equal(new byte[] { 0xef, 0xbb, 0xbf }, (await File.ReadAllBytesAsync(_filePath)).Take(3));
    }
    /// <summary>The shipped legacy persona migrates while owner-edited wording stays intact.</summary>
    [Fact]
    public void PersonaMigrationPreservesOwnerEdits()
    {
        const string legacy = "You are Laura, a discreet personal assistant: calm, capable, precise, " +
            "and quietly confident. Speak as if replying aloud, never as a chatbot. Keep answers " +
            "to one short sentence by default, two only when truly needed. Do not use lists, " +
            "markdown, emojis, long explanations, disclaimers, or filler. If the user asks for " +
            "an action you cannot perform, say so briefly and offer the closest useful next step. " +
            "Sound composed and practical, with subtle warmth, never overly enthusiastic.";
        Assert.Equal(GenerativeAiOptions.Default.Persona, (new GenerativeAiOptions { Persona = legacy }).Sanitized().Persona);
        Assert.Equal("Owner's wording", (new GenerativeAiOptions { Persona = "Owner's wording" }).Sanitized().Persona);
    }
    /// <summary>Retired providers migrate without granting cloud consent or changing the selected Brain.</summary>
    [Theory]
    [InlineData("local")]
    [InlineData("openai")]
    [InlineData("auto")]
    public async Task RetiredProvidersCannotBeEnabledAfterMigration(string provider)
    {
        await File.WriteAllTextAsync(_filePath, "[GenerativeAI]\nProvider=" + provider + "\nEnabled=true\nLocalModel=retired\n[Brain]\nDirectory=C:/Synthetic/Brain\n");
        var store = CreateStore();
        LauraSettings migrated = await store.LoadAsync();
        Assert.Equal("gemini", migrated.GenerativeAi.Provider);
        Assert.False(migrated.GenerativeAi.Enabled);
        Assert.Equal("C:/Synthetic/Brain", migrated.Brain.Directory);
        await store.SaveAsync(migrated);
        Assert.DoesNotContain("LocalModel=retired", await File.ReadAllTextAsync(_filePath));
        Assert.Equal(migrated.GenerativeAi, (await store.LoadAsync()).GenerativeAi);
    }

    /// <summary>The explicit full-access choice round-trips while diagnostics remain intact.</summary>
    [Fact]
    public async Task FullAccessChoiceAndDiagnosticLevelRoundTrip()
    {
        var value = LauraSettings.Default with { Permissions = new() { Mode = (Laura.Core.Permissions.ApprovalMode)2 }, DiagnosticLevel = Microsoft.Extensions.Logging.LogLevel.Debug };
        var store = CreateStore(); await store.SaveAsync(value);
        var loaded = await store.LoadAsync();
        Assert.Equal(Laura.Core.Permissions.ApprovalMode.FullAccess, loaded.Permissions.Mode);
        Assert.Equal(value.DiagnosticLevel, loaded.DiagnosticLevel);
        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Information, (value with { DiagnosticLevel = (Microsoft.Extensions.Logging.LogLevel)999 }).Sanitized().DiagnosticLevel);
    }
    private readonly string _filePath = Path.Combine(
        Path.GetTempPath(),
        $"laura-test-{Guid.NewGuid():N}.ini");

    private IniSettingsStore CreateStore() => new(_filePath, NullLogger<IniSettingsStore>.Instance);

    [Fact]
    public async Task SaveAsync_ThenLoadAsync_PreservesEveryValue()
    {
        LauraSettings original = new LauraSettings
        {
            Culture = "pt-BR",
            UiCulture = "en-US",
            RememberConversations = false,
            MicrophoneMuted = false,
            ClapWakeEnabled = true,
            ClapWakeSensitivity = 67,
            InputDevice = 2,
            OutputDevice = 1,
            GreetOnStartup = false,
            AnnounceHourly = true,
            StartWithWindows = true,
            Voice = new VoiceProfile { VoiceName = "Maria", Rate = 3, Pitch = -2, Volume = 80 },
            GenerativeAi = new GenerativeAiOptions
            {
                Enabled = true,
                Provider = "gemini",
                LiveVoice = "Kore",
                ContinuousListening = true,
                Model = "gemini-3.1-flash-live-preview",
                Persona = "Be brief.",
            },
        }.Sanitized();

        IniSettingsStore store = CreateStore();
        await store.SaveAsync(original);
        LauraSettings loaded = (await store.LoadAsync()).Sanitized();

        Assert.Equal(original.Culture, loaded.Culture);
        Assert.True(loaded.GenerativeAi.Enabled);
        Assert.Equal(original.GenerativeAi.Model, loaded.GenerativeAi.Model);
        Assert.Equal(original.GenerativeAi.ContinuousListening, loaded.GenerativeAi.ContinuousListening);
        Assert.Equal(original.UiCulture, loaded.UiCulture);
        Assert.Equal(original.RememberConversations, loaded.RememberConversations);
        Assert.Equal(original.MicrophoneMuted, loaded.MicrophoneMuted);
        Assert.Equal(original.ClapWakeEnabled, loaded.ClapWakeEnabled);
        Assert.Equal(original.ClapWakeSensitivity, loaded.ClapWakeSensitivity);
        Assert.Equal(original.InputDevice, loaded.InputDevice);
        Assert.Equal(original.OutputDevice, loaded.OutputDevice);
        Assert.Equal(original.GreetOnStartup, loaded.GreetOnStartup);
        Assert.Equal(original.AnnounceHourly, loaded.AnnounceHourly);
        Assert.Equal(original.StartWithWindows, loaded.StartWithWindows);
        Assert.Equal(original.Voice, loaded.Voice);
        Assert.Equal(original.GenerativeAi.Persona, loaded.GenerativeAi.Persona);
        string serialized = await File.ReadAllTextAsync(_filePath);
        Assert.Contains("gemini-3.1", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LiveVoice", serialized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BrainOptionsRoundTripOutsideTheVaultAndBoundInvalidRankingValues()
    {
        var options = new Laura.Core.Brain.BrainOptions { Directory = "C:/Synthetic/brain", EmbeddingModel = "C:/Synthetic/model.onnx", EmbeddingVocabulary = "C:/Synthetic/vocab.txt", AutoLearn = false, MaintenanceEnabled = true,
            ContextCharacters = 2200, GraphLimit = 200, RetrievalGraphDepth = 3, RetrievalGraphCandidates = 80, RetrievalGraphMilliseconds = 45,
            LexicalWeight = .6, SemanticWeight = .2, GraphWeight = .1, TemporalWeight = .1, TrustedSkills = ["fictional-id:fictional-hash"] };
        IniSettingsStore store = CreateStore(); await store.SaveAsync(LauraSettings.Default with { Brain = options });
        var loaded = (await store.LoadAsync()).Brain;
        Assert.Equal(options.Directory, loaded.Directory); Assert.Equal(options.EmbeddingModel, loaded.EmbeddingModel); Assert.Equal(options.EmbeddingVocabulary, loaded.EmbeddingVocabulary);
        Assert.False(loaded.AutoLearn); Assert.Equal(2200, loaded.ContextCharacters); Assert.Equal(200, loaded.GraphLimit); Assert.Equal(options.TrustedSkills, loaded.TrustedSkills);
        Assert.True(loaded.MaintenanceEnabled);
        Assert.Equal(3, loaded.RetrievalGraphDepth); Assert.Equal(80, loaded.RetrievalGraphCandidates); Assert.Equal(45, loaded.RetrievalGraphMilliseconds);
        Assert.Equal(.6, loaded.LexicalWeight); Assert.Equal(.2, loaded.SemanticWeight); Assert.Equal(.1, loaded.GraphWeight); Assert.Equal(.1, loaded.TemporalWeight);
        var bounded = options with { ContextCharacters = -1, GraphLimit = int.MaxValue, SemanticWeight = double.NaN };
        Assert.Equal(500, bounded.Sanitized().ContextCharacters); Assert.Equal(1000, bounded.Sanitized().GraphLimit); Assert.Equal(.3, bounded.Sanitized().SemanticWeight);
    }

    [Fact]
    public async Task SaveAsync_WritesUtf8SoAccentsSurviveHandEditing()
    {
        LauraSettings settings = LauraSettings.Default with
        {
            GenerativeAi = GenerativeAiOptions.Default with { Persona = "Be warm and concise. Do not use lists." },
        };

        IniSettingsStore store = CreateStore();
        await store.SaveAsync(settings);

        byte[] bytes = await File.ReadAllBytesAsync(_filePath);
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);

        LauraSettings loaded = await store.LoadAsync();
        Assert.Equal(settings.GenerativeAi.Persona, loaded.GenerativeAi.Persona);
    }

    [Fact]
    public async Task LoadAsync_ReturnsDefaultsWhenFileMissing()
    {
        LauraSettings loaded = await CreateStore().LoadAsync();
        Assert.Equal(LauraSettings.Default.Culture, loaded.Culture);
    }

    [Fact]
    public async Task LoadAsync_KeepsDefaultsForCorruptedEntries()
    {
        await File.WriteAllTextAsync(
            _filePath,
            """
            ; loose comment
            [Voice]
            Speed = this-is-not-a-number
            line without separator
            [General]
            StartWithWindows = maybe
            """);

        LauraSettings loaded = await CreateStore().LoadAsync();

        Assert.Equal(LauraSettings.Default.Voice.Rate, loaded.Voice.Rate);
        Assert.Equal(LauraSettings.Default.StartWithWindows, loaded.StartWithWindows);
    }

    [Fact]
    public void IniDocument_ReadsSectionsKeysAndLists()
    {
        IniDocument document = IniDocument.Parse("""
            [General]
            Items = one | two
            Ratio = 0.7
            """);

        Assert.Equal(["one", "two"], document.GetList("General", "Items", []));
        Assert.Equal(0.7, document.GetDouble("General", "Ratio", 0.0));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (File.Exists(_filePath))
        {
            File.Delete(_filePath);
        }
    }
}
