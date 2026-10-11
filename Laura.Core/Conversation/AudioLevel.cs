using System.Buffers.Binary;

namespace Laura.Core.Conversation;

/// <summary>Measures normalized loudness without keeping audio or depending on an audio device.</summary>
public static class AudioLevel
{
    /// <summary>Calculates RMS amplitude for signed little-endian 16-bit PCM.</summary>
    public static float Measure(ReadOnlySpan<byte> pcm)
    {
        int samples = pcm.Length / 2;
        if (samples == 0) { return 0; }
        double sum = 0;
        for (int offset = 0; offset + 1 < pcm.Length; offset += 2)
        { double value = BinaryPrimitives.ReadInt16LittleEndian(pcm[offset..]) / 32768d; sum += value * value; }
        return (float)Math.Clamp(Math.Sqrt(sum / samples) * 5, 0, 1);
    }
}
