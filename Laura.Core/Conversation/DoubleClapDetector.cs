using System.Buffers.Binary;

namespace Laura.Core.Conversation;

/// <summary>Detects two isolated PCM transients locally without retaining or transmitting microphone audio.</summary>
public sealed class DoubleClapDetector(int sensitivity = 50)
{
    private readonly double _threshold = .70 - Math.Clamp(sensitivity, 0, 100) * .005;
    private double _noiseFloor = .005;
    private long? _transient, _firstClap;
    private long _cooldown;
    /// <summary>Consumes one short 16-bit mono frame using a monotonic millisecond timestamp.</summary>
    public bool Process(ReadOnlySpan<byte> pcm, long milliseconds)
    {
        if (pcm.Length < 2 || milliseconds < _cooldown) { return false; }
        double peak = 0, energy = 0;
        for (int offset = 0; offset + 1 < pcm.Length; offset += 2)
        {
            double sample = BinaryPrimitives.ReadInt16LittleEndian(pcm[offset..]) / 32768d;
            peak = Math.Max(peak, Math.Abs(sample)); energy += sample * sample;
        }
        double rms = Math.Sqrt(energy / (pcm.Length / 2));
        double threshold = Math.Max(_threshold, _noiseFloor * 6);
        bool impulse = peak >= threshold && rms >= .025 && peak / Math.Max(.0001, rms) >= 2.5;
        if (_transient is null && impulse) { _transient = milliseconds; }
        if (_transient is { } started)
        {
            if (milliseconds - started > 140) { _transient = null; _firstClap = null; return false; }
            if (peak >= threshold * .35) { return false; }
            _transient = null;
            if (_firstClap is { } first && started - first is >= 180 and <= 900)
            { _firstClap = null; _cooldown = milliseconds + 3000; return true; }
            _firstClap = started;
        }
        if (!impulse) { _noiseFloor = Math.Clamp(_noiseFloor * .97 + rms * .03, .002, .08); }
        if (_firstClap is { } previous && milliseconds - previous > 900) { _firstClap = null; }
        return false;
    }
}
