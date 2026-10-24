namespace Laura.Core.Speech;

/// <summary>Private stdin request to the bundled local voice worker; content never appears in process arguments or diagnostic logs.</summary>
public sealed record VoiceWorkerRequest(string Operation, string Directory, string Culture, int Threads, string Text = "", string Voice = "", float Speed = 1, string Pcm = "", bool DetectLanguage = false, string AssetsDirectory = "");
/// <summary>One bounded private worker response containing PCM, a transcript, completion or a safe error category.</summary>
public sealed record VoiceWorkerEvent(string Type, string Data = "", int SampleRate = 0);
