namespace Laura.Core.Models;

/// <summary>A pinned model artifact. RAM estimates include a conservative context overhead.</summary>
/// <param name="Id">Stable identifier and safe local filename stem.</param>
/// <param name="Name">Display name.</param>
/// <param name="Source">Immutable HTTPS artifact URL.</param>
/// <param name="Size">Exact byte length.</param>
/// <param name="Sha256">Publisher's LFS SHA256 digest.</param>
/// <param name="Quantization">Quantization description.</param>
/// <param name="ContextLength">Supported context ceiling.</param>
/// <param name="RecommendedRam">Conservative total system RAM recommendation.</param>
/// <param name="License">Weight license name.</param>
/// <param name="LicenseUrl">Weight terms and attribution.</param>
public sealed record ModelArtifact(string Id, string Name, Uri Source, long Size, string Sha256,
    string Quantization, int ContextLength, long RecommendedRam, string License, Uri LicenseUrl);

/// <summary>Measured CPU/RAM/disk information; unknown GPU values are not guessed.</summary>
/// <param name="Processors">Logical CPU count.</param>
/// <param name="TotalRam">Total physical RAM in bytes.</param>
/// <param name="AvailableRam">Currently available physical RAM in bytes.</param>
/// <param name="FreeDisk">Available bytes on model storage volume.</param>
/// <param name="Backend">Packaged acceleration backend.</param>
public sealed record HardwareProfile(int Processors, long TotalRam, long AvailableRam, long FreeDisk, string Backend)
{
    /// <summary>Checks disk and conservative available memory for installation and CPU inference.</summary>
    public bool CanRun(ModelArtifact model) => model.Size + 64 * 1024 * 1024 < FreeDisk
        && TotalRam >= model.RecommendedRam && AvailableRam > model.Size + 512 * 1024 * 1024;
}

/// <summary>Download status, delivered without blocking the caller.</summary>
/// <param name="BytesReceived">Bytes already stored.</param>
/// <param name="TotalBytes">Expected total bytes.</param>
/// <param name="Verifying">Whether SHA256 verification is in progress.</param>
public sealed record ModelDownloadProgress(long BytesReceived, long TotalBytes, bool Verifying);
