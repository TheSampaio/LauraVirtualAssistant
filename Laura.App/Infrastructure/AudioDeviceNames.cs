using System.Runtime.InteropServices;
using NAudio.Wave;

namespace Laura.App.Infrastructure;

/// <summary>Resolves native output names consistently for settings and the HUD.</summary>
internal static class AudioDeviceNames
{
    /// <summary>Returns the actual input name or a localized fallback after device removal.</summary>
    public static string Input(int id, string unavailable)
    {
        if (id < 0 || id >= WaveInterop.waveInGetNumDevs()) { return unavailable; }
        return WaveInterop.waveInGetDevCaps((nint)id, out WaveInCapabilities capabilities, Marshal.SizeOf<WaveInCapabilities>()) == NAudio.MmResult.NoError
            ? capabilities.ProductName : unavailable;
    }
    /// <summary>Returns the actual endpoint name or a localized fallback after device removal.</summary>
    public static string Output(int id, string unavailable)
    {
        if (id < 0 || id >= WaveInterop.waveOutGetNumDevs()) { return unavailable; }
        return WaveInterop.waveOutGetDevCaps((nint)id, out WaveOutCapabilities capabilities, Marshal.SizeOf<WaveOutCapabilities>()) == NAudio.MmResult.NoError
            ? capabilities.ProductName : unavailable;
    }
}
