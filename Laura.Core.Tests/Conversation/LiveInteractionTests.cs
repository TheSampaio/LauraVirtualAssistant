using System.Text.Json;
using Laura.Core.Conversation;
using Laura.Core.Skills;

namespace Laura.Core.Tests.Conversation;

/// <summary>Verifies speech-end configuration, regional instructions and keyboard lifecycle edge cases.</summary>
public sealed class LiveInteractionTests
{
    [Fact]
    public void AutomaticListening_UsesSilenceDetectionAndBrazilianAccent()
    {
        using var setup = JsonDocument.Parse(GeminiLiveProtocol.Setup("test", "Charon", "Laura", "pt-BR", [], false, silenceDurationMs: 550));
        var detection = setup.RootElement.GetProperty("setup").GetProperty("realtimeInputConfig").GetProperty("automaticActivityDetection");
        Assert.False(detection.GetProperty("disabled").GetBoolean());
        Assert.Equal(550, detection.GetProperty("silenceDurationMs").GetInt32());
        Assert.Contains("Brazilian accent", setup.RootElement.GetProperty("setup").GetProperty("systemInstruction").GetProperty("parts")[0].GetProperty("text").GetString());
        Assert.Contains("European Portuguese", GeminiLiveProtocol.LanguageInstruction("pt-BR"));
    }
    [Fact]
    public void HoldToTalk_RequiresDebounceAndEndsWhenEitherKeyIsReleased()
    {
        var state = new ShortcutStateTracker();
        Assert.Null(state.Update(100, true, false, false, false, false, false).PushToTalk);
        Assert.Null(state.Update(140, true, false, false, false, false, false).PushToTalk);
        Assert.True(state.Update(165, true, false, false, false, false, false).PushToTalk);
        Assert.Null(state.Update(200, true, false, false, false, false, false).PushToTalk);
        Assert.False(state.Update(220, false, false, false, false, false, false).PushToTalk);
    }
    [Fact]
    public void MuteShortcut_IsIgnoredWhileTypingAndDoesNotRepeatWhileHeld()
    {
        var state = new ShortcutStateTracker();
        Assert.False(state.Update(0, false, true, true, false, true, false).ToggleMute);
        state.Update(20, false, true, false, false, false, false);
        Assert.True(state.Update(40, false, true, false, false, true, false).ToggleMute);
        Assert.False(state.Update(60, false, true, false, false, true, false).ToggleMute);
    }
    [Fact]
    public void LocalShortcuts_RequireLauraForegroundButHoldToTalkIsGlobal()
    {
        var state = new ShortcutStateTracker();
        var elsewhere = state.Update(0, false, false, false, true, true, true);
        Assert.False(elsewhere.Interrupt); Assert.False(elsewhere.ToggleMute); Assert.False(elsewhere.ToggleFullscreen);
        state.Update(20, false, true, false, false, false, false);
        var local = state.Update(40, false, true, false, true, true, true);
        Assert.True(local.Interrupt); Assert.True(local.ToggleMute); Assert.True(local.ToggleFullscreen);
    }
    [Theory]
    [InlineData("Steam", "meu Steam", 100)]
    [InlineData("SteamVR", "Steam", 50)]
    [InlineData("PCSX2", "meu emulador de PS2", 100)]
    [InlineData("PCSX2-Qt", "PCSX2", 85)]
    [InlineData("Discord", "disc", 50)]
    [InlineData("Calendar", "calc", 0)]
    public void AppMatching_ResolvesSpeechAliasesWithoutShortNameCollisions(string candidate, string query, int expected)
    { Assert.Equal(expected, ApplicationNameMatcher.Score(candidate, query)); }
    [Fact]
    public void AudioLevel_DistinguishesSilenceAndSignedPcm()
    {
        Assert.Equal(0, AudioLevel.Measure([0, 0, 0, 0]));
        Assert.Equal(1, AudioLevel.Measure([0, 128]));
        Assert.Equal(0, AudioLevel.Measure([255]));
        Assert.InRange(AudioLevel.Measure([0, 4]), .1f, .2f);
    }
}
