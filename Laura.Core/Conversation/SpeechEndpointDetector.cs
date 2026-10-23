namespace Laura.Core.Conversation;

/// <summary>Detects a bounded speech turn from local PCM energy without retaining audio or transmitting silence.</summary>
public sealed class SpeechEndpointDetector(int silenceMilliseconds = 650)
{
    private int _voiced, _silent, _elapsed;
    /// <summary>Gets whether enough speech energy was observed to permit recognition.</summary>
    public bool HasSpeech => _voiced >= 160;
    /// <summary>Accepts one local audio block. Args: level: Normalized RMS energy; milliseconds: Actual block duration. Returns: Whether automatic capture should end.</summary>
    public bool Observe(float level, int milliseconds)
    {
        _elapsed += milliseconds;
        if (level >= .035f) { _voiced += milliseconds; _silent = 0; } else { _silent += milliseconds; }
        return HasSpeech && _silent >= Math.Clamp(silenceMilliseconds, 300, 2000) || _elapsed >= 30000;
    }
}
