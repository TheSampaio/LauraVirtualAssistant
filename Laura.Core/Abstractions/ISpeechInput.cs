using Laura.Core.Speech;

namespace Laura.Core.Abstractions;

/// <summary>Offline push-to-talk. No microphone opens until capture is explicitly requested.</summary>
public interface ISpeechInput
{
    /// <summary>Gets available offline recognition cultures.</summary>
    IReadOnlyList<string> Languages { get; }
    /// <summary>Lists microphone devices.</summary>
    IReadOnlyList<AudioDevice> InputDevices { get; }
    /// <summary>Lists playback devices.</summary>
    IReadOnlyList<AudioDevice> OutputDevices { get; }
    /// <summary>Captures at most 15 seconds, stopping on release, then transcribes locally.</summary>
    Task<string?> RecognizeAsync(string culture, int device, CancellationToken stopRecording, CancellationToken cancellationToken);
}
