using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using LLama.Native;

namespace Laura.Platform.Windows.Conversation;

/// <summary>Owns one process-wide native selection and retains only closed offload counts from native diagnostics.</summary>
public sealed class WindowsInferenceBackend(bool cuda, string runtimeDirectory, bool allowCpuFallback = true)
{
    private static readonly object ConfigurationGate = new();
    private static WindowsInferenceBackend? _configured;
    private readonly List<nint> _runtimeHandles = [];
    private nint _cudaRuntime;
    private int _offloadedLayers;
    /// <summary>Gets the requested layer offload, after native selection has been configured.</summary>
    public int GpuLayers { get; private set; }
    /// <summary>Gets the native loader's observed offloaded-layer count; zero never proves acceleration.</summary>
    public int OffloadedLayers => Volatile.Read(ref _offloadedLayers);
    /// <summary>Gets whether the loaded runtime actually supports GPU offload.</summary>
    public bool SupportsGpu { get; private set; }
    /// <summary>Gets a closed reason when the native loader used CPU instead of the requested acceleration.</summary>
    public string FallbackReason { get; private set; } = "none";
    /// <summary>Clears evidence before a new native weight load so an earlier model cannot prove a later offload.</summary>
    public void ResetOffloadEvidence() => Volatile.Write(ref _offloadedLayers, 0);

    /// <summary>Configures the native loader once, preloading only verified app-local runtime DLLs when CUDA is requested.</summary>
    public void Configure(bool useGpu = true)
    {
        lock (ConfigurationGate)
        {
            if (_configured is not null)
            {
                if (!ReferenceEquals(_configured, this)) { throw new InvalidOperationException("The process-wide inference backend is already configured."); }
                if (cuda && useGpu && !SupportsGpu && FallbackReason == "cpu_selected") { FallbackReason = "restart_required"; }
                return;
            }
            if (NativeLibraryConfig.LLama.LibraryHasLoaded) { throw new InvalidOperationException("Native inference loaded before backend selection."); }
            bool tryCuda = cuda && useGpu;
            if (!useGpu) { FallbackReason = "cpu_selected"; }
            if (tryCuda)
            {
                try { LoadRedistributables(); }
                catch (Exception error) when (allowCpuFallback && error is FileNotFoundException or DirectoryNotFoundException)
                { tryCuda = false; FallbackReason = "runtime_missing"; }
                catch (IOException) when (allowCpuFallback) { tryCuda = false; FallbackReason = "runtime_invalid"; }
                catch (Exception error) when (allowCpuFallback && error is DllNotFoundException or BadImageFormatException)
                { tryCuda = false; FallbackReason = "runtime_unavailable"; }
            }
            NativeLibraryConfig.All.WithLogCallback((_, message) => ObserveOffload(message));
            NativeLibraryConfig.All.WithCuda(tryCuda).WithVulkan(false).WithSelectingPolicy(new PackagedNativeLibraryPolicy(tryCuda, allowCpuFallback));
            SupportsGpu = NativeApi.llama_supports_gpu_offload();
            if (cuda && useGpu && !SupportsGpu && !allowCpuFallback) { throw new InvalidOperationException("The loaded native runtime did not support required GPU offload."); }
            GpuLayers = tryCuda && SupportsGpu ? 99 : 0;
            if (tryCuda && !SupportsGpu) { FallbackReason = "driver_unavailable"; }
            _configured = this;
        }
    }

    /// <summary>Verifies runtime bytes before loading; handles intentionally remain rooted for the process-wide native runtime lifetime.</summary>
    private void LoadRedistributables()
    {
        foreach (var asset in RuntimeFiles)
        {
            string path = Path.Combine(Path.GetFullPath(runtimeDirectory), asset.Name);
            using var image = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (image.Length != asset.Size || !Convert.ToHexString(SHA256.HashData(image)).Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase))
            { throw new IOException("The app-local CUDA runtime failed integrity verification."); }
            nint handle = NativeLibrary.Load(path); _runtimeHandles.Add(handle);
            if (asset.Name == "cudart64_12.dll") { _cudaRuntime = handle; }
        }
    }

    /// <summary>Queries the selected CUDA device on the caller's worker; unknown or failed driver observations remain null.</summary>
    public unsafe (long Free, long Total)? ReadDeviceMemory()
    {
        if (!SupportsGpu || GpuLayers == 0 || _cudaRuntime == 0) { return null; }
        var select = (delegate* unmanaged<int, int>)NativeLibrary.GetExport(_cudaRuntime, "cudaSetDevice");
        var read = (delegate* unmanaged<nuint*, nuint*, int>)NativeLibrary.GetExport(_cudaRuntime, "cudaMemGetInfo");
        nuint free = 0, total = 0;
        if (select(0) != 0 || read(&free, &total) != 0 || free > total || total > long.MaxValue) { return null; }
        return ((long)free, (long)total);
    }

    /// <summary>Accepts only the exact bounded numeric offload diagnostic and discards every other native message.</summary>
    private void ObserveOffload(string message)
    {
        const string prefix = "load_tensors: offloaded ", suffix = " layers to GPU";
        if (message.Length > 128) { return; }
        ReadOnlySpan<char> text = message.AsSpan().Trim();
        if (!text.StartsWith(prefix, StringComparison.Ordinal) || !text.EndsWith(suffix, StringComparison.Ordinal)) { return; }
        ReadOnlySpan<char> counts = text[prefix.Length..^suffix.Length]; int separator = counts.IndexOf('/');
        if (separator <= 0 || !int.TryParse(counts[..separator], NumberStyles.None, CultureInfo.InvariantCulture, out int offloaded)
            || !int.TryParse(counts[(separator + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out int total)
            || offloaded < 0 || total is < 1 or > 1024 || offloaded > total) { return; }
        Volatile.Write(ref _offloadedLayers, offloaded);
    }

    // Values come from the SHA-256-verified official CUDA 12.9.1 redistributable archives.
    private static readonly (string Name, long Size, string Sha256)[] RuntimeFiles =
    [
        ("cudart64_12.dll", 583680, "760c38928bbe5759f7b31ed6692599eb7ec83cedd5702e84c2b72028a89837e1"),
        ("cublasLt64_12.dll", 668669952, "c3a05ea244c937314afec09f87b91f814c7e27977681f6c67eb51bb06ced3a4a"),
        ("cublas64_12.dll", 102518272, "90052a83efd1b57a8e3616a6590b335855f81b814a4f16eecb7b5bf6d1b1d4eb"),
    ];
}
