using System.Security.Cryptography;
using System.Text.Json;
using Laura.Core.Abstractions;
using Laura.Core.Permissions;

namespace Laura.Platform.Windows.Conversation;

/// <summary>Stores isolated OAuth registrations atomically under DPAPI; callers must hold the cross-process lease.</summary>
public sealed class ChatGptCredentialStore(string directory, IPrivateDataProtector protector)
{
    private readonly string _directory = Path.GetFullPath(directory);
    private const int MaximumBytes = 2 * 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new() { MaxDepth = 8 };
    /// <summary>Serializes sign-in, selection, refresh and revocation across processes. Returns: Exclusive lease released by disposal.</summary>
    internal async Task<FileStream> AcquireAsync(CancellationToken token)
    {
        Validate(); Directory.CreateDirectory(_directory);
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try { return new FileStream(Path.Combine(_directory, "chatgpt-credentials.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { await Task.Delay(100, token).ConfigureAwait(false); }
        }
    }
    /// <summary>Reads one bounded protected store, preserving a corrupt original instead of resetting its host identity.</summary>
    internal async Task<ChatGptCredentials> ReadAsync(CancellationToken token)
    {
        Validate(); string path = Path.Combine(_directory, "chatgpt-credentials.dat");
        if (!File.Exists(path))
        {
            var fresh = new ChatGptCredentials { Host = "urn:uuid:" + Guid.NewGuid().ToString("D") };
            await SaveAsync(fresh, token).ConfigureAwait(false); return fresh;
        }
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, FileOptions.Asynchronous);
        if (input.Length is <= 0 or > MaximumBytes) { throw new InvalidDataException("credential_store_invalid"); }
        byte[] encrypted = new byte[(int)input.Length]; await input.ReadExactlyAsync(encrypted, token).ConfigureAwait(false);
        byte[] plaintext = protector.Unprotect(encrypted);
        try
        {
            if (plaintext.Length > MaximumBytes) { throw new InvalidDataException("credential_store_invalid"); }
            var data = JsonSerializer.Deserialize<ChatGptCredentials>(plaintext, Options) ?? throw new InvalidDataException("credential_store_invalid");
            if (data.Host is null || !data.Host.StartsWith("urn:uuid:", StringComparison.Ordinal) || !Guid.TryParse(data.Host[9..], out _) || data.Accounts is null || data.Accounts.Count > 16
                || data.PendingClient is null || data.PendingClient.Length > 256 || data.PendingClient.Length > 0 && !data.PendingClient.StartsWith("oaiapp_", StringComparison.Ordinal)
                || data.Accounts.Any(account => account is null || account.Id == Guid.Empty || account.Client is null || !account.Client.StartsWith("oaiapp_", StringComparison.Ordinal)
                    || account.Client.Length > 256 || account.Issuer != "https://auth.openai.com" || account.Subject is null || account.Subject.Length is < 1 or > 512
                    || account.Email is null || account.AccessToken is null || account.RefreshToken is null || account.IdToken is null
                    || account.Scopes is null || account.Scopes.Length > 32 || account.Scopes.Any(scope => scope is null || scope.Length > 256)
                    || account.Email.Length > 512 || account.AccessToken.Length > 65536 || account.RefreshToken.Length > 65536 || account.IdToken.Length > 65536)
                || data.Accounts.Select(account => account.Id).Distinct().Count() != data.Accounts.Count
                || data.Active is { } active && !data.Accounts.Any(account => account.Id == active))
            { throw new InvalidDataException("credential_store_invalid"); }
            return data;
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }
    /// <summary>Replaces all rotating-token fields together using protected write-through temporary storage.</summary>
    internal async Task SaveAsync(ChatGptCredentials data, CancellationToken token)
    {
        Validate(); Directory.CreateDirectory(_directory);
        byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(data, Options); byte[] encrypted;
        try
        {
            if (plaintext.Length > MaximumBytes - 4096) { throw new InvalidDataException("credential_store_limit"); }
            encrypted = protector.Protect(plaintext);
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
        string temporary = Path.Combine(_directory, "chatgpt-credentials.dat." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, FileOptions.Asynchronous | FileOptions.WriteThrough))
            { await output.WriteAsync(encrypted, token).ConfigureAwait(false); await output.FlushAsync(token).ConfigureAwait(false); output.Flush(true); }
            token.ThrowIfCancellationRequested(); Validate(); File.Move(temporary, Path.Combine(_directory, "chatgpt-credentials.dat"), true);
        }
        finally { if (File.Exists(temporary)) { File.Delete(temporary); } }
    }
    /// <summary>Rejects redirected or network credential paths before accessing protected data.</summary>
    private void Validate() => PermissionPolicy.ValidateResources(new(PermissionOperation.FileWrite, "ChatGPT credential storage", "Protect private credentials",
        [_directory, Path.Combine(_directory, "chatgpt-credentials.dat"), Path.Combine(_directory, "chatgpt-credentials.lock")]), []);
}

/// <summary>Internal protected envelope; never registered as a model-visible dependency.</summary>
internal sealed class ChatGptCredentials
{
    public string Host { get; set; } = "";
    public Guid? Active { get; set; }
    public string PendingClient { get; set; } = "";
    public List<ChatGptRegistration> Accounts { get; set; } = [];
    /// <summary>Prevents accidental disclosure when formatting an internal envelope.</summary>
    public override string ToString() => "ChatGPT protected credentials (redacted)";
}

/// <summary>A single registration keeps its issued client, validated identity and rotating tokens together.</summary>
internal sealed class ChatGptRegistration
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Client { get; set; } = "";
    public string Issuer { get; set; } = "https://auth.openai.com";
    public string Subject { get; set; } = "";
    public string Email { get; set; } = "";
    public string AccessToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public string IdToken { get; set; } = "";
    public string[] Scopes { get; set; } = [];
    public DateTimeOffset Expires { get; set; }
    public DateTimeOffset? EarliestRefresh { get; set; }
    public string VerifiedModel { get; set; } = "";
    public bool IncludedPlanOnlyConfirmed { get; set; }
    /// <summary>Gets permission from the token response, never from callback scope or identity alone.</summary>
    public bool PlanAuthorized => Scopes.Contains("chatgpt.tokens.use.direct", StringComparer.Ordinal);
    /// <summary>Clears tokens while retaining this exact registration for later reauthorization.</summary>
    public void ClearTokens() { AccessToken = ""; RefreshToken = ""; IdToken = ""; Scopes = []; Expires = default; EarliestRefresh = null; VerifiedModel = ""; IncludedPlanOnlyConfirmed = false; }
    /// <summary>Prevents accidental token disclosure by logging or debugger interpolation.</summary>
    public override string ToString() => "ChatGPT registration (redacted)";
}
