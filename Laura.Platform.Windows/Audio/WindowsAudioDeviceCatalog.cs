using Laura.Core.Abstractions;
using Laura.Core.Speech;
using Microsoft.Extensions.Logging;
using NAudio.Wave;

namespace Laura.Platform.Windows.Audio;

/// <summary>
/// Lists the capture devices reported by the Windows multimedia (MME) stack.
///
/// MME is used rather than WASAPI because the recognition engine feeds on a
/// 16 kHz mono PCM stream, exactly what an MME capture produces without resampling.
/// </summary>
public sealed class WindowsAudioDeviceCatalog : IAudioDeviceCatalog
{
    private readonly ILogger<WindowsAudioDeviceCatalog> _logger;

    /// <summary>
    /// Initializes the catalog.
    ///
    /// Args:
    ///     logger: Destination for diagnostic logs.
    /// </summary>
    public WindowsAudioDeviceCatalog(ILogger<WindowsAudioDeviceCatalog> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<AudioDevice> GetInputDevices()
    {
        try
        {
            // The empty id represents the Windows default capture device.
            List<AudioDevice> devices = [new AudioDevice(string.Empty, "System default")];

            for (int index = 0; index < WaveInEvent.DeviceCount; index++)
            {
                WaveInCapabilities capabilities = WaveInEvent.GetCapabilities(index);
                devices.Add(new AudioDevice(capabilities.ProductName, capabilities.ProductName));
            }

            return devices;
        }
        catch (Exception exception) when (exception is InvalidOperationException or NAudio.MmException)
        {
            _logger.LogWarning(exception, "Failed to enumerate the capture devices.");
            return [new AudioDevice(string.Empty, "System default")];
        }
    }

    /// <summary>
    /// Resolves a stored device id to its MME device index.
    ///
    /// The product name reported by MME is truncated to 31 characters, so the match
    /// is by prefix to survive that and minor driver-string differences.
    ///
    /// Args:
    ///     deviceId: The stored device id, or null/empty for the default device.
    ///
    /// Returns:
    ///     The device index, or -1 to mean the default capture device.
    /// </summary>
    public static int ResolveDeviceNumber(string? deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return -1;
        }

        for (int index = 0; index < WaveInEvent.DeviceCount; index++)
        {
            string name = WaveInEvent.GetCapabilities(index).ProductName;

            if (name.StartsWith(deviceId, StringComparison.OrdinalIgnoreCase)
                || deviceId.StartsWith(name, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }
}
