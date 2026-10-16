using Laura.Core.Configuration;
using Laura.Core.Models;

namespace Laura.Core.Tests.Models;

/// <summary>Checks strict CPU preference, actual free-memory admission, reviewed profiles and unknown hardware.</summary>
public sealed class ModelOffloadPolicyTests
{
    [Theory]
    [InlineData(-1, 6, true, 99)]
    [InlineData(-1, 3, true, 16)]
    [InlineData(-1, 1, true, 0)]
    [InlineData(-1, 8, false, 0)]
    [InlineData(0, 8, true, 0)]
    [InlineData(16, 8, true, 16)]
    [InlineData(99, 3, true, 0)]
    [InlineData(8, 8, true, 0)]
    public void FreeDeviceMemoryAndExplicitPreferenceDetermineOnlyTheReviewedProfile(int preference, int freeGiB, bool supportsGpu, int expected)
    { Assert.Equal(expected, ModelOffloadPolicy.SelectLayers(SupportedModels.Intelligence, preference, (long)freeGiB << 30, supportsGpu)); }

    [Fact]
    public void UnknownMemoryUnverifiedWeightsAndExactBoundaryNeverInventGpuCapacity()
    {
        var model = SupportedModels.Intelligence; long full = HardwareProfile.RequiredAvailableRam(model);
        Assert.Equal(0, ModelOffloadPolicy.SelectLayers(model, -1, null, true));
        Assert.Equal(99, ModelOffloadPolicy.SelectLayers(model, -1, full, true));
        Assert.Equal(0, ModelOffloadPolicy.SelectLayers(model, 99, full - 1, true));
        Assert.Equal(0, ModelOffloadPolicy.SelectLayers(model with { Sha256 = "unreviewed" }, -1, 16L << 30, true));
        Assert.Equal(0, ModelOffloadPolicy.SelectLayers(SupportedModels.All[1], -1, 16L << 30, true));
    }

    [Theory]
    [InlineData(-1, -1)]
    [InlineData(0, 0)]
    [InlineData(16, 16)]
    [InlineData(99, 99)]
    [InlineData(int.MaxValue, -1)]
    public void SettingsRetainOnlyReviewedGpuProfiles(int value, int expected)
    { Assert.Equal(expected, new GenerativeAiOptions { LocalGpuLayers = value }.Sanitized().LocalGpuLayers); }
}
