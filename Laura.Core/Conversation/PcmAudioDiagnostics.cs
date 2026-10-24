using System.Security.Cryptography;

namespace Laura.Core.Conversation;

/// <summary>Validates the provider's native output contract instead of silently interpreting another encoding or rate.</summary>
public static class PcmAudioFormat
{
    /// <summary>Gets the native output sample rate; samples are signed little-endian 16-bit mono PCM.</summary>
    public const int SampleRate = 24000;
    /// <summary>Accepts PCM with the documented native rate and mono channel count, without resampling.</summary>
    public static bool IsNativeOutput(string mimeType)
    {
        string[] parts = mimeType.Split(';', StringSplitOptions.TrimEntries);
        if (!parts[0].Equals("audio/pcm", StringComparison.OrdinalIgnoreCase)) { return false; }
        return parts.Skip(1).All(part => part.Equals("rate=24000", StringComparison.OrdinalIgnoreCase) || part.Equals("channels=1", StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>Numeric playback counters; underruns exclude startup priming and completed-turn silence.</summary>
public sealed record PcmPlaybackMetrics(long AddedBytes, long ReadBytes, int QueuedBytes, int HighWaterBytes, long Underruns, long MisalignedReads);

/// <summary>Explicit bounded A/B capture for provider PCM and real samples read into the device queue. Normal application use does not instantiate it.</summary>
public sealed class PcmAudioDiagnosticCapture : IDisposable
{
    private const int MaxBytes = 8 * 1024 * 1024;
    private readonly object _gate = new();
    private readonly MemoryStream _received = new();
    private readonly MemoryStream _played = new();
    /// <summary>Captures exact provider bytes in memory; no transformations or file I/O occur in the callback.</summary>
    public void Receive(byte[] pcm) { lock (_gate) { Append(_received, pcm); } }
    /// <summary>Captures only actual PCM bytes copied from the buffer, separately from priming/gap silence.</summary>
    public void Play(byte[] pcm) { lock (_gate) { Append(_played, pcm); } }
    private static void Append(MemoryStream target, byte[] pcm)
    { if (target.Length + pcm.Length > MaxBytes) { throw new IOException("Opt-in audio capture exceeded its byte bound."); } target.Write(pcm); }
    /// <summary>Compares exact byte order after playback drains; equality does not establish subjective voice quality or driver behavior.</summary>
    public PcmAudioComparison Compare()
    {
        lock (_gate)
        {
            byte[] received = _received.ToArray(), played = _played.ToArray();
            return new(received.Length, played.Length, Convert.ToHexString(SHA256.HashData(received)), Convert.ToHexString(SHA256.HashData(played)), received.AsSpan().SequenceEqual(played));
        }
    }
    /// <summary>Exports opt-in native-rate WAVs after capture, away from audio callbacks; duration excludes inserted gap silence.</summary>
    public async Task ExportAsync(string directory, CancellationToken token = default)
    {
        byte[] received, played; lock (_gate) { received = _received.ToArray(); played = _played.ToArray(); }
        Directory.CreateDirectory(directory);
        await WriteWaveAsync(Path.Combine(directory, "provider-a.wav"), received, token).ConfigureAwait(false);
        await WriteWaveAsync(Path.Combine(directory, "playback-b.wav"), played, token).ConfigureAwait(false);
    }
    private static async Task WriteWaveAsync(string path, byte[] pcm, CancellationToken token)
    {
        using var wave = new MemoryStream(); using (var writer = new BinaryWriter(wave, System.Text.Encoding.ASCII, true))
        {
            writer.Write("RIFF"u8); writer.Write(36 + pcm.Length); writer.Write("WAVEfmt "u8); writer.Write(16); writer.Write((short)1); writer.Write((short)1);
            writer.Write(PcmAudioFormat.SampleRate); writer.Write(PcmAudioFormat.SampleRate * 2); writer.Write((short)2); writer.Write((short)16);
            writer.Write("data"u8); writer.Write(pcm.Length); writer.Write(pcm);
        }
        await File.WriteAllBytesAsync(path, wave.ToArray(), token).ConfigureAwait(false);
    }
    /// <inheritdoc />
    public void Dispose() { _received.Dispose(); _played.Dispose(); GC.SuppressFinalize(this); }
}

/// <summary>Exact A/B integrity measurements; differing timbre must be listened to separately.</summary>
public sealed record PcmAudioComparison(int ReceivedBytes, int PlayedBytes, string ReceivedHash, string PlayedHash, bool Identical);
