using Laura.Core.Skills;
using Laura.Core.Text;

namespace Laura.Core.Tests.Skills;

/// <summary>
/// Testes do casamento de frases e prefixos usado pelas habilidades.
/// </summary>
public sealed class PhraseMatcherTests
{
    private static readonly string[] SearchPrefixes = ["pesquise por", "pesquise", "procure por"];

    [Fact]
    public void MatchesAny_DetectsAnyListedPhrase()
    {
        string text = TextNormalizer.Normalize("bloquear o computador");
        Assert.True(PhraseMatcher.MatchesAny(text, ["trancar", "bloquear o computador"]));
    }

    [Fact]
    public void TryMatchPrefix_PrefersLongestPrefix()
    {
        string text = TextNormalizer.Normalize("pesquise por gatos fofos");

        bool matched = PhraseMatcher.TryMatchPrefix(text, SearchPrefixes, out string argument);

        Assert.True(matched);
        Assert.Equal("gatos fofos", argument);
    }

    [Fact]
    public void TryMatchPrefix_ReturnsFalseWhenNoPrefixPresent()
    {
        string text = TextNormalizer.Normalize("que horas sao");

        bool matched = PhraseMatcher.TryMatchPrefix(text, SearchPrefixes, out string argument);

        Assert.False(matched);
        Assert.Equal(string.Empty, argument);
    }
}
