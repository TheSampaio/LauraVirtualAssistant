using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Laura.Core.ChatGpt;
using Laura.Platform.Windows.Conversation;

namespace Laura.CredentialBroker;

/// <summary>Local SID-partitioned credential transport; all native tools remain outside this process.</summary>
internal static class BrokerHost
{
    /// <summary>Accepts bounded local interactive clients using an explicit pipe ACL. Args: root: Administrator-provisioned data root; token: Service lifetime.</summary>
    internal static async Task RunAsync(string root, CancellationToken token)
    {
        VerifyRoot(root);
        var security = new PipeSecurity(); security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
        foreach (var identity in new[] { WellKnownSidType.LocalServiceSid, WellKnownSidType.LocalSystemSid, WellKnownSidType.BuiltinAdministratorsSid })
        { security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(identity, null), PipeAccessRights.FullControl, AccessControlType.Allow)); }
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.InteractiveSid, null), PipeAccessRights.ReadWrite, AccessControlType.Allow));
        using var slots = new SemaphoreSlim(16, 16);
        var sessions = new Dictionary<string, BrokerSession>(StringComparer.Ordinal);
        var workers = new List<Task>(); bool first = true;
        try
        {
            while (!token.IsCancellationRequested)
            {
                await slots.WaitAsync(token).ConfigureAwait(false);
                NamedPipeServerStream? pipe = null;
                try
                {
                    pipe = NamedPipeServerStreamAcl.Create(BrokerProtocol.PipeName, PipeDirection.InOut, 16, PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous | (first ? PipeOptions.FirstPipeInstance : PipeOptions.None), 8192, 8192, security);
                    first = false; await pipe.WaitForConnectionAsync(token).ConfigureAwait(false);
                    string sid = "";
                    pipe.RunAsClient(() =>
                    {
                        using var identity = WindowsIdentity.GetCurrent(true);
                        if (identity is not { IsAuthenticated: true, User: { } user } || user.IsWellKnown(WellKnownSidType.AnonymousSid))
                        { throw new UnauthorizedAccessException("broker_peer_invalid"); }
                        sid = user.Value;
                    });
                    if (!sessions.TryGetValue(sid, out var session))
                    {
                        if (sessions.Count >= 32) { throw new UnauthorizedAccessException("broker_user_limit"); }
                        session = new BrokerSession(Path.Combine(root, sid)); sessions.Add(sid, session);
                    }
                    var owned = pipe; pipe = null;
                    workers.RemoveAll(task => task.IsCompleted);
                    workers.Add(HandleAsync(owned, session, slots, token));
                }
                catch (Exception error) when (error is UnauthorizedAccessException or IOException && !token.IsCancellationRequested)
                { if (pipe is null) { throw; } }
                finally { if (pipe is not null) { await pipe.DisposeAsync().ConfigureAwait(false); slots.Release(); } }
            }
        }
        finally
        {
            await Task.WhenAll(workers).ConfigureAwait(false);
            foreach (var session in sessions.Values) { session.Dispose(); }
        }
    }

    /// <summary>Checks that medium-integrity clients cannot write the credential root or redirect service-owned paths.</summary>
    private static void VerifyRoot(string root)
    {
        using var identity = WindowsIdentity.GetCurrent();
        if (identity.User?.IsWellKnown(WellKnownSidType.LocalServiceSid) != true || !Directory.Exists(root))
        { throw new UnauthorizedAccessException("broker_installation_required"); }
        for (DirectoryInfo? directory = new(root); directory is not null; directory = directory.Parent)
        { if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) { throw new UnauthorizedAccessException("broker_root_redirected"); } }
        var rules = new DirectoryInfo(root).GetAccessControl().GetAccessRules(true, true, typeof(SecurityIdentifier));
        foreach (FileSystemAccessRule rule in rules)
        {
            if (rule.AccessControlType != AccessControlType.Allow || (rule.FileSystemRights & (FileSystemRights.WriteData | FileSystemRights.AppendData | FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership)) == 0) { continue; }
            var sid = (SecurityIdentifier)rule.IdentityReference;
            if (!sid.IsWellKnown(WellKnownSidType.LocalServiceSid) && !sid.IsWellKnown(WellKnownSidType.LocalSystemSid) && !sid.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid))
            { throw new UnauthorizedAccessException("broker_root_acl_invalid"); }
        }
    }

    /// <summary>Processes exactly one request and cancels the model or login when the owning client disconnects.</summary>
    private static async Task HandleAsync(NamedPipeServerStream pipe, BrokerSession session, SemaphoreSlim slots, CancellationToken token)
    {
        await using (pipe.ConfigureAwait(false))
        using (var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token))
        {
            lifetime.CancelAfter(TimeSpan.FromMinutes(6));
            bool acquired = false; Task? watch = null;
            try
            {
                using var header = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); header.CancelAfter(TimeSpan.FromSeconds(10));
                var request = await BrokerProtocol.ReadAsync<BrokerRequest>(pipe, header.Token).ConfigureAwait(false);
                watch = WatchDisconnectAsync(pipe, lifetime);
                if (request.Operation is BrokerOperation.Disconnect or BrokerOperation.Select) { session.Connection.CancelInference(); }
                await session.Gate.WaitAsync(lifetime.Token).ConfigureAwait(false); acquired = true;
                session.Launch = (uri, ct) => BrokerProtocol.WriteAsync(pipe, new BrokerReply("browser", BrokerProtocol.Payload(uri.AbsoluteUri)), ct);
                object result = await session.ExecuteAsync(request, text => BrokerProtocol.WriteAsync(pipe,
                    new BrokerReply("delta", BrokerProtocol.Payload(text)), lifetime.Token).GetAwaiter().GetResult(), lifetime.Token).ConfigureAwait(false);
                var state = await session.Connection.ReadAsync(lifetime.Token).ConfigureAwait(false);
                await BrokerProtocol.WriteAsync(pipe, new BrokerReply("result", BrokerProtocol.Payload(result), State: state), lifetime.Token).ConfigureAwait(false);
            }
            catch (Exception error) when (error is ChatGptConnectionException or JsonException or InvalidDataException or IOException or UnauthorizedAccessException or OperationCanceledException or ArgumentException or InvalidOperationException)
            {
                string code = error is ChatGptConnectionException known ? known.Code : error is OperationCanceledException ? "canceled" : "broker_request_invalid";
                if (!lifetime.IsCancellationRequested)
                {
                    try { await BrokerProtocol.WriteAsync(pipe, new BrokerReply("error", BrokerProtocol.Payload(false), code,
                        (error as ChatGptConnectionException)?.HttpStatus), lifetime.Token).ConfigureAwait(false); }
                    catch (IOException) { }
                }
            }
            finally
            {
                if (acquired) { session.Launch = null; session.Gate.Release(); }
                await lifetime.CancelAsync().ConfigureAwait(false);
                if (watch is not null) { await watch.ConfigureAwait(false); }
                slots.Release();
            }
        }
    }

    /// <summary>A disconnected or protocol-writing client loses its in-flight authorization immediately.</summary>
    private static async Task WatchDisconnectAsync(Stream pipe, CancellationTokenSource lifetime)
    {
        try { await pipe.ReadAsync(new byte[1], lifetime.Token).ConfigureAwait(false); await lifetime.CancelAsync().ConfigureAwait(false); }
        catch (Exception error) when (error is IOException or OperationCanceledException or ObjectDisposedException)
        { await lifetime.CancelAsync().ConfigureAwait(false); }
    }
}

/// <summary>Serializes one authenticated Windows user's registrations independently from other users.</summary>
internal sealed class BrokerSession : IDisposable
{
    private readonly ChatGptCredentialStore _store;
    private readonly ChatGptIdTokenValidator _validator;
    private readonly HttpClient _http, _identityHttp;
    internal SemaphoreSlim Gate { get; } = new(1, 1);
    internal ChatGptConnection Connection { get; }
    internal Func<Uri, CancellationToken, Task>? Launch { get; set; }

    /// <summary>Builds service-local adapters; browser opening is delegated to the foreground native app without retained ID tokens.</summary>
    internal BrokerSession(string directory)
    {
        _store = new ChatGptCredentialStore(directory, new WindowsPrivateDataProtector());
        _http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
        _identityHttp = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
        _validator = new ChatGptIdTokenValidator(_identityHttp);
        Connection = new ChatGptConnection(_http, _store, new ChatGptBrowserAuthorization((uri, token) =>
            Launch?.Invoke(uri, token) ?? throw new InvalidOperationException("broker_browser_unavailable")), _validator, includeIdentityHint: false);
    }

    /// <summary>Dispatches a closed set of credential operations without accepting paths, endpoints, scripts or grants.</summary>
    internal async Task<object> ExecuteAsync(BrokerRequest request, Action<string> delta, CancellationToken token) => request.Operation switch
    {
        BrokerOperation.Read => await Connection.ReadAsync(token).ConfigureAwait(false),
        BrokerOperation.SignIn => await Connection.SignInAsync(request.Payload.Deserialize<Guid?>(), token).ConfigureAwait(false),
        BrokerOperation.Select => await Connection.SelectAsync(request.Payload.Deserialize<Guid>(), token).ConfigureAwait(false),
        BrokerOperation.Models => await Connection.ModelsAsync(token).ConfigureAwait(false),
        BrokerOperation.Probe => await ProbeAsync(request.Payload.Deserialize<BrokerProbe>() ?? throw new InvalidDataException(), token).ConfigureAwait(false),
        BrokerOperation.Disconnect => await Connection.DisconnectAsync(token).ConfigureAwait(false),
        BrokerOperation.Complete => await Connection.CompleteAsync(request.Payload.Deserialize<ChatGptInferenceRequest>() ?? throw new InvalidDataException(), delta, token).ConfigureAwait(false),
        BrokerOperation.Import => await ImportAsync(request.Payload, token).ConfigureAwait(false),
        _ => throw new InvalidDataException("broker_operation_invalid")
    };

    /// <summary>Runs only the fixed connection probe with an explicit included-plan confirmation.</summary>
    private Task<ChatGptProbeResult> ProbeAsync(BrokerProbe probe, CancellationToken token) => Connection.ProbeAsync(probe.Model, probe.IncludedPlanOnlyConfirmed, token);

    /// <summary>Accepts a one-way migration into an empty registration store; a retried import cannot overwrite rotating service tokens.</summary>
    private async Task<ChatGptConnectionState> ImportAsync(JsonElement payload, CancellationToken token)
    {
        bool reauthorization = false;
        using (await _store.AcquireAsync(token).ConfigureAwait(false))
        {
            var current = await _store.ReadAsync(token).ConfigureAwait(false);
            var imported = payload.Deserialize<ChatGptCredentials>() ?? throw new InvalidDataException("broker_migration_invalid");
            ChatGptCredentialStore.ValidateData(imported);
            if (current.Accounts.Count > 0)
            {
                if (current.Host != imported.Host || !current.Accounts.Select(account => account.Id).Order().SequenceEqual(imported.Accounts.Select(account => account.Id).Order()))
                { throw new ChatGptConnectionException("broker_migration_conflict"); }
            }
            else
            {
                // Verify account ownership against the signed official ID token before accepting any legacy renewal token.
                foreach (var account in imported.Accounts)
                {
                    if (account.AccessToken.Length == 0 && account.RefreshToken.Length == 0 && account.IdToken.Length == 0) { continue; }
                    try
                    {
                        var identity = await _validator.ValidateAsync(account.IdToken, account.Client, null, token).ConfigureAwait(false);
                        if (identity.Subject != account.Subject) { throw new ChatGptConnectionException("account_identity_mismatch"); }
                    }
                    catch (InvalidDataException error) when (error.Message is "oidc_token_invalid" or "oidc_identity_invalid")
                    {
                        // Expired or invalid identity is never accepted as a live authorization. Preserve only its local registration label for native reauthorization.
                        account.ClearTokens(); reauthorization = true;
                    }
                }
                await _store.SaveAsync(imported, token).ConfigureAwait(false);
                await _store.ReadAsync(token).ConfigureAwait(false);
            }
        }
        var result = await Connection.ReadAsync(token).ConfigureAwait(false);
        return reauthorization ? result with { Error = "broker_migration_reauthorization_required" } : result;
    }

    /// <inheritdoc />
    public void Dispose() { Connection.Dispose(); _validator.Dispose(); _http.Dispose(); _identityHttp.Dispose(); Gate.Dispose(); }
}
