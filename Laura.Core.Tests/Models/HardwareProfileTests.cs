using Laura.Core.Models;

namespace Laura.Core.Tests.Models;

/// <summary>Checks current-memory admission independently of disk installation, recommendations and detected GPU capacity.</summary>
public sealed class HardwareProfileTests
{
    [Fact]
    public void InstalledWeightsCanLoadWithLowFreeDiskButNewDownloadsCannot()
    {
        var model = SupportedModels.Intelligence;
        var hardware = new HardwareProfile(4, 8L << 30, HardwareProfile.RequiredAvailableRam(model), 0, "CPU");
        Assert.True(hardware.CanLoad(model)); Assert.False(hardware.CanInstall(model)); Assert.False(hardware.CanRun(model));
        Assert.False((hardware with { AvailableRam = hardware.AvailableRam - 1 }).CanLoad(model));
        Assert.True((hardware with { FreeDisk = model.Size + (64L << 20) }).CanRun(model));
        Assert.False((hardware with { FreeDisk = model.Size + (64L << 20) - 1 }).CanInstall(model));
    }

    [Fact]
    public void SharedGpuRamNeverSubstitutesForAvailablePhysicalMemory()
    {
        var model = SupportedModels.Intelligence;
        var hardware = new HardwareProfile(8, 16L << 30, 1L << 30, 20L << 30, "CPU")
        { GpuDiscoveryAvailable = true, GpuAdapters = [new("Synthetic GPU", 24UL << 30, 8UL << 30, 1, 2)] };
        Assert.False(hardware.CanLoad(model)); Assert.Equal(15L << 30, hardware.UsedRam);
        Assert.Null(hardware.GpuAdapters[0].UsedDedicatedMemory);
    }

    [Fact]
    public void UnknownRecommendationIsNotInventedAndInvalidLengthsCannotOverflowAdmission()
    {
        var model = SupportedModels.Intelligence; Assert.Equal(0, model.RecommendedRam);
        var hardware = new HardwareProfile(4, long.MaxValue, long.MaxValue, long.MaxValue, "CPU");
        Assert.True(hardware.CanRun(model));
        Assert.False(hardware.CanLoad(model with { Size = long.MaxValue }));
        Assert.False(hardware.CanInstall(model with { Size = long.MaxValue }));
        Assert.False(hardware.CanLoad(model with { Size = 0 })); Assert.False(hardware.CanInstall(model with { Size = -1 }));
    }

    [Fact]
    public void ThroughputUsesActualSamplerCountersAndRequiresASustainedMeasuredInterval()
    {
        Assert.Equal(20, new LocalInferenceMetrics(100, 41, 1000, 2000).SampledTokensPerSecond);
        Assert.Null(new LocalInferenceMetrics(100, 0, 1000, 2000).SampledTokensPerSecond);
        Assert.Null(new LocalInferenceMetrics(100, 1, 1000, 2000).SampledTokensPerSecond);
        Assert.Null(new LocalInferenceMetrics(100, 40, 1000, 0).SampledTokensPerSecond);
        Assert.Null(new LocalInferenceMetrics(100, 40, 1000, double.NaN).SampledTokensPerSecond);
    }
}
