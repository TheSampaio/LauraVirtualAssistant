using System.Buffers.Binary;
using System.Globalization;
using Laura.Core.Configuration;
using Laura.Core.Conversation;
using Laura.Core.Localization;
using Microsoft.Extensions.Logging.Abstractions;

namespace Laura.Core.Tests.Conversation;

/// <summary>Exercises clap false positives, privacy defaults, localized standby and leading PCM preservation.</summary>
public sealed class WakeInteractionTests
{
    [Fact]
    public void TwoIsolatedClapsWakeOnceAndIgnoreImmediateEcho()
    {
        var detector = new DoubleClapDetector();
        Assert.False(detector.Process(Clap(), 100)); Assert.False(detector.Process(Silence(), 120));
        Assert.False(detector.Process(Clap(), 400)); Assert.True(detector.Process(Silence(), 420));
        Assert.False(detector.Process(Clap(), 600)); Assert.False(detector.Process(Silence(), 620));
    }
    [Theory]
    [InlineData(120)]
    [InlineData(1200)]
    public void ClapsOutsideThePairWindowDoNotWake(int distance)
    {
        var detector = new DoubleClapDetector();
        detector.Process(Clap(), 100); detector.Process(Silence(), 120);
        Assert.False(detector.Process(Clap(), 100 + distance)); Assert.False(detector.Process(Silence(), 120 + distance));
    }
    [Fact]
    public void SustainedSpeechAndLowBackgroundNoiseDoNotWake()
    {
        var detector = new DoubleClapDetector();
        byte[] tone = new byte[640];
        for (int sample = 0; sample < 320; sample++) { BinaryPrimitives.WriteInt16LittleEndian(tone.AsSpan(sample * 2), (short)(24000 * Math.Sin(sample * Math.PI / 8))); }
        for (int time = 0; time < 2000; time += 20) { Assert.False(detector.Process(tone, time)); }
        for (int time = 2000; time < 2500; time += 20) { Assert.False(detector.Process(Silence(), time)); }
    }
    [Fact]
    public void SensitivitySupportsQuieterClapsWithoutTreatingSilenceAsAClap()
    {
        var sensitive = new DoubleClapDetector(100); var conservative = new DoubleClapDetector(0);
        foreach (var detector in new[] { sensitive, conservative }) { detector.Process(Clap(10000), 100); detector.Process(Silence(), 120); detector.Process(Clap(10000), 400); }
        Assert.True(sensitive.Process(Silence(), 420)); Assert.False(conservative.Process(Silence(), 420));
    }
    [Theory]
    [InlineData("pt-BR", "LAURA, isso é tudo!", true)]
    [InlineData("pt-BR", "descançar", true)]
    [InlineData("pt-BR", "pode descansar", true)]
    [InlineData("pt-BR", "não pare de ouvir", false)]
    [InlineData("pt-BR", "isso é tudo que eu preciso para o projeto", false)]
    [InlineData("en-US", "That's all.", true)]
    [InlineData("en-US", "stop listening please", true)]
    [InlineData("en-US", "don't stop listening", false)]
    public void StandbyMatchesWholeLocalizedCommands(string culture, string text, bool expected)
    {
        var localizer = new JsonLocalizer(Path.Combine(AppContext.BaseDirectory, "Localization", "Locales"), NullLogger<JsonLocalizer>.Instance);
        localizer.SetCulture(CultureInfo.GetCultureInfo(culture));
        Assert.Equal(expected, new ListeningControl(localizer).IsRestRequest(text));
    }
    [Theory]
    [InlineData("pt-BR")]
    [InlineData("en-US")]
    public void ClapAcknowledgmentsAreShortLocalizedAndHaveAnOwnerVariant(string culture)
    {
        var localizer = new JsonLocalizer(Path.Combine(AppContext.BaseDirectory, "Localization", "Locales"), NullLogger<JsonLocalizer>.Instance); localizer.SetCulture(CultureInfo.GetCultureInfo(culture));
        var greetings = localizer.GetPhrases(LocalizationKeys.Listening.WakeGreeting);
        Assert.True(greetings.Count >= 3); Assert.Contains(greetings, phrase => phrase.Contains("{0}", StringComparison.Ordinal));
        Assert.All(greetings, phrase => Assert.True(phrase.Length < 60));
        Assert.True(new ListeningControl(localizer).RestConfirmation.Length > 10);
    }
    [Fact]
    public void WakeCaptureIsOptInAndSensitivityIsBounded()
    {
        Assert.False(LauraSettings.Default.ClapWakeEnabled);
        Assert.True(LauraSettings.Default.MicrophoneMuted);
        Assert.Equal(100, (LauraSettings.Default with { ClapWakeSensitivity = 1000 }).Sanitized().ClapWakeSensitivity);
        Assert.Equal(0, (LauraSettings.Default with { ClapWakeSensitivity = -10 }).Sanitized().ClapWakeSensitivity);
    }
    [Fact]
    public void PrimingDoesNotConsumeTheFirstNetworkFragment()
    {
        var buffer = new PcmPlaybackBuffer(); byte[] first = [1, 2, 3, 4];
        buffer.BeginTurn(); buffer.Add(first, 100);
        byte[] output = new byte[8];
        Assert.Equal(0, buffer.Read(output, 120)); Assert.Equal(4, buffer.BufferedBytes);
        buffer.Add([5, 6, 7, 8], 150); buffer.CompleteTurn();
        Assert.Equal(8, buffer.Read(output, 151)); Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, output);
    }
    [Fact]
    public void AShortResponseEventuallyPlaysEvenWithoutACompletionSignal()
    {
        var buffer = new PcmPlaybackBuffer(); buffer.Add([1, 2], 0);
        byte[] output = new byte[4]; Assert.Equal(0, buffer.Read(output, 249)); Assert.Equal(2, buffer.Read(output, 250));
        Assert.Equal(new byte[] { 1, 2, 0, 0 }, output);
    }
    [Fact]
    public void ConsecutiveTurnsPreserveQueuedAudioAndOnlyExplicitInterruptionDropsIt()
    {
        var buffer = new PcmPlaybackBuffer(); buffer.Add([1, 2], 0); buffer.BeginTurn(); buffer.Add([3, 4], 10); buffer.CompleteTurn();
        byte[] output = new byte[4]; Assert.Equal(4, buffer.Read(output, 20)); Assert.Equal(new byte[] { 1, 2, 3, 4 }, output);
        buffer.Add([5, 6], 100); buffer.Clear(); Assert.Equal(0, buffer.BufferedBytes); Assert.Equal(0, buffer.Read(output, 400));
    }
    [Fact]
    public void AMinuteLongBriefingDoesNotOverflowOrDiscardSpeech()
    {
        var buffer = new PcmPlaybackBuffer(); byte[] speech = new byte[48000 * 60]; speech[0] = 17; speech[^1] = 93;
        buffer.Add(speech, 0); buffer.CompleteTurn(); byte[] output = new byte[speech.Length];
        Assert.Equal(speech.Length, buffer.Read(output, 10)); Assert.Equal(speech, output);
    }
    /// <summary>Builds an isolated impulse with a clap-like high crest factor.</summary>
    private static byte[] Clap(short amplitude = 28000)
    { byte[] pcm = Silence(); for (int index = 80; index < 92; index++) { BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(index * 2), index % 2 == 0 ? amplitude : (short)-amplitude); } return pcm; }
    /// <summary>Builds one twenty-millisecond mono frame without opening a microphone.</summary>
    private static byte[] Silence() => new byte[640];
}
