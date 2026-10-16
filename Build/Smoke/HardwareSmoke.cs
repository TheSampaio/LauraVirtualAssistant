using System.Text.Json;
using Laura.Core.Models;
using Laura.Platform.Windows.System;

/// <summary>Records actual model-host capacity and optional GPU counters without loading weights or opening audio.</summary>
internal static class HardwareSmoke
{
    /// <summary>Queries native CPU topology, RAM/disk and DXGI hardware, preserving unavailable counters as null.</summary>
    internal static async Task RunAsync(string directory, CancellationToken token)
    {
        using var http = new HttpClient();
        using var models = new LocalModelManager(Path.Combine(directory, "Models"), http, SupportedModels.All);
        using var gpu = new WindowsGpuMonitor();
        var probe = new WindowsModelHardwareProbe(models, gpu);
        HardwareProfile snapshot = await probe.ReadAsync(token);
        if (snapshot.Processors <= 0 || snapshot.TotalRam <= 0 || snapshot.AvailableRam <= 0 || snapshot.AvailableRam > snapshot.TotalRam)
        { throw new InvalidOperationException("Native hardware capacity is invalid."); }
        await File.WriteAllTextAsync(Path.Combine(directory, "hardware.json"), JsonSerializer.Serialize(new
        { hardware = snapshot, backend = "cpu_only", gpuAccelerationVerified = false, minimumRequirementsMeasured = false,
            qwenAdmissionBytes = HardwareProfile.RequiredAvailableRam(SupportedModels.Intelligence), qwenCurrentRamAdmission = snapshot.CanLoad(SupportedModels.Intelligence) },
            new JsonSerializerOptions { WriteIndented = true }), token);
        Console.WriteLine("Native CPU topology, runtime instructions, RAM/disk and DXGI adapter snapshot recorded; unknown GPU counters stay null. No GPU inference claim, weights, microphone or cloud request.");
    }
}
