namespace Laura.Core.Configuration;

/// <summary>Explicit app-managed llama.cpp configuration; paths never cause downloads or execution until local inference is enabled.</summary>
public sealed record LocalModelOptions
{
    /// <summary>Gets owner consent to run the selected local model.</summary>
    public bool Enabled { get; init; }
    /// <summary>Gets the reviewed llama-server executable.</summary>
    public string Executable { get; init; } = Path.Combine(LocalModelPaths.Runtime, "llama-b11669-vulkan", "llama-server.exe");
    /// <summary>Gets the local GGUF weights path.</summary>
    public string ModelPath { get; init; } = Path.Combine(LocalModelPaths.Models, Models.LocalModelPackages.Qwen.FileName);
    /// <summary>Gets the verified executable SHA-256 recorded during installation or explicit import.</summary>
    public string ExecutableHash { get; init; } = "";
    /// <summary>Gets the verified weights SHA-256 recorded during installation or explicit import.</summary>
    public string ModelHash { get; init; } = "";
    /// <summary>Gets the bounded local context size.</summary>
    public int ContextTokens { get; init; } = 8192;
    /// <summary>Gets CPU threads retained for local inference.</summary>
    public int Threads { get; init; } = Math.Clamp(Environment.ProcessorCount / 2, 1, 8);
    /// <summary>Gets GPU offload layers; minus one selects hardware automatically, zero is an explicit CPU override.</summary>
    public int GpuLayers { get; init; } = -1;
    /// <summary>Gets idle time before the supervised model process releases its memory.</summary>
    public int IdleMinutes { get; init; } = 5;
    /// <summary>Gets the maximum automatic difficulty supported by the reviewed installation.</summary>
    public Models.TaskDifficulty MaximumDifficulty { get; init; } = Models.TaskDifficulty.Light;
    /// <summary>Normalizes paths, integrity metadata and resource budgets without enabling inference.</summary>
    public LocalModelOptions Sanitized() => this with
    {
        Executable = Executable?.Trim() ?? "", ModelPath = ModelPath?.Trim() ?? "",
        ExecutableHash = Hash(ExecutableHash), ModelHash = Hash(ModelHash), ContextTokens = Math.Clamp(ContextTokens, 2048, 32768),
        Threads = Math.Clamp(Threads, 1, Math.Clamp(Environment.ProcessorCount - 1, 1, 8)), GpuLayers = Math.Clamp(GpuLayers, -1, 128),
        IdleMinutes = Math.Clamp(IdleMinutes, 1, 60), MaximumDifficulty = MaximumDifficulty is Models.TaskDifficulty.Light or Models.TaskDifficulty.Standard ? MaximumDifficulty : Models.TaskDifficulty.Light
    };
    /// <summary>Accepts only complete SHA-256 values.</summary>
    private static string Hash(string? value) => value?.Trim() is { Length: 64 } hash && hash.All(char.IsAsciiHexDigit) ? hash.ToUpperInvariant() : "";
}
