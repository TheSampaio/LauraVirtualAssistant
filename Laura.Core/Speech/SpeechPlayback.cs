namespace Laura.Core.Speech;

/// <summary>Names observable synthesis and playback stages without equating generated text with sound.</summary>
public enum SpeechPlaybackState
{
    /// <summary>Awaiting the speech executor.</summary>
    Queued,
    /// <summary>The voice provider is producing audio.</summary>
    Synthesizing,
    /// <summary>At least one PCM fragment was received.</summary>
    AudioReady,
    /// <summary>PCM has entered the Windows output queue.</summary>
    Playing,
    /// <summary>The device position passed the final queued PCM frame.</summary>
    Completed,
    /// <summary>Synthesis or playback failed.</summary>
    Failed,
    /// <summary>The owner or operation lifetime canceled playback.</summary>
    Canceled,
}

/// <summary>Content-free evidence for a response's audio pipeline; device completion does not prove acoustic audibility.</summary>
public sealed record SpeechPlayback(Guid ResponseId, Guid PlaybackId, SpeechPlaybackState State, long ReceivedBytes = 0,
    long QueuedBytes = 0, long DevicePositionBytes = 0, int OutputDevice = -1, double? FirstAudioMilliseconds = null,
    long Underruns = 0, string? Failure = null);

/// <summary>Signals an unavailable voice without invalidating an already produced text response.</summary>
public sealed class SpeechUnavailableException(string reason) : InvalidOperationException(reason);

/// <summary>Signals a failed synthesis or output component without invalidating a completed text inference or task checkpoint.</summary>
public sealed class SpeechPlaybackException(string reason, Exception? innerException = null) : InvalidOperationException(reason, innerException);
