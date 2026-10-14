using Laura.Core.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Laura.Core.Tests.Configuration;

/// <summary>
/// Tests INI persistence: value round-tripping and tolerance for junk.
/// </summary>
public sealed class IniSettingsStoreTests : IDisposable
{
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
                Endpoint = "http://localhost:11434",
                Model = "gemini-3.1-flash-live-preview",
                Persona = "Be brief.",
            },
        }.Sanitized();

        IniSettingsStore store = CreateStore();
        await store.SaveAsync(original);
        LauraSettings loaded = (await store.LoadAsync()).Sanitized();

        Assert.Equal(original.Culture, loaded.Culture);
        Assert.Equal(original.GenerativeAi.LiveVoice, loaded.GenerativeAi.LiveVoice);
        Assert.Equal(original.GenerativeAi.ContinuousListening, loaded.GenerativeAi.ContinuousListening);
        Assert.Equal(original.GenerativeAi.Provider, loaded.GenerativeAi.Provider);
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
        Assert.Equal(original.GenerativeAi, loaded.GenerativeAi);
    }

    [Fact]
    public async Task BrainOptionsRoundTripOutsideTheVaultAndBoundInvalidRankingValues()
    {
        var options = new Laura.Core.Brain.BrainOptions { Directory = "C:/Synthetic/brain", EmbeddingModel = "C:/Synthetic/model.onnx", EmbeddingVocabulary = "C:/Synthetic/vocab.txt", AutoLearn = false,
            ContextCharacters = 2200, GraphLimit = 200, RetrievalGraphDepth = 3, RetrievalGraphCandidates = 80, RetrievalGraphMilliseconds = 45,
            LexicalWeight = .6, SemanticWeight = .2, GraphWeight = .1, TemporalWeight = .1, TrustedSkills = ["fictional-id:fictional-hash"] };
        IniSettingsStore store = CreateStore(); await store.SaveAsync(LauraSettings.Default with { Brain = options });
        var loaded = (await store.LoadAsync()).Brain;
        Assert.Equal(options.Directory, loaded.Directory); Assert.Equal(options.EmbeddingModel, loaded.EmbeddingModel); Assert.Equal(options.EmbeddingVocabulary, loaded.EmbeddingVocabulary);
        Assert.False(loaded.AutoLearn); Assert.Equal(2200, loaded.ContextCharacters); Assert.Equal(200, loaded.GraphLimit); Assert.Equal(options.TrustedSkills, loaded.TrustedSkills);
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
