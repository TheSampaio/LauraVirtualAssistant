using Laura.Core.Text;

namespace Laura.Core.Tests.Text;

/// <summary>
/// Tests text normalization, the basis for all command matching.
/// </summary>
public sealed class TextNormalizerTests
{
    [Theory]
    [InlineData("Hello, Laura!", "hello laura")]
    [InlineData("WHAT TIME IS IT?", "what time is it")]
    [InlineData("  Hello   World  ", "hello world")]
    [InlineData("Search for cats.", "search for cats")]
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
        Assert.True(TextNormalizer.ContainsPhrase("hello laura what time is it", "laura"));
        Assert.False(TextNormalizer.ContainsPhrase("lauraceous plants", "laura"));
    }

    [Fact]
    public void RemovePhrase_StripsFirstOccurrenceAndTrims() =>
        Assert.Equal("what time is it", TextNormalizer.RemovePhrase("hello laura what time is it", "hello laura"));

    [Fact]
    public void RemovePhrase_ReturnsOriginalWhenPhraseAbsent() =>
        Assert.Equal("good morning", TextNormalizer.RemovePhrase("good morning", "hello laura"));
}
