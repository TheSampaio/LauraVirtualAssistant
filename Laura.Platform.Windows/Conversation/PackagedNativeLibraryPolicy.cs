using LLama.Abstractions;
using LLama.Native;

namespace Laura.Platform.Windows.Conversation;

/// <summary>Selects the pinned app-local CUDA build without requiring a separate Toolkit installation.</summary>
internal sealed class PackagedNativeLibraryPolicy(bool cuda, bool allowCpuFallback) : INativeLibrarySelectingPolicy
{
    /// <inheritdoc />
    public IEnumerable<INativeLibrary> Apply(NativeLibraryConfig.Description description, SystemInfo systemInfo, NativeLogConfig.LLamaLogCallback? logCallback)
    {
        // The SDK's Toolkit-path check is irrelevant to the reviewed app-local redistributables.
        // Driver initialization, actual backend support and offload evidence are checked separately.
        if (cuda) { yield return new NativeLibraryWithCuda(12, description.Library, description.AvxLevel, true); }
        if (!cuda || allowCpuFallback)
        {
            if (description.AvxLevel >= AvxLevel.Avx512) { yield return new NativeLibraryWithAvx(description.Library, AvxLevel.Avx512, false); }
            if (description.AvxLevel >= AvxLevel.Avx2) { yield return new NativeLibraryWithAvx(description.Library, AvxLevel.Avx2, false); }
            if (description.AvxLevel >= AvxLevel.Avx) { yield return new NativeLibraryWithAvx(description.Library, AvxLevel.Avx, false); }
            yield return new NativeLibraryWithAvx(description.Library, AvxLevel.None, false);
        }
    }
}
