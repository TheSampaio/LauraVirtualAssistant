namespace Laura.Core.Models;

/// <summary>Reviewed, immutable model downloads. No remote executable manifests are accepted.</summary>
public static class SupportedModels
{
    /// <summary>Gets the reviewed multilingual Whisper base model (English and Portuguese).</summary>
    public static ModelArtifact Speech { get; } = new("whisper-base", "Whisper Base (multilingual)",
        new Uri("https://huggingface.co/ggerganov/whisper.cpp/resolve/5359861c739e955e79d9a303bcbc70fb988958b1/ggml-base.bin"),
        147951465, "60ed5bc3dd14eea856493d334349b405782ddcaf0028d4b5df4088345fba2efe",
        "F16", 0, 2L * 1024 * 1024 * 1024, "MIT", new Uri("https://github.com/openai/whisper/blob/main/LICENSE"))
        { IsSpeechModel = true };
    /// <summary>Gets models with explicit distribution terms and verified publisher metadata.</summary>
    public static IReadOnlyList<ModelArtifact> All { get; } =
    [
        new("qwen2.5-0.5b-instruct-q4_k_m", "Qwen 2.5 0.5B Instruct",
            new Uri("https://huggingface.co/Qwen/Qwen2.5-0.5B-Instruct-GGUF/resolve/9217f5db79a29953eb74d5343926648285ec7e67/qwen2.5-0.5b-instruct-q4_k_m.gguf"),
            491400032, "74a4da8c9fdbcd15bd1f6d01d621410d31c6fc00986f5eb687824e7b93d7a9db",
            "Q4_K_M", 32768, 4L * 1024 * 1024 * 1024, "Apache-2.0",
            new Uri("https://huggingface.co/Qwen/Qwen2.5-0.5B-Instruct/blob/main/LICENSE")),
        Speech,
    ];
}
