using Laura.Core.Configuration;

namespace Laura.Core.Tests.Configuration;

/// <summary>
/// Testes do saneamento das configurações, a defesa contra valores fora de faixa.
/// </summary>
public sealed class SanitizationTests
{
    [Fact]
    public void VoiceProfile_ClampsProsodyAndVolume()
    {
        VoiceProfile sanitized = new VoiceProfile { Rate = 99, Pitch = -99, Volume = 500 }.Sanitized();

        Assert.Equal(VoiceProfile.MaximumProsody, sanitized.Rate);
        Assert.Equal(VoiceProfile.MinimumProsody, sanitized.Pitch);
        Assert.Equal(100, sanitized.Volume);
    }

    [Fact]
    public void VoiceProfile_NormalizesBlankVoiceNameToNull()
    {
        VoiceProfile sanitized = new VoiceProfile { VoiceName = "   " }.Sanitized();
        Assert.Null(sanitized.VoiceName);
    }

    [Fact]
    public void RecognitionOptions_RemovesDuplicateAndBlankPhrases()
    {
        RecognitionOptions sanitized = new RecognitionOptions
        {
            WakePhrases = ["Ok Laura", "ok laura", "  ", "Hey Laura"],
        }.Sanitized();

        Assert.Equal(2, sanitized.WakePhrases.Count);
    }

    [Fact]
    public void RecognitionOptions_RestoresDefaultsWhenAllPhrasesInvalid()
    {
        RecognitionOptions sanitized = new RecognitionOptions { WakePhrases = ["", "   "] }.Sanitized();
        Assert.NotEmpty(sanitized.WakePhrases);
    }

    [Fact]
    public void RecognitionOptions_ClampsConfidenceAndTimeout()
    {
        RecognitionOptions sanitized = new RecognitionOptions
        {
            MinimumConfidence = 5.0,
            CommandTimeout = TimeSpan.FromSeconds(120),
        }.Sanitized();

        Assert.Equal(1.0, sanitized.MinimumConfidence);
        Assert.Equal(30, sanitized.CommandTimeout.TotalSeconds);
    }

    [Fact]
    public void LauraSettings_FallsBackToDefaultCultureWhenInvalid()
    {
        LauraSettings sanitized = new LauraSettings { Culture = "não-existe" }.Sanitized();
        Assert.Equal(LauraSettings.Default.Culture, sanitized.Culture);
    }

    [Fact]
    public void LauraSettings_MirrorsCultureIntoRecognition()
    {
        LauraSettings sanitized = new LauraSettings { Culture = "en-US" }.Sanitized();
        Assert.Equal("en-US", sanitized.Recognition.Culture);
    }
}
