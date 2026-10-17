using System.Buffers.Binary;
using System.Text.Json;
using System.Security.Cryptography;

namespace Laura.Platform.Windows.Conversation;

/// <summary>Closed credential-service operations; there is no token export or command execution operation.</summary>
internal enum BrokerOperation { Read, SignIn, Select, Models, Probe, Disconnect, Complete, Import }

/// <summary>One bounded local request. Credentials are accepted only by the one-way legacy migration operation.</summary>
internal sealed record BrokerRequest(BrokerOperation Operation, JsonElement Payload)
{
    /// <summary>Prevents accidental payload logging.</summary>
    public override string ToString() => $"Credential broker request: {Operation} (redacted)";
}

/// <summary>One framed reply contains only safe state, visible deltas, browser authorization or inference results.</summary>
internal sealed record BrokerReply(string Kind, JsonElement Payload, string Error = "", int? HttpStatus = null, Laura.Core.ChatGpt.ChatGptConnectionState? State = null)
{
    /// <summary>Prevents accidental context or authorization URL logging.</summary>
    public override string ToString() => "Credential broker reply (redacted)";
}

/// <summary>Versioned local framing with strict allocation bounds and no payload diagnostics.</summary>
internal static class BrokerProtocol
{
    internal const string PipeName = "Laura.ChatGpt.CredentialBroker.v1";
    internal const string ServiceName = "LauraCredentialBroker";
    internal const int MaximumBytes = 2 * 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new() { MaxDepth = 48, PropertyNameCaseInsensitive = false };

    /// <summary>Writes one size-prefixed JSON frame. Args: stream: Local authenticated pipe; value: Typed message; token: Deadline.</summary>
    internal static async Task WriteAsync<T>(Stream stream, T value, CancellationToken token)
    {
        byte[] data = JsonSerializer.SerializeToUtf8Bytes(value, Options);
        try
        {
            if (data.Length is < 1 or > MaximumBytes) { throw new InvalidDataException("broker_frame_limit"); }
            byte[] length = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
            await stream.WriteAsync(length, token).ConfigureAwait(false);
            await stream.WriteAsync(data, token).ConfigureAwait(false); await stream.FlushAsync(token).ConfigureAwait(false);
        }
        finally { CryptographicOperations.ZeroMemory(data); }
    }

    /// <summary>Reads exactly one bounded frame; truncation is a failure, never a successful terminal response.</summary>
    internal static async Task<T> ReadAsync<T>(Stream stream, CancellationToken token)
    {
        byte[] header = new byte[4]; await stream.ReadExactlyAsync(header, token).ConfigureAwait(false);
        int length = BinaryPrimitives.ReadInt32BigEndian(header);
        if (length is < 1 or > MaximumBytes) { throw new InvalidDataException("broker_frame_limit"); }
        byte[] data = new byte[length];
        try
        {
            await stream.ReadExactlyAsync(data, token).ConfigureAwait(false);
            return JsonSerializer.Deserialize<T>(data, Options) ?? throw new InvalidDataException("broker_frame_invalid");
        }
        finally { CryptographicOperations.ZeroMemory(data); }
    }

    /// <summary>Creates a detached payload for a closed protocol message without string interpolation.</summary>
    internal static JsonElement Payload<T>(T value) => JsonSerializer.SerializeToElement(value, Options);
}
