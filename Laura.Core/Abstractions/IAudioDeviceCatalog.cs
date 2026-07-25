using Laura.Core.Speech;

namespace Laura.Core.Abstractions;

/// <summary>
/// Lists the microphones the assistant can listen on.
/// </summary>
public interface IAudioDeviceCatalog
{
    /// <summary>
    /// Enumerates the available capture devices.
    ///
    /// Returns:
    ///     The input devices, or an empty list when none is present. The first
    ///     entry, when present, represents the system default device.
    /// </summary>
    IReadOnlyList<AudioDevice> GetInputDevices();
}
