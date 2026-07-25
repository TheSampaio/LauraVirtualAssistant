using Laura.Core.Text;

namespace Laura.Core.Tests.Text;

/// <summary>
/// Testes da normalização de texto, base de todo o casamento de comandos.
/// </summary>
public sealed class TextNormalizerTests
{
    [Theory]
    [InlineData("Ok, Laura!", "ok laura")]
    [InlineData("QUE HORAS SÃO?", "que horas sao")]
    [InlineData("  Olá   Mundo  ", "ola mundo")]
    [InlineData("Pesquise por gatos.", "pesquise por gatos")]
    public void Normalize_RemovesAccentsCaseAndPunctuation(string input, string expected) =>
        Assert.Equal(expected, TextNormalizer.Normalize(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!!")]
    public void Normalize_ReturnsEmptyForBlankInput(string? input) =>
        Assert.Equal(string.Empty, TextNormalizer.Normalize(input));

    [Fact]
    public void ContainsPhrase_MatchesWholeWordsOnly()
    {
        Assert.True(TextNormalizer.ContainsPhrase("ok laura que horas sao", "laura"));
        Assert.False(TextNormalizer.ContainsPhrase("lauraceas sao plantas", "laura"));
    }

    [Fact]
    public void RemovePhrase_StripsFirstOccurrenceAndTrims() =>
        Assert.Equal("que horas sao", TextNormalizer.RemovePhrase("ok laura que horas sao", "ok laura"));

    [Fact]
    public void RemovePhrase_ReturnsOriginalWhenPhraseAbsent() =>
        Assert.Equal("bom dia", TextNormalizer.RemovePhrase("bom dia", "ok laura"));
}
