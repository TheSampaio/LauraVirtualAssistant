using Laura.Platform.Windows.Conversation;

/// <summary>Rejects absent or modified runtime binaries before native GPU initialization.</summary>
internal static class CudaRuntimeValidationSmoke
{
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
