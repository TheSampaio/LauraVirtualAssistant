using Laura.Core.Abstractions;

namespace Laura.Core.Tests.TestDoubles;

/// <summary>Provides a shell that cannot open windows or close the test process.</summary>
public sealed class StubShellController : IShellController
{
    /// <inheritdoc />
    public void ShowSettings() { }
    /// <inheritdoc />
    public void ToggleSettings() { }
    /// <inheritdoc />
    public void Shutdown() { }
}
