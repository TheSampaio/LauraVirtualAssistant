using System.Text.Json;
using Laura.Core.Configuration;

namespace Laura.Core.Conversation;

/// <summary>Creates documented Gemini Live messages without a dependency on UI or audio devices.</summary>
public static class GeminiLiveProtocol
{
    private static readonly string[] AudioModalities = ["AUDIO"];
    /// <summary>Builds the session setup, keeping credentials out of message content.</summary>
    public static string Setup(string model, string voice, string persona, string culture, object[] tools, bool pushToTalk, string? resumeHandle = null, int silenceDurationMs = 650)
    {
        string name = model.StartsWith("models/", StringComparison.Ordinal) ? model : "models/" + model;
        return JsonSerializer.Serialize(new
        {
            setup = new
            {
                model = name,
                generationConfig = new { responseModalities = AudioModalities, speechConfig = new { voiceConfig = new { prebuiltVoiceConfig = new { voiceName = voice } } } },
                systemInstruction = new { parts = new[] { new { text = persona + "\n" + LanguageInstruction(culture) +
                    (tools.Length == 0 ? "" : "\nYou are Laura, running on the user's Windows computer. Use the available tools to perform requested actions; do not claim you cannot open installed applications. Use open_app for ANY application name, including Steam and PCSX2. Only report success after a successful tool result. Ask for clarification when a target is ambiguous.") + "\nCurrent local time: " + DateTimeOffset.Now.ToString("O", System.Globalization.CultureInfo.InvariantCulture) } } },
                inputAudioTranscription = new { }, outputAudioTranscription = new { },
                realtimeInputConfig = new { automaticActivityDetection = new { disabled = pushToTalk,
                    silenceDurationMs = Math.Clamp(silenceDurationMs, 300, 2000), prefixPaddingMs = 150,
                    startOfSpeechSensitivity = "START_SENSITIVITY_HIGH", endOfSpeechSensitivity = "END_SENSITIVITY_HIGH" } },
                sessionResumption = resumeHandle is null ? (object)new { } : new { handle = resumeHandle },
                contextWindowCompression = new { slidingWindow = new { } }, tools,
            },
        });
    }

    /// <summary>Specifies regional pronunciation as well as vocabulary for native audio models.</summary>
    public static string LanguageInstruction(string culture) => culture == "pt-BR"
        ? "Always speak Brazilian Portuguese (pt-BR), with a natural Brazilian accent and Brazilian pronunciation. Use Brazilian vocabulary such as você, celular, arquivo and tela. Never use a European Portuguese accent or Portugal pronunciation. These language instructions apply to all replies and read-aloud text."
        : "Speak American English (en-US), with a natural American accent, unless the user explicitly requests a translation.";

    /// <summary>Attaches an explicitly captured image to the current live conversation.</summary>
    public static string Image(byte[] bytes, string mimeType) => JsonSerializer.Serialize(new
    { realtimeInput = new { video = new { mimeType, data = Convert.ToBase64String(bytes) } } });

    /// <summary>Builds a typed user turn and optionally restores bounded history after reconnect.</summary>
    public static string Text(ConversationTurn turn, bool includeHistory)
    {
        var turns = new List<object>();
        if (includeHistory)
        {
            turns.AddRange(turn.History.TakeLast(8).Select(message => (object)new
            {
                role = message.Source == ConversationMessageSource.Assistant ? "model" : "user",
                parts = new[] { new { text = message.Text } },
            }));
        }
        turns.Add(new { role = "user", parts = new[] { new { text = turn.Prompt } } });
        return JsonSerializer.Serialize(new { clientContent = new { turns, turnComplete = true } });
    }

    /// <summary>Builds a raw 16 kHz PCM microphone packet using the current audio field.</summary>
    public static string Audio(byte[] pcm) => JsonSerializer.Serialize(new
    { realtimeInput = new { audio = new { mimeType = "audio/pcm;rate=16000", data = Convert.ToBase64String(pcm) } } });
}
