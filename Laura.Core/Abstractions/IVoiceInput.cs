namespace Laura.Core.Abstractions;

/// <summary>Transcribes bounded local PCM without owning a microphone, cloud transport or conversation provider.</summary>
public interface IVoiceInput
{
    /// <summary>Gets whether the reviewed local multilingual recognition assets are available.</summary>
    bool IsAvailable { get; }
    /// <summary>Transcribes mono signed 16-bit 16 kHz PCM. Args: pcm: Local audio; culture: Manual language; token: Cancellation. Returns: Observed transcript.</summary>
    Task<string> TranscribeAsync(byte[] pcm, string culture, CancellationToken token);
}
