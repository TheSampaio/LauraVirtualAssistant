using System.Buffers.Binary;
using Laura.Core.Conversation;

namespace Laura.Core.Tests.Conversation;

/// <summary>Checks silence suppression and hesitation handling before any local ASR result can become a command.</summary>
public sealed class SpeechTurnDetectorTests
{
    [Fact]
    public void SilenceAndBriefClicksNeverCreateASpeechTurn()
    {
        var detector = new SpeechTurnDetector(); Assert.False(detector.Observe(new byte[32_000]));
        byte[] click = new byte[640]; BinaryPrimitives.WriteInt16LittleEndian(click, short.MaxValue);
        Assert.False(detector.Observe(click)); Assert.False(detector.Observe(new byte[64_000])); Assert.False(detector.HasSpeech);
    }
    [Fact]
    public void ShortHesitationKeepsTheTurnOpenUntilSustainedSilence()
    {
        var detector = new SpeechTurnDetector(1000); byte[] speech = new byte[6400];
        for (int index = 0; index < speech.Length; index += 2) { BinaryPrimitives.WriteInt16LittleEndian(speech.AsSpan(index), 2000); }
        Assert.False(detector.Observe(speech)); Assert.True(detector.HasSpeech);
        Assert.False(detector.Observe(new byte[16_000])); Assert.False(detector.Observe(speech));
        Assert.False(detector.Observe(new byte[16_000])); Assert.True(detector.Observe(new byte[16_000]));
    }
}
