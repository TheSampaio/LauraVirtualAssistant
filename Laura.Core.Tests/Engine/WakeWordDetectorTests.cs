using Laura.Core.Configuration;
using Laura.Core.Engine;
using Laura.Core.Text;

namespace Laura.Core.Tests.Engine;

/// <summary>
/// Testes da detecção da palavra de ativação e da separação do comando.
/// </summary>
public sealed class WakeWordDetectorTests
{
    private static readonly RecognitionOptions Options = new()
    {
        WakePhrases = ["ok laura", "hey laura"],
    };

    [Fact]
    public void TryDetect_FindsWakePhraseWithoutCommand()
    {
        bool detected = WakeWordDetector.TryDetect(
            TextNormalizer.Normalize("Ok Laura"),
            Options,
            out string command);

        Assert.True(detected);
        Assert.Equal(string.Empty, command);
    }

    [Fact]
    public void TryDetect_ExtractsInlineCommand()
    {
        bool detected = WakeWordDetector.TryDetect(
            TextNormalizer.Normalize("Ok Laura que horas são"),
            Options,
            out string command);

        Assert.True(detected);
        Assert.Equal("que horas sao", command);
    }

    [Fact]
    public void TryDetect_ReturnsFalseWithoutWakePhrase()
    {
        bool detected = WakeWordDetector.TryDetect(
            TextNormalizer.Normalize("que horas são"),
            Options,
            out string command);

        Assert.False(detected);
        Assert.Equal(string.Empty, command);
    }
}
