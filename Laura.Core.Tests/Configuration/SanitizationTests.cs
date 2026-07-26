using Laura.Core.Configuration;

namespace Laura.Core.Tests.Configuration;

/// <summary>
/// Tests settings sanitization, the defense against out-of-range values.
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
    public void LauraSettings_ForcesEnglishCulture()
    {
        LauraSettings sanitized = new LauraSettings { Culture = "pt-BR" }.Sanitized();
        Assert.Equal(LauraSettings.Default.Culture, sanitized.Culture);
    }
}
