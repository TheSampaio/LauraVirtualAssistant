using Laura.Core.Abstractions;

namespace Laura.Core.Tests.TestDoubles;

/// <summary>Provides a shell that cannot open windows or close the test process.</summary>
public sealed class StubShellController : IShellController
{
    /// <summary>Gets whether normal post-response processing requested shutdown.</summary>
    public bool ShutdownRequested { get; private set; }
    /// <inheritdoc />
    public void ShowSettings() { }
    /// <inheritdoc />
    public void ToggleSettings() { }
    /// <inheritdoc />
    public void Shutdown() => ShutdownRequested = true;
}
