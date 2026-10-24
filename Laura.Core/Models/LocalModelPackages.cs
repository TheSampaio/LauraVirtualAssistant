namespace Laura.Core.Models;

/// <summary>One immutable optional download pinned to its upstream revision, size and observed SHA-256.</summary>
public sealed record LocalModelAsset(string Name, string FileName, string Url, long Bytes, string Sha256, string License);

/// <summary>Reviewed optional local-first starter pack; no asset is fetched by startup, restore or build.</summary>
public static class LocalModelPackages
{
    /// <summary>Gets the pinned Vulkan runtime, which also contains CPU backends for machines without a compatible GPU.</summary>
    public static LocalModelAsset Runtime { get; } = new("llama.cpp b11669 Vulkan", "llama-b11669-vulkan.zip",
        "https://github.com/ggml-org/llama.cpp/releases/download/b11669/llama-b11669-bin-win-vulkan-x64.zip", 32182350,
        "2c07574e7bde71321cb785282b20cc821e951132149eeceeea32ddebdbf0598e", "MIT; LLVM OpenMP notices included");
    /// <summary>Gets the exact evaluated text-only Qwen quantization; multimodal projector assets are intentionally separate.</summary>
    public static LocalModelAsset Qwen { get; } = new("Qwen3.5-4B Q4_K_M", "Qwen3.5-4B-Q4_K_M-e87f1764.gguf",
        "https://huggingface.co/unsloth/Qwen3.5-4B-GGUF/resolve/e87f176479d0855a907a41277aca2f8ee7a09523/Qwen3.5-4B-Q4_K_M.gguf", 2740937888,
        "00fe7986ff5f6b463e62455821146049db6f9313603938a70800d1fb69ef11a4", "Apache-2.0");
    /// <summary>Gets the multilingual 53-speaker Kokoro pack containing the reviewed voice IDs.</summary>
    public static LocalModelAsset Kokoro { get; } = new("Kokoro multilingual v1.0", "kokoro-multi-lang-v1_0.tar.bz2",
        "https://github.com/k2-fsa/sherpa-onnx/releases/download/tts-models/kokoro-multi-lang-v1_0.tar.bz2", 349906910,
        "c5f7e2d2caf082bc1d20fb70334a61d99d20b484500aad32e7cf84c128ea3298", "Apache-2.0 model; GPL-3.0 phonemizer data");
    /// <summary>Gets an opt-in female Brazilian Portuguese voice; attribution and non-commercial licensing apply.</summary>
    public static LocalModelAsset BrazilianVoice { get; } = new("Dii Brazilian Portuguese female", "vits-piper-pt_BR-dii-high.tar.bz2",
        "https://github.com/k2-fsa/sherpa-onnx/releases/download/tts-models/vits-piper-pt_BR-dii-high.tar.bz2", 67238016,
        "76bdf009bb29e69b12336de833d0d219ffa558f4281330eb141e4a0569c108a2", "CC BY-NC-ND 4.0; TigreGotico Lda attribution");
    /// <summary>Gets a female British English voice from the Piper catalogue.</summary>
    public static LocalModelAsset BritishVoice { get; } = new("Cori British English female", "vits-piper-en_GB-cori-medium.tar.bz2",
        "https://github.com/k2-fsa/sherpa-onnx/releases/download/tts-models/vits-piper-en_GB-cori-medium.tar.bz2", 67257412,
        "49c9a5361bbdd95d7ca9687c4de11e5908481f65e7c7c368960df79949fdac2b", "MIT model; public-domain recordings");
    /// <summary>Gets hash-pinned multilingual Whisper base artifacts from an immutable upstream conversion.</summary>
    public static IReadOnlyList<LocalModelAsset> Whisper { get; } =
    [
        new("Whisper base encoder", "base-encoder.int8.onnx", WhisperRoot + "base-encoder.int8.onnx", 29120534, "0b8fb1304b6109976038efff5ace81720e00386f3ff6b54ee8c75291ca0a1e11", "MIT"),
        new("Whisper base decoder", "base-decoder.int8.onnx", WhisperRoot + "base-decoder.int8.onnx", 130672026, "9759d217388a01b3a4c7c15533201067b48ae819c4daafc8624e64b9409dc02d", "MIT"),
        new("Whisper base vocabulary", "base-tokens.txt", WhisperRoot + "base-tokens.txt", 816730, "b34b360dbb493e781e479794586d661700670d65564001f23024971d1f2fa126", "MIT")
    ];
    /// <summary>Gets the multilingual small encoder and decoder for improved EN/PT recognition over the base fallback.</summary>
    public static IReadOnlyList<LocalModelAsset> WhisperSmall { get; } =
    [
        new("Whisper small encoder", "small-encoder.int8.onnx", WhisperSmallRoot + "small-encoder.int8.onnx", 112442483,
            "4cbe7b22fa9026b843b60a68640c747de05bafb1a11b57edc0e66c232d9f33a9", "MIT"),
        new("Whisper small decoder", "small-decoder.int8.onnx", WhisperSmallRoot + "small-decoder.int8.onnx", 262226114,
            "acad50b5c782696e91b55914cc5ab4f756f1532f76e22aa6fc615f39fb69a8ee", "MIT")
    ];
    private const string WhisperRoot = "https://huggingface.co/csukuangfj/sherpa-onnx-whisper-base/resolve/bb53ee204431c90d314c1cc08d28d23e5b7927cc/";
    private const string WhisperSmallRoot = "https://huggingface.co/csukuangfj/sherpa-onnx-whisper-small/resolve/8f3c18b358db4d1f2fc1eae49d75cd20989e4309/";
    /// <summary>Gets the exact compressed/download size shown before owner consent.</summary>
    public static long DownloadBytes => Runtime.Bytes + Qwen.Bytes + Kokoro.Bytes + BrazilianVoice.Bytes + BritishVoice.Bytes
        + Whisper.Sum(asset => asset.Bytes) + WhisperSmall.Sum(asset => asset.Bytes);
}
