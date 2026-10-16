namespace Laura.Core.Models;

/// <summary>A pinned model artifact. RAM estimates include a conservative context overhead.</summary>
/// <param name="Id">Stable identifier and safe local filename stem.</param>
/// <param name="Name">Display name.</param>
/// <param name="Source">Immutable HTTPS artifact URL.</param>
/// <param name="Size">Exact byte length.</param>
/// <param name="Sha256">Publisher's LFS SHA256 digest.</param>
/// <param name="Quantization">Quantization description.</param>
/// <param name="ContextLength">Supported context ceiling.</param>
/// <param name="RecommendedRam">Measured or reviewed total system RAM recommendation; zero means not established.</param>
/// <param name="License">Weight license name.</param>
/// <param name="LicenseUrl">Weight terms and attribution.</param>
public sealed record ModelArtifact(string Id, string Name, Uri Source, long Size, string Sha256,
    string Quantization, int ContextLength, long RecommendedRam, string License, Uri LicenseUrl)
{
    /// <summary>Gets whether this is a multilingual speech model rather than a text model.</summary>
    public bool IsSpeechModel { get; init; }
    /// <summary>Gets the reviewed container extension.</summary>
    public string FileExtension => IsSpeechModel ? ".bin" : ".gguf";
}

/// <summary>Download status, delivered without blocking the caller.</summary>
/// <param name="BytesReceived">Bytes already stored.</param>
/// <param name="TotalBytes">Expected total bytes.</param>
/// <param name="Verifying">Whether SHA256 verification is in progress.</param>
public sealed record ModelDownloadProgress(long BytesReceived, long TotalBytes, bool Verifying);
