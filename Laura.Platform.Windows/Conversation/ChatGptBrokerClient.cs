using System.Diagnostics;
using System.ComponentModel;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;
using Laura.Core.Abstractions;
using Laura.Core.ChatGpt;

namespace Laura.Platform.Windows.Conversation;

/// <summary>Connects native Laura controls to the authenticated credential service without receiving OAuth tokens.</summary>
public sealed class ChatGptBrokerClient(ChatGptCredentialStore legacyStore, string legacyDirectory) : IChatGptConnection, IChatGptInference, IDisposable
{
    private readonly SemaphoreSlim _migrationGate = new(1, 1);
    private int _migrationChecked, _available;
    private string _registration = "";
    /// <inheritdoc />
    public bool IsAvailable => Volatile.Read(ref _available) == 1;
    /// <inheritdoc />
    public Guid? ActiveRegistration => Guid.TryParse(Volatile.Read(ref _registration), out var id) ? id : null;

    /// <inheritdoc />
    public async Task<ChatGptConnectionState> ReadAsync(CancellationToken cancellationToken = default)
    {
        try { return await SendAsync<ChatGptConnectionState>(BrokerOperation.Read, new { }, null, cancellationToken).ConfigureAwait(false); }
        catch (ChatGptConnectionException error) when (error.Code is "broker_required" or "broker_identity_invalid" or "broker_identity_unavailable")
        { return new(null, [], "disconnected", error.Code); }
    }
    /// <inheritdoc />
    public Task<ChatGptConnectionState> SignInAsync(Guid? registration, CancellationToken cancellationToken = default) => SendAsync<ChatGptConnectionState>(BrokerOperation.SignIn, registration, null, cancellationToken);
    /// <inheritdoc />
    public Task<ChatGptConnectionState> SelectAsync(Guid registration, CancellationToken cancellationToken = default) => SendAsync<ChatGptConnectionState>(BrokerOperation.Select, registration, null, cancellationToken);
    /// <inheritdoc />
    public Task<IReadOnlyList<ChatGptModel>> ModelsAsync(CancellationToken cancellationToken = default) => SendAsync<IReadOnlyList<ChatGptModel>>(BrokerOperation.Models, new { }, null, cancellationToken);
    /// <inheritdoc />
    public Task<ChatGptProbeResult> ProbeAsync(string model, bool includedPlanOnlyConfirmed, CancellationToken cancellationToken = default) => SendAsync<ChatGptProbeResult>(BrokerOperation.Probe, new BrokerProbe(model, includedPlanOnlyConfirmed), null, cancellationToken);
    /// <inheritdoc />
    public Task<ChatGptConnectionState> DisconnectAsync(CancellationToken cancellationToken = default) => SendAsync<ChatGptConnectionState>(BrokerOperation.Disconnect, new { }, null, cancellationToken);
    /// <inheritdoc />
    public Task<ChatGptInferenceResult> CompleteAsync(ChatGptInferenceRequest inference, Action<string>? textDelta, CancellationToken cancellationToken) => SendAsync<ChatGptInferenceResult>(BrokerOperation.Complete, inference, textDelta, cancellationToken);

    /// <summary>Migrates only Laura's own legacy DPAPI store once; no direct inference fallback is available.</summary>
    private async Task<T> SendAsync<T>(BrokerOperation operation, object? payload, Action<string>? delta, CancellationToken token)
    {
        await EnsureMigrationAsync(token).ConfigureAwait(false);
        return await TransportAsync<T>(operation, payload, delta, token).ConfigureAwait(false);
    }

    /// <summary>Transfers the existing registration over an SCM-authenticated local pipe, then removes all legacy token fields.</summary>
    private async Task EnsureMigrationAsync(CancellationToken token)
    {
        if (Volatile.Read(ref _migrationChecked) != 0) { return; }
        await _migrationGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (_migrationChecked != 0) { return; }
            if (File.Exists(Path.Combine(legacyDirectory, "chatgpt-credentials.dat")))
            {
                using var lease = await legacyStore.AcquireAsync(token).ConfigureAwait(false);
                var data = await legacyStore.ReadAsync(token).ConfigureAwait(false);
                if (data.Accounts.Any(account => account.AccessToken.Length > 0 || account.RefreshToken.Length > 0 || account.IdToken.Length > 0))
                {
                    var state = await TransportAsync<ChatGptConnectionState>(BrokerOperation.Import, data, null, token).ConfigureAwait(false);
                    if (state.Active != data.Active || !state.Accounts.Select(account => account.Id).Order().SequenceEqual(data.Accounts.Select(account => account.Id).Order()))
                    { throw new ChatGptConnectionException("broker_migration_unconfirmed"); }
                    foreach (var account in data.Accounts) { account.ClearTokens(); }
                    await legacyStore.SaveAsync(data, CancellationToken.None).ConfigureAwait(false);
                }
            }
            Volatile.Write(ref _migrationChecked, 1);
        }
        finally { _migrationGate.Release(); }
    }

    /// <summary>Sends one request only after checking the server against SCM; cancellation closes the pipe and cancels server work.</summary>
    private async Task<T> TransportAsync<T>(BrokerOperation operation, object? payload, Action<string>? delta, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromMinutes(6));
        await using var pipe = new NamedPipeClientStream(".", BrokerProtocol.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Impersonation);
        try
        {
            await pipe.ConnectAsync(3000, deadline.Token).ConfigureAwait(false); BrokerServerIdentity.Verify(pipe.SafePipeHandle);
            await BrokerProtocol.WriteAsync(pipe, new BrokerRequest(operation, BrokerProtocol.Payload(payload)), deadline.Token).ConfigureAwait(false);
            int events = 0;
            while (++events <= 100000)
            {
                var reply = await BrokerProtocol.ReadAsync<BrokerReply>(pipe, deadline.Token).ConfigureAwait(false);
                if (reply.State is { } state) { Update(state); }
                switch (reply.Kind)
                {
                    case "delta": delta?.Invoke(reply.Payload.GetString() ?? ""); break;
                    case "browser":
                        if (operation != BrokerOperation.SignIn) { throw new InvalidDataException("broker_event_invalid"); }
                        var uri = new Uri(reply.Payload.GetString() ?? "");
                        if (uri.Scheme != "https" || uri.Host != "auth.openai.com" || uri.AbsolutePath != "/api/accounts/authorize"
                            || uri.Query.Contains("id_token_hint", StringComparison.OrdinalIgnoreCase)) { throw new InvalidDataException("broker_browser_invalid"); }
                        using (Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true })) { }
                        break;
                    case "error": throw new ChatGptConnectionException(reply.Error, reply.HttpStatus);
                    case "result": return reply.Payload.Deserialize<T>() ?? throw new InvalidDataException("broker_result_invalid");
                    default: throw new InvalidDataException("broker_event_invalid");
                }
            }
            throw new InvalidDataException("broker_event_limit");
        }
        catch (ChatGptConnectionException) { Volatile.Write(ref _available, 0); throw; }
        catch (Exception error) when (error is IOException or TimeoutException or UnauthorizedAccessException or JsonException or InvalidOperationException or UriFormatException or Win32Exception or OperationCanceledException)
        {
            Volatile.Write(ref _available, 0); token.ThrowIfCancellationRequested();
            string code = error is UnauthorizedAccessException && error.Message.StartsWith("broker_identity_", StringComparison.Ordinal) ? error.Message
                : error is OperationCanceledException ? "timeout" : "broker_required";
            throw new ChatGptConnectionException(code);
        }
    }

    /// <summary>Caches only safe account selection and plan availability metadata.</summary>
    private void Update(ChatGptConnectionState state)
    {
        Volatile.Write(ref _registration, state.Active?.ToString("N") ?? "");
        Volatile.Write(ref _available, state.Status == "plan_authorized" && state.IncludedPlanOnlyConfirmed ? 1 : 0);
    }

    /// <inheritdoc />
    public void Dispose() => _migrationGate.Dispose();
}

/// <summary>Fixed probe arguments; included-plan confirmation remains an explicit native owner decision.</summary>
internal sealed record BrokerProbe(string Model, bool IncludedPlanOnlyConfirmed);
