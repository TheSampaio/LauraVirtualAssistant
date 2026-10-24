using Laura.Core.Configuration;
using Laura.Core.Models;

namespace Laura.Core.Tests.Configuration;

/// <summary>Verifies ready-to-install local model defaults.</summary>
public sealed class LocalReadyDefaultsTests
{
    /// <summary>Default model paths are flat and beside Brain; executable resources stay outside Models.</summary>
    [Fact]
    public void DefaultsUseFlatDocumentsModelsAndAutomaticGpu()
    {
        var model = new LocalModelOptions(); var voice = new LocalVoiceOptions();
        Assert.False(model.Enabled); Assert.Equal(-1, model.GpuLayers);
        Assert.Equal(LocalModelPaths.Models, Path.GetDirectoryName(model.ModelPath));
        Assert.Equal(LocalModelPackages.Qwen.FileName, Path.GetFileName(model.ModelPath));
        Assert.Equal(LocalModelPaths.Models, voice.KokoroDirectory); Assert.Equal(LocalModelPaths.Models, voice.WhisperDirectory);
        Assert.DoesNotContain(LocalModelPaths.Models + Path.DirectorySeparatorChar, model.Executable, StringComparison.OrdinalIgnoreCase);
    }
}
