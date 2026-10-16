namespace Laura.Core.Models;

/// <summary>Describes detected GPU capacity without treating shared system RAM as dedicated VRAM.</summary>
/// <param name="Name">Driver-reported adapter name.</param>
/// <param name="DedicatedMemory">Total dedicated video memory in bytes, not currently free memory.</param>
/// <param name="SharedMemoryLimit">Maximum shared system memory available to the adapter, not allocated VRAM.</param>
/// <param name="VendorId">Native hardware vendor identifier.</param>
/// <param name="DeviceId">Native hardware device identifier.</param>
public sealed record GpuHardwareInfo(string Name, ulong DedicatedMemory, ulong SharedMemoryLimit, uint VendorId, uint DeviceId)
{
    /// <summary>Gets the native session-local adapter identity used to match counters without relying on display names.</summary>
    public string AdapterId { get; init; } = "";
    /// <summary>Gets measured system-wide dedicated usage for this adapter, or null when its counter is unavailable.</summary>
    public ulong? UsedDedicatedMemory { get; init; }
}
