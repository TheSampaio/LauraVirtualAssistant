using Laura.Core.Models;

namespace Laura.Core.Abstractions;

/// <summary>Reads model-host hardware on a worker without loading weights, opening audio or contacting a provider.</summary>
public interface IModelHardwareProbe
{
    /// <summary>Returns actual capacity and optional counters; unknown values remain unknown. Args: cancellationToken: Cancels observation. Returns: A single bounded hardware snapshot.</summary>
    Task<HardwareProfile> ReadAsync(CancellationToken cancellationToken);
}
