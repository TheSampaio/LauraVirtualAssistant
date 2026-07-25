namespace Laura.Core.Speech;

/// <summary>
/// A microphone the assistant can listen on.
/// </summary>
/// <param name="Id">
/// Stable identifier used to persist the choice. Empty means the system default
/// capture device.
/// </param>
/// <param name="Name">Human-readable device name for display in the UI.</param>
public sealed record AudioDevice(string Id, string Name);
