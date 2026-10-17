using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text.Json;
using Laura.Core.ChatGpt;
using Laura.Platform.Windows.Conversation;

/// <summary>Exercises bounded credential-service framing and rejects a local pipe impostor without sending a credential payload.</summary>
internal static class CredentialBrokerSmoke
{
    /// <summary>Runs isolated protocol checks without an authorized ChatGPT account or administrator privileges.</summary>
    internal static async Task<int> RunAsync(string directory)
    {
        Directory.CreateDirectory(directory); var checks = new List<string>(); using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        await using (var stream = new MemoryStream())
        {
            var expected = new BrokerRequest(BrokerOperation.Probe, BrokerProtocol.Payload(new BrokerProbe("synthetic-model", true)));
            await BrokerProtocol.WriteAsync(stream, expected, token); stream.Position = 0;
            var actual = await BrokerProtocol.ReadAsync<BrokerRequest>(stream, token);
            Require(actual.Operation == expected.Operation && actual.Payload.Deserialize<BrokerProbe>()?.IncludedPlanOnlyConfirmed == true, "typed_roundtrip");
        }
        checks.Add("bounded_typed_roundtrip");
        foreach (int invalid in new[] { -1, 0, BrokerProtocol.MaximumBytes + 1 })
        {
            byte[] size = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(size, invalid);
            await using var stream = new MemoryStream(size);
            await RejectAsync<InvalidDataException>(() => BrokerProtocol.ReadAsync<BrokerRequest>(stream, token));
        }
        checks.Add("negative_zero_and_oversized_frames_rejected_before_allocation");
        await using (var truncated = new MemoryStream(new byte[] { 0, 0, 0, 20, 123 }))
        { await RejectAsync<EndOfStreamException>(() => BrokerProtocol.ReadAsync<BrokerReply>(truncated, token)); }
        checks.Add("truncated_frame_is_not_completed_inference");
        await using (var invalidJson = new MemoryStream(new byte[] { 0, 0, 0, 1, 123 }))
        { await RejectAsync<JsonException>(() => BrokerProtocol.ReadAsync<BrokerReply>(invalidJson, token)); }
        checks.Add("malformed_json_rejected");
        Require(!new BrokerRequest(BrokerOperation.Import, BrokerProtocol.Payload("synthetic_private_credential")).ToString().Contains("synthetic_private", StringComparison.Ordinal)
            && !new BrokerReply("browser", BrokerProtocol.Payload("synthetic_private_authorization")).ToString().Contains("synthetic_private", StringComparison.Ordinal), "redacted_formatting");
        checks.Add("payload_and_browser_url_formatting_redacted");
        Require(!Enum.GetNames<BrokerOperation>().Any(name => name.Contains("Export", StringComparison.Ordinal) || name.Contains("Execute", StringComparison.Ordinal)), "closed_operations");
        checks.Add("no_token_export_or_command_executor");
        var store = new ChatGptCredentialStore(Path.Combine(directory, "Credentials"), new WindowsPrivateDataProtector());
        using (await store.AcquireAsync(token))
        {
            var data = await store.ReadAsync(token); string original = data.Host;
            data.Host = "invalid-host";
            await RejectAsync<InvalidDataException>(() => store.SaveAsync(data, token));
            Require((await store.ReadAsync(token)).Host == original, "malformed_import_preserves_store");
        }
        checks.Add("invalid_migration_cannot_corrupt_protected_store");
        if (!BrokerServiceInspection.IsRegistered())
        {
            await using var impostor = new NamedPipeServerStream(BrokerProtocol.PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.FirstPipeInstance);
            Task connected = impostor.WaitForConnectionAsync(token);
            using var client = new ChatGptBrokerClient(store, Path.Combine(directory, "Credentials"));
            Task<ChatGptConnectionState> read = client.ReadAsync(token); await connected;
            var state = await read;
            byte[] received = new byte[1]; int count = await impostor.ReadAsync(received, token);
            Require(count == 0 && state.Error is "broker_identity_invalid" or "broker_identity_unavailable" && !client.IsAvailable, "pipe_impostor");
            checks.Add("scm_identity_rejects_pipe_squatter_before_payload");
        }
        await File.WriteAllTextAsync(Path.Combine(directory, "broker-synthetic.json"), JsonSerializer.Serialize(new { Kind = "synthetic_protocol", RealServiceIsolation = false, Passed = checks.Count, Checks = checks }, new JsonSerializerOptions { WriteIndented = true }), token);
        Console.WriteLine($"Credential broker synthetic checks passed: {checks.Count}. Real Windows service isolation is not claimed."); return 0;
    }

    /// <summary>Asserts a failure without logging payloads.</summary>
    private static async Task RejectAsync<T>(Func<Task> action) where T : Exception
    { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected bounded protocol failure."); }
    /// <summary>Reports only a symbolic assertion label.</summary>
    private static void Require(bool condition, string check) { if (!condition) { throw new InvalidOperationException(check); } }
}

/// <summary>Reads only service availability for the optional pipe-impostor test.</summary>
internal static class BrokerServiceInspection
{
    /// <summary>Checks fixed service registration without requesting change access.</summary>
    internal static bool IsRegistered()
    {
        using var manager = new System.Diagnostics.Process { StartInfo = new("sc.exe", "query LauraCredentialBroker") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true } };
        manager.Start(); manager.StandardOutput.ReadToEnd(); manager.StandardError.ReadToEnd(); manager.WaitForExit(); return manager.ExitCode == 0;
    }
}
