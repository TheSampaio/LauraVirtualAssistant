namespace Laura.Core.Models;

/// <summary>Chooses only reviewed full/partial Qwen profiles using a conservative snapshot of actually free device memory.</summary>
public static class ModelOffloadPolicy
{
    /// <summary>Returns admitted GPU layers or zero for CPU; detection alone and unknown free memory never authorize offload.</summary>
    public static int SelectLayers(ModelArtifact model, int preference, long? freeDeviceMemory, bool runtimeSupportsGpu)
    {
        if (preference == 0 || !runtimeSupportsGpu || freeDeviceMemory is null || freeDeviceMemory <= 0
            || model.Id != SupportedModels.Intelligence.Id || model.Size != SupportedModels.Intelligence.Size
            || !model.Sha256.Equals(SupportedModels.Intelligence.Sha256, StringComparison.OrdinalIgnoreCase)) { return 0; }
        const long margin = 1024L * 1024 * 1024;
        long full = HardwareProfile.RequiredAvailableRam(model), partial = (model.Size * 16 + 32) / 33 + margin;
        return preference switch
        {
            99 when freeDeviceMemory >= full => 99,
            16 when freeDeviceMemory >= partial => 16,
            -1 when freeDeviceMemory >= full => 99,
            -1 when freeDeviceMemory >= partial => 16,
            _ => 0,
        };
    }
}
