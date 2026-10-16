using Laura.Platform.Windows.Conversation;

/// <summary>Rejects absent or modified runtime binaries before native GPU initialization.</summary>
internal static class CudaRuntimeValidationSmoke
{
    /// <summary>Proves a missing/invalid optional runtime falls back to CPU, or cold explicit CPU avoids GPU initialization.</summary>
    internal static async Task RunFallbackAsync(string directory, string runtimeDirectory, string scenario, CancellationToken token)
    {
        string selected = scenario == "cpu" ? runtimeDirectory : Path.Combine(directory, "Synthetic optional runtime");
        if (scenario == "invalid")
        {
            Directory.CreateDirectory(selected); string copy = Path.Combine(selected, "cudart64_12.dll");
            File.Copy(Path.Combine(runtimeDirectory, "cudart64_12.dll"), copy, true);
            await using var modified = new FileStream(copy, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            modified.Position = modified.Length - 1; int value = modified.ReadByte(); modified.Position--; modified.WriteByte((byte)(value ^ 1));
            await modified.FlushAsync(token);
        }
        var backend = new WindowsInferenceBackend(true, selected, true); backend.Configure(scenario != "cpu");
        string expected = scenario switch { "missing" => "runtime_missing", "invalid" => "runtime_invalid", "cpu" => "cpu_selected", _ => throw new ArgumentException("Unknown fallback diagnostic.") };
        if (backend.SupportsGpu || backend.GpuLayers != 0 || backend.FallbackReason != expected || backend.ReadDeviceMemory() is not null)
        { throw new InvalidOperationException("Actual CPU fallback did not preserve its safe native cause."); }
        if (scenario == "cpu")
        {
            backend.Configure(true);
            if (backend.FallbackReason != "restart_required") { throw new InvalidOperationException("Cold CPU runtime switching did not report its restart requirement."); }
        }
        await File.WriteAllTextAsync(Path.Combine(directory, "cuda-fallback.txt"), "Actual native CPU runtime selected for " + scenario + "; no unverified runtime was initialized and no model/audio/network was used.", token);
        Console.WriteLine("Actual native CPU fallback verified: " + scenario);
    }
    /// <summary>Uses only synthetic isolated directories and a modified copy of one verified runtime DLL.</summary>
    internal static async Task RunAsync(string directory, string runtimeDirectory, CancellationToken token)
    {
        bool missingRejected = false, corruptRejected = false;
        try { new WindowsInferenceBackend(true, Path.Combine(directory, "Missing CUDA runtime"), false).Configure(); }
        catch (IOException) { missingRejected = true; }
        string corruptRoot = Path.Combine(directory, "Corrupt CUDA runtime"); Directory.CreateDirectory(corruptRoot);
        string copy = Path.Combine(corruptRoot, "cudart64_12.dll");
        File.Copy(Path.Combine(runtimeDirectory, "cudart64_12.dll"), copy, true);
        await using (var modified = new FileStream(copy, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            modified.Position = modified.Length - 1; int original = modified.ReadByte(); modified.Position--;
            modified.WriteByte((byte)(original ^ 1)); await modified.FlushAsync(token);
        }
        try { new WindowsInferenceBackend(true, corruptRoot, false).Configure(); }
        catch (IOException) { corruptRejected = true; }
        if (!missingRejected || !corruptRejected) { throw new InvalidOperationException("Absent or modified CUDA runtime bytes were not rejected before initialization."); }
        await File.WriteAllTextAsync(Path.Combine(directory, "cuda-runtime-validation.txt"),
            "Absent runtime and same-length modified DLL rejected before GPU initialization. No weights, microphone, cloud or system changes.", token);
        Console.WriteLine("Missing and modified app-local CUDA runtime rejected before initialization.");
    }
}
