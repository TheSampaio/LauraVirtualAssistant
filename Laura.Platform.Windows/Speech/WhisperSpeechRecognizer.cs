using Laura.Core.Abstractions;
using Laura.Core.Speech;

namespace Laura.Platform.Windows.Speech;

/// <summary>Requests multilingual Whisper transcription from the isolated local worker without opening a device or network.</summary>
public sealed class WhisperSpeechRecognizer(ISettingsService settings, LocalVoiceWorkerClient worker) : IVoiceInput
{
    /// <inheritdoc />
    public bool IsAvailable => worker.IsInstalled && (Find("small-encoder.int8.onnx") && Find("small-decoder.int8.onnx")
        || Find("base-encoder.int8.onnx") && Find("base-decoder.int8.onnx")) && Find("base-tokens.txt");
    /// <inheritdoc />
    public async Task<string> TranscribeAsync(byte[] pcm, string culture, CancellationToken token)
    {
        if (pcm.Length is < 3200 or > 960000 || (pcm.Length & 1) != 0) { return ""; }
        if (!IsAvailable) { throw new SpeechUnavailableException("Local Whisper assets are unavailable."); }
        string text = "";
        await worker.ExecuteAsync(new("recognize", settings.Current.LocalVoice.WhisperDirectory, culture, settings.Current.LocalVoice.Threads,
            Pcm: Convert.ToBase64String(pcm), DetectLanguage: settings.Current.LocalVoice.DetectLanguage),
            response => { if (response.Type == "transcript") { text = response.Data; } }, token).ConfigureAwait(false);
        return text;
    }
    /// <summary>Checks only known filename patterns inside the selected local installation.</summary>
    private bool Find(params string[] patterns) => Directory.Exists(settings.Current.LocalVoice.WhisperDirectory)
        && patterns.Any(pattern => Directory.GetFiles(settings.Current.LocalVoice.WhisperDirectory, pattern).Length == 1);
}
