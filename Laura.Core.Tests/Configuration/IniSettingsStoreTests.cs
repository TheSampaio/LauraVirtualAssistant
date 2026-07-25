using Laura.Core.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Laura.Core.Tests.Configuration;

/// <summary>
/// Testes da persistência em INI: ida e volta dos valores e tolerância a lixo.
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
            GreetOnStartup = false,
            AnnounceHourly = true,
            StartWithWindows = true,
            Voice = new VoiceProfile { VoiceName = "Maria", Rate = 3, Pitch = -2, Volume = 80 },
            Recognition = new RecognitionOptions
            {
                Enabled = false,
                WakePhrases = ["ok laura", "ei laura"],
                MinimumConfidence = 0.75,
                CommandTimeout = TimeSpan.FromSeconds(12),
                AllowInlineCommand = false,
            },
            GenerativeAi = new GenerativeAiOptions
            {
                Enabled = true,
                Endpoint = "http://localhost:11434",
                Model = "llama3.2",
                Persona = "Seja breve.",
            },
        }.Sanitized();

        IniSettingsStore store = CreateStore();
        await store.SaveAsync(original);
        LauraSettings loaded = (await store.LoadAsync()).Sanitized();

        Assert.Equal(original.Culture, loaded.Culture);
        Assert.Equal(original.GreetOnStartup, loaded.GreetOnStartup);
        Assert.Equal(original.AnnounceHourly, loaded.AnnounceHourly);
        Assert.Equal(original.StartWithWindows, loaded.StartWithWindows);
        Assert.Equal(original.Voice, loaded.Voice);
        Assert.Equal(original.Recognition.Enabled, loaded.Recognition.Enabled);
        Assert.Equal(original.Recognition.WakePhrases, loaded.Recognition.WakePhrases);
        Assert.Equal(original.Recognition.MinimumConfidence, loaded.Recognition.MinimumConfidence);
        Assert.Equal(original.Recognition.CommandTimeout, loaded.Recognition.CommandTimeout);
        Assert.Equal(original.Recognition.AllowInlineCommand, loaded.Recognition.AllowInlineCommand);
        Assert.Equal(original.GenerativeAi, loaded.GenerativeAi);
    }

    [Fact]
    public async Task SaveAsync_WritesUtf8SoAccentsSurviveHandEditing()
    {
        LauraSettings settings = LauraSettings.Default with
        {
            GenerativeAi = GenerativeAiOptions.Default with { Persona = "Seja cordial e objetiva. Não use listas." },
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
            ; comentário solto
            [Voz]
            Velocidade = isto-nao-e-numero
            linha sem separador
            [Escuta]
            Ativada = talvez
            """);

        LauraSettings loaded = await CreateStore().LoadAsync();

        Assert.Equal(LauraSettings.Default.Voice.Rate, loaded.Voice.Rate);
        Assert.Equal(LauraSettings.Default.Recognition.Enabled, loaded.Recognition.Enabled);
    }

    [Fact]
    public void IniDocument_ReadsSectionsKeysAndLists()
    {
        IniDocument document = IniDocument.Parse("""
            [Escuta]
            FrasesDeAtivacao = ok laura | hey laura
            ConfiancaMinima = 0.7
            """);

        Assert.Equal(["ok laura", "hey laura"], document.GetList("Escuta", "FrasesDeAtivacao", []));
        Assert.Equal(0.7, document.GetDouble("Escuta", "ConfiancaMinima", 0.0));
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
