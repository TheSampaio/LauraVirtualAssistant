using Laura.Core.Conversation;

namespace Laura.Core.Tests.Conversation;

public sealed class AudioStabilityTests
{
    [Theory]
    [InlineData("audio/pcm;rate=24000", true)]
    [InlineData("audio/pcm;rate=24000;channels=1", true)]
    [InlineData("audio/pcm;rate=16000", false)]
    [InlineData("audio/pcm;rate=24000;channels=2", false)]
    [InlineData("audio/mp3", false)]
    public void ProviderFormatMustMatchNativePcm(string mime, bool expected) => Assert.Equal(expected, PcmAudioFormat.IsNativeOutput(mime));
    [Fact]
    public void DelayedHalfSampleIsNeverSentToHardwareWithZeroPaddingBetweenItsBytes()
    {
        var buffer = new PcmPlaybackBuffer(); buffer.BeginTurn(); buffer.Add([1], 0);
        byte[] output = new byte[6]; Assert.Equal(0, buffer.Read(output, 550));
        buffer.Add([2, 3, 4], 551); buffer.CompleteTurn();
        Assert.Equal(4, buffer.Read(output, 552)); Assert.Equal(new byte[] { 1, 2, 3, 4, 0, 0 }, output);
        Assert.Equal(0, buffer.Metrics.MisalignedReads);
    }
    [Fact]
    public void RealGapReprimesWithoutLosingSamplesAndCompletedSilenceIsNotAnUnderrun()
    {
        var buffer = new PcmPlaybackBuffer(); buffer.BeginTurn(); buffer.Add(new byte[6000], 0);
        byte[] output = new byte[6000]; Assert.Equal(6000, buffer.Read(output, 1));
        Assert.Equal(0, buffer.Read(output, 2)); Assert.Equal(1, buffer.Metrics.Underruns);
        buffer.Add([1, 2, 3, 4], 100); Assert.Equal(0, buffer.Read(output, 101)); Assert.Equal(4, buffer.BufferedBytes);
        buffer.CompleteTurn(); Assert.Equal(4, buffer.Read(output, 102)); Assert.Equal(0, buffer.Read(output, 103));
        Assert.Equal(1, buffer.Metrics.Underruns);
    }
    [Fact]
    public void TruncatedPcmIsReportedAndInterruptionCanDiscardItExplicitly()
    { var buffer = new PcmPlaybackBuffer(); buffer.Add([1], 0); Assert.Throws<InvalidOperationException>(buffer.CompleteTurn); buffer.Clear(); buffer.CompleteTurn(); }
    [Fact]
    public void LongFragmentedAudioPreservesExactSamplesAndQueueBounds()
    {
        byte[] pcm = Enumerable.Range(0, 48000 * 90).Select(value => (byte)(value % 251)).ToArray();
        var buffer = new PcmPlaybackBuffer(); buffer.BeginTurn();
        using var capture = new PcmAudioDiagnosticCapture(); capture.Receive(pcm);
        for (int offset = 0; offset < pcm.Length; offset += 961) { buffer.Add(pcm.AsSpan(offset, Math.Min(961, pcm.Length - offset)), offset / 48); }
        buffer.CompleteTurn(); byte[] output = new byte[2880];
        while (buffer.BufferedBytes > 0) { int read = buffer.Read(output, 90000); capture.Play(output[..read]); }
        Assert.True(capture.Compare().Identical); Assert.Equal(pcm.Length, buffer.Metrics.ReadBytes); Assert.Equal(0, buffer.Metrics.MisalignedReads);
        Assert.Throws<InvalidOperationException>(() => buffer.Add(new byte[48000 * 121], 90001));
    }
}
