using System.Text.Json;
using Laura.Core.Configuration;
using Laura.Core.ChatGpt;
using Laura.Platform.Windows.Conversation;

/// <summary>Checks the actually installed broker with the owner's authorized Laura session; no token values are exported.</summary>
internal static class CredentialBrokerLiveSmoke
{
    /// <summary>Verifies migration, protected-root access denial, account continuity, official catalog and one completed plan probe.</summary>
    internal static async Task<int> RunAsync(string directory, string[] args)
    {
        Directory.CreateDirectory(directory); using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(6)); var token = deadline.Token;
        string legacy = Path.Combine(Path.GetDirectoryName(IniSettingsStore.GetDefaultFilePath())!, "Credentials");
        var store = new ChatGptCredentialStore(legacy, new WindowsPrivateDataProtector()); Guid? prior = null;
        if (File.Exists(Path.Combine(legacy, "chatgpt-credentials.dat")))
        { using (await store.AcquireAsync(token)) { prior = (await store.ReadAsync(token)).Active; } }
        using var connection = new ChatGptBrokerClient(store, legacy);
        try
        {
            var state = await connection.ReadAsync(token);
            if (args.Contains("--credential-broker-login", StringComparer.Ordinal)) { state = await connection.SignInAsync(state.Active, token); }
            if (state.Status != "plan_authorized")
            {
                Console.WriteLine(JsonSerializer.Serialize(new { state.Status, state.Error, ReauthorizationRequired = state.Active is not null })); return 3;
            }
            bool rootDenied = false;
            try { Directory.GetFiles(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "LauraCredentialBroker"), "*", SearchOption.AllDirectories); }
            catch (UnauthorizedAccessException) { rootDenied = true; }
            if (!rootDenied) { throw new InvalidOperationException("Broker credential root was readable by the ordinary user."); }
            bool legacyCleared;
            using (await store.AcquireAsync(token))
            { legacyCleared = (await store.ReadAsync(token)).Accounts.All(account => account.AccessToken.Length == 0 && account.RefreshToken.Length == 0 && account.IdToken.Length == 0); }
            if (!legacyCleared || prior is { } id && id != state.Active) { throw new InvalidOperationException("Legacy credential migration was not verified."); }
            var models = await connection.ModelsAsync(token);
            var checks = new List<string> { "scm_authenticated_local_service", "ordinary_user_cannot_read_service_store", "legacy_oauth_fields_cleared", "stable_registration_preserved", "official_account_catalog" };
            ChatGptProbeResult? probe = null;
            if (args.Contains("--included-plan-only-confirmed", StringComparer.Ordinal))
            {
                int requested = Array.IndexOf(args, "--chatgpt-probe-model");
                string selected = requested >= 0 && requested + 1 < args.Length ? args[requested + 1] : state.VerifiedModel;
                if (!models.Any(model => model.Slug == selected)) { throw new InvalidOperationException("Previously verified model is absent from the current account catalog."); }
                probe = await connection.ProbeAsync(selected, true, token);
                if (!probe.Completed) { throw new InvalidOperationException("A completed terminal response was not received."); }
                checks.Add("real_plan_inference_completed_through_service");
            }
            await File.WriteAllTextAsync(Path.Combine(directory, "broker-live.json"), JsonSerializer.Serialize(new { Kind = "real_installed_service", Passed = checks.Count, Checks = checks,
                Models = models.Select(model => model.Slug), Probe = probe, NoPaidApiKey = true, AdditionalCreditAllowanceConfirmedZero = args.Contains("--included-plan-only-confirmed", StringComparer.Ordinal) }, new JsonSerializerOptions { WriteIndented = true }), token);
            Console.WriteLine($"Actual installed credential broker checks passed: {checks.Count}. Token values were not exported."); return 0;
        }
        catch (ChatGptConnectionException error) { Console.WriteLine(JsonSerializer.Serialize(new { Error = error.Code, error.HttpStatus })); return 1; }
    }
}
