namespace Laura.Core.Configuration;

/// <summary>Local Kokoro synthesis and multilingual Whisper assets, independent of conversation provider and cloud consent.</summary>
public sealed record LocalVoiceOptions
{
    /// <summary>Gets the installed Kokoro multilingual asset directory.</summary>
    public string KokoroDirectory { get; init; } = LocalModelPaths.Models;
    /// <summary>Gets the installed multilingual Whisper ONNX directory.</summary>
    public string WhisperDirectory { get; init; } = LocalModelPaths.Models;
    /// <summary>Gets non-model phonemizer resources stored outside the flat model directory.</summary>
    public string KokoroDataDirectory { get; init; } = "";
    /// <summary>Gets the explicit feminine Brazilian voice identity.</summary>
    public string PortugueseVoice { get; init; } = "pt_dii";
    /// <summary>Gets the explicit feminine American voice identity.</summary>
    public string EnglishVoice { get; init; } = "af_heart";
    /// <summary>Gets bounded CPU synthesis/recognition concurrency within the single local voice lane.</summary>
    public int Threads { get; init; } = 2;
    /// <summary>Gets whether Whisper detects language instead of using the manually configured conversation language.</summary>
    public bool DetectLanguage { get; init; } = true;
    /// <summary>Clamps resources and supported voice identities while preserving optional missing asset paths.</summary>
    public LocalVoiceOptions Sanitized() => this with
    {
        KokoroDirectory = KokoroDirectory?.Trim() ?? "", WhisperDirectory = WhisperDirectory?.Trim() ?? "", KokoroDataDirectory = KokoroDataDirectory?.Trim() ?? "",
        PortugueseVoice = PortugueseVoice is "pf_dora" or "pt_dii" ? PortugueseVoice : "pt_dii",
        EnglishVoice = EnglishVoice is "af_heart" or "af_bella" or "bf_alice" or "bf_emma" or "bf_isabella" or "bf_lily" or "gb_cori" ? EnglishVoice : "af_heart", Threads = Math.Clamp(Threads, 1, 8)
    };
}
