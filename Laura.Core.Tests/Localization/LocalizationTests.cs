using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Laura.Core.Configuration;
using Laura.Core.Localization;
using Microsoft.Extensions.Logging.Abstractions;

namespace Laura.Core.Tests.Localization;

public sealed class LocalizationTests
{
    private static string Locales => Path.Combine(AppContext.BaseDirectory, "Localization", "Locales");

    [Fact]
    public void PortugueseCatalogCoversAllEnglishKeysAndFormatArguments()
    {
        using var english = JsonDocument.Parse(File.ReadAllText(Path.Combine(Locales, "en-US.json")));
        using var portuguese = JsonDocument.Parse(File.ReadAllText(Path.Combine(Locales, "pt-BR.json")));
        foreach (JsonProperty entry in english.RootElement.EnumerateObject())
        {
            Assert.True(portuguese.RootElement.TryGetProperty(entry.Name, out JsonElement translation), entry.Name);
            string original = entry.Value.ValueKind == JsonValueKind.Array ? entry.Value[0].GetString()! : entry.Value.GetString()!;
            string translated = translation.ValueKind == JsonValueKind.Array ? translation[0].GetString()! : translation.GetString()!;
            Assert.Equal(Arguments(original), Arguments(translated));
        }
    }

    [Fact]
    public void UnsupportedLanguageFallsBackToEnglish()
    {
        var localizer = new JsonLocalizer(Locales, NullLogger<JsonLocalizer>.Instance);
        localizer.SetCulture(CultureInfo.GetCultureInfo("fr-FR"));
        Assert.Equal("en-US", localizer.Culture.Name);
        localizer.SetCulture(CultureInfo.GetCultureInfo("pt-BR"));
        Assert.Equal("Modelos", localizer.Get("ui.tab.models"));
        Assert.Contains("que horas são", localizer.GetPhrases("phrases.time"));
    }

    [Fact]
    public void InterfaceAndConversationCulturesRemainIndependent()
    {
        var settings = new LauraSettings { UiCulture = "pt-BR", Culture = "en-US" }.Sanitized();
        Assert.Equal("pt-BR", settings.ResolveUiCulture().Name);
        Assert.Equal("en-US", settings.ResolveCulture().Name);
        Assert.Equal("en-US", new LauraSettings { Culture = "bad-tag" }.Sanitized().Culture);
    }

    [Fact]
    public void VoicePreviewLookupDoesNotChangeInterfaceCulture()
    {
        var localizer = new JsonLocalizer(Locales, NullLogger<JsonLocalizer>.Instance);
        Assert.Equal("Modelos", localizer.GetForCulture("pt-BR", "ui.tab.models"));
        Assert.Equal("Models", localizer.GetForCulture("unknown", "ui.tab.models"));
        Assert.Equal("en-US", localizer.Culture.Name);
    }

    private static string[] Arguments(string text) => [.. Regex.Matches(text, @"\{(\d+)")
        .Select(static match => match.Groups[1].Value).Distinct().Order(StringComparer.Ordinal)];
}
