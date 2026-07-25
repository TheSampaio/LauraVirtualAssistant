using Laura.Core.Text;

namespace Laura.Core.Tests.Text;

/// <summary>
/// Tests text normalization, the basis for all command matching.
/// </summary>
public sealed class TextNormalizerTests
{
    [Theory]
    [InlineData("Ok, Laura!", "ok laura")]
    [InlineData("WHAT TIME IS IT?", "what time is it")]
    [InlineData("  Hello   World  ", "hello world")]
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
        Assert.True(TextNormalizer.ContainsPhrase("ok laura what time is it", "laura"));
        Assert.False(TextNormalizer.ContainsPhrase("lauraceas sao plantas", "laura"));
    }

    [Fact]
    public void RemovePhrase_StripsFirstOccurrenceAndTrims() =>
        Assert.Equal("what time is it", TextNormalizer.RemovePhrase("ok laura what time is it", "ok laura"));

    [Fact]
    public void RemovePhrase_ReturnsOriginalWhenPhraseAbsent() =>
        Assert.Equal("bom dia", TextNormalizer.RemovePhrase("bom dia", "ok laura"));
}
