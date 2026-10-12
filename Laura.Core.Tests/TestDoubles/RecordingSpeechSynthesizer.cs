using Laura.Core.Abstractions;
using Laura.Core.Speech;

namespace Laura.Core.Tests.TestDoubles;

/// <summary>Records completed speech requests without opening a device or network connection.</summary>
public sealed class RecordingSpeechSynthesizer : ISpeechSynthesizer
{
    /// <summary>Gets text accepted for speech.</summary>
    public List<string> SpokenTexts { get; } = [];
    /// <inheritdoc />
    public bool IsSpeaking => false;
    /// <inheritdoc />
    public IReadOnlyList<VoiceDescriptor> GetAvailableVoices() => [];
    /// <inheritdoc />
    public Task SpeakAsync(SpeechRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); SpokenTexts.Add(request.Text); return Task.CompletedTask;
    }
    /// <inheritdoc />
    public void CancelSpeech() { }
    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
