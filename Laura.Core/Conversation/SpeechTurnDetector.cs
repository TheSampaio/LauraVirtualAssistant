namespace Laura.Core.Conversation;

/// <summary>Bounds local capture and suppresses silence-only recognition without interpreting microphone audio as authorization.</summary>
public sealed class SpeechTurnDetector(int silenceMilliseconds = 1000)
{
    private readonly int _silenceLimit = Math.Clamp(silenceMilliseconds, 300, 3000);
    private double _voicedMilliseconds, _silentMilliseconds;
    /// <summary>Gets whether enough non-silent audio was observed to attempt local recognition.</summary>
    public bool HasSpeech => _voicedMilliseconds >= 160;
    /// <summary>Gets whether sustained silence followed actual speech.</summary>
    public bool Completed => HasSpeech && _silentMilliseconds >= _silenceLimit;

    /// <summary>Observes one 16 kHz mono PCM16 packet. Args: pcm: Explicitly captured audio. Returns: Whether speech end was detected.</summary>
    public bool Observe(ReadOnlySpan<byte> pcm)
    {
        double milliseconds = pcm.Length / 32d;
        if (AudioLevel.Measure(pcm) >= .04f) { _voicedMilliseconds += milliseconds; _silentMilliseconds = 0; }
        else if (HasSpeech) { _silentMilliseconds += milliseconds; }
        return Completed;
    }
}
