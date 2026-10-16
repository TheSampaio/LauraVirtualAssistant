using Laura.Core.Abstractions;
using Laura.Core.Models;

namespace Laura.Platform.Windows.System;

/// <summary>Collects native model-host capacity and optional GPU counters without blocking a UI caller.</summary>
public sealed class WindowsModelHardwareProbe(ILocalModelManager models, WindowsGpuMonitor gpu) : IModelHardwareProbe
{
    /// <inheritdoc />
    public Task<HardwareProfile> ReadAsync(CancellationToken cancellationToken) => Task.Run(async () =>
    {
        var profile = WindowsModelHardware.Read(Path.GetDirectoryName(models.GetModelPath(SupportedModels.Intelligence.Id))!);
        _ = gpu.SampleResources();
        await Task.Delay(550, cancellationToken).ConfigureAwait(false);
        GpuSnapshot? measured = gpu.SampleResources();
        return profile with { GpuAdapters = profile.GpuAdapters.Select(adapter => measured?.AdapterId == adapter.AdapterId
            ? adapter with { UsedDedicatedMemory = measured.UsedDedicatedMemory } : adapter).ToArray() };
    }, cancellationToken);
}
