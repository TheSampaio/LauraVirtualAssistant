using Laura.Core.Configuration;
using Laura.Core.Conversation;

namespace Laura.Core.Tests.Conversation;

/// <summary>Checks local wake admission and transient detection without opening or transmitting microphone audio.</summary>
public sealed class ClapWakePolicyTests
{
    [Theory]
    [InlineData("local", true, true, false)]
    [InlineData("local", true, false, false)]
    [InlineData("local", false, true, false)]
    [InlineData("gemini", true, false, true)]
    [InlineData("gemini", false, false, false)]
    [InlineData("openai", true, true, false)]
    [InlineData("unavailable", true, true, false)]
    public void WakeUsesOnlyTheSelectedReadyVoiceProvider(string provider, bool available, bool speechInstalled, bool expected)
    {
        var settings = LauraSettings.Default with { ClapWakeEnabled = true, MicrophoneMuted = false };
        Assert.Equal(expected, ClapWakePolicy.CanArm(settings, new(provider, available, speechInstalled, true, false, false)));
    }

    [Fact]
    public void OptOutMuteBusyAndExplicitHandoffEachKeepDetectionClosed()
    {
        var enabled = LauraSettings.Default with { ClapWakeEnabled = true, MicrophoneMuted = false };
        var ready = new ClapWakeState("gemini", true, true, true, false, false);
        Assert.True(ClapWakePolicy.CanArm(enabled, ready));
        Assert.False(ClapWakePolicy.CanArm(enabled with { ClapWakeEnabled = false }, ready));
        Assert.False(ClapWakePolicy.CanArm(enabled with { MicrophoneMuted = true }, ready));
        Assert.False(ClapWakePolicy.CanArm(enabled, ready with { EngineReady = false }));
        Assert.False(ClapWakePolicy.CanArm(enabled, ready with { ConversationBusy = true }));
        Assert.False(ClapWakePolicy.CanArm(enabled, ready with { Suspended = true }));
    }

}
