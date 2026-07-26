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
            Culture = "en-US",
            GreetOnStartup = false,
            AnnounceHourly = true,
            StartWithWindows = true,
            Voice = new VoiceProfile { VoiceName = "Maria", Rate = 3, Pitch = -2, Volume = 80 },
            GenerativeAi = new GenerativeAiOptions
            {
                Enabled = true,
                Endpoint = "http://localhost:11434",
                Model = "llama3.2",
                Persona = "Be brief.",
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
        Assert.Equal(original.GenerativeAi, loaded.GenerativeAi);
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
