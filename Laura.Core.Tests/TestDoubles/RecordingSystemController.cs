using Laura.Core.Abstractions;

namespace Laura.Core.Tests.TestDoubles;

/// <summary>Records system requests without altering the desktop session or audio settings.</summary>
public sealed class RecordingSystemController : ISystemController
{
    /// <summary>Gets or sets the simulated OS acceptance, independent of authorization.</summary>
    public bool AcceptLock { get; init; }
    /// <summary>Gets the number of actual backend lock requests.</summary>
    public int LockRequests { get; private set; }
    /// <inheritdoc />
    public bool LockWorkstation() { LockRequests++; return AcceptLock; }
    /// <inheritdoc />
    public void AdjustVolume(int steps) { }
    /// <inheritdoc />
    public void ToggleMute() { }
}
