namespace Laura.Core.Models;

/// <summary>Measured capacity; detecting hardware does not establish backend compatibility or model performance.</summary>
/// <param name="Processors">Logical processors available to this process.</param>
/// <param name="TotalRam">Total physical RAM in bytes.</param>
/// <param name="AvailableRam">Currently available physical RAM in bytes.</param>
/// <param name="FreeDisk">Available bytes on model storage volume.</param>
/// <param name="Backend">Observed native CPU baseline; the resident inference engine reports its actual loaded backend separately.</param>
public sealed record HardwareProfile(int Processors, long TotalRam, long AvailableRam, long FreeDisk, string Backend)
{
    /// <summary>Gets the active physical core count across Windows processor groups, or null when unavailable.</summary>
    public int? PhysicalCores { get; init; }
    /// <summary>Gets the current process architecture.</summary>
    public string Architecture { get; init; } = "unknown";
    /// <summary>Gets CPU instructions actually exposed to this process.</summary>
    public IReadOnlyList<string> Instructions { get; init; } = [];
    /// <summary>Gets detected hardware adapters, excluding software adapters.</summary>
    public IReadOnlyList<GpuHardwareInfo> GpuAdapters { get; init; } = [];
    /// <summary>Gets whether DXGI enumeration succeeded, distinguishing unknown from a confirmed empty list.</summary>
    public bool GpuDiscoveryAvailable { get; init; }
    /// <summary>Gets measured used physical memory without substituting process working set.</summary>
    public long UsedRam => Math.Max(0, TotalRam - AvailableRam);

    /// <summary>Gets a conservative weight-plus-overhead admission margin, not a measured minimum hardware requirement.</summary>
    public static long RequiredAvailableRam(ModelArtifact model) => model.Size > long.MaxValue - 1024L * 1024 * 1024
        ? long.MaxValue : model.Size + 1024L * 1024 * 1024;

    /// <summary>Checks current CPU load admission independently of installation space or total-RAM recommendations.</summary>
    public bool CanLoad(ModelArtifact model) => model.Size > 0 && model.Size <= long.MaxValue - 1024L * 1024 * 1024
        && AvailableRam >= RequiredAvailableRam(model);

    /// <summary>Checks conservative installation space independently of whether weights are already installed.</summary>
    public bool CanInstall(ModelArtifact model) => model.Size > 0 && FreeDisk > 64L * 1024 * 1024
        && model.Size <= FreeDisk - 64L * 1024 * 1024;

    /// <summary>Checks both first-install capacity and conservative CPU admission; it does not guarantee performance.</summary>
    public bool CanRun(ModelArtifact model) => CanInstall(model) && CanLoad(model);
}
