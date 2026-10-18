using System.Text.Json;
using Laura.Core.Abstractions;
using Laura.Core.Configuration;
using Laura.Core.Diagnostics;
using Laura.Core.Memory;
using Laura.Core.Skills;
using Laura.Core.Speech;
using Laura.Platform.Windows.Conversation;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>Explicit Windows credential and playback probes; never opens a microphone or writes owner credentials.</summary>
internal static class ReliabilitySmoke
{
    /// <summary>Checks DPAPI persistence, missing, corrupt and inaccessible storage in an isolated directory.</summary>
    public static async Task<int> RunSyntheticAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        var store = new ProtectedApiKeyStore(directory);
        if (store.Inspect() != CredentialState.NotConfigured) { throw new InvalidOperationException("Use a fresh isolated credential directory."); }
        store.Save("synthetic-credential-not-valid-for-any-provider");
        var restarted = new ProtectedApiKeyStore(directory);
        if (restarted.Inspect() != CredentialState.Stored || restarted.Read() != "synthetic-credential-not-valid-for-any-provider")
        { throw new InvalidOperationException("DPAPI persistence failed."); }
        string path = Path.Combine(directory, "google-key.dat");
        await File.WriteAllBytesAsync(path, [1, 2, 3, 4]);
        var corrupt = new ProtectedApiKeyStore(directory);
        if (corrupt.Inspect() != CredentialState.DecryptionFailed || !File.Exists(path)) { throw new InvalidOperationException("Corrupt storage was hidden or removed."); }
        File.Delete(path); Directory.CreateDirectory(path);
        var unavailable = new ProtectedApiKeyStore(directory);
        if (unavailable.Inspect() != CredentialState.Unavailable) { throw new InvalidOperationException("Inaccessible storage was treated as missing."); }
        Directory.Delete(path);
        await File.WriteAllTextAsync(Path.Combine(directory, "credential-results.json"), JsonSerializer.Serialize(new
        { Missing = "Passed", ProtectedRoundTripAndRestart = "Passed", DecryptionFailureRetained = "Passed", UnavailableDistinct = "Passed", RealAuthentication = "Not tested" }));
        Console.WriteLine("Windows DPAPI and distinct storage failure states verified; no real authentication was simulated.");
        return 0;
    }

    /// <summary>Uses the existing owner Gemini authorization for synthetic read-aloud at zero volume, with real device telemetry.</summary>
    public static async Task<int> RunVoiceAsync(string directory, string credentialDirectory)
    {
        Directory.CreateDirectory(directory);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var ownerStore = new IniSettingsStore(Path.Combine(credentialDirectory, "settings.ini"), NullLogger<IniSettingsStore>.Instance);
        LauraSettings owner = await ownerStore.LoadAsync(timeout.Token);
        using var settings = new SettingsService(new IniSettingsStore(Path.Combine(directory, "settings.ini"), NullLogger<IniSettingsStore>.Instance), NullLogger<SettingsService>.Instance);
        await settings.UpdateAsync(LauraSettings.Default with { Culture = "pt-BR", ProviderSelection = ConversationProviderSelection.ChatGpt,
            GenerativeAi = owner.GenerativeAi with { Enabled = true }, OutputDevice = owner.OutputDevice, Voice = owner.Voice with { Volume = 0 }, GreetOnStartup = false }, timeout.Token);
        var memory = new SqliteConversationMemory(Path.Combine(directory, "conversation.db")); memory.Initialize();
        await using var live = new GeminiLiveClient(settings, new ProtectedApiKeyStore(credentialDirectory), memory, Array.Empty<ISkill>(), new SmokeShell());
        using var capture = LauraDiagnostics.Capture();
        Guid response = Guid.NewGuid();
        try
        {
            await live.SpeakAsync("Teste de reprodução da Laura. Dois mais dois são quatro.", timeout.Token, response);
            SpeechPlayback result = live.LastSpeechPlayback ?? throw new InvalidOperationException("No playback evidence was recorded.");
            if (result.ResponseId != response || result.State != SpeechPlaybackState.Completed || result.ReceivedBytes <= 0
                || result.QueuedBytes != result.ReceivedBytes || live.IsListening)
            { throw new InvalidOperationException("Playback did not meet native completion requirements."); }
            Console.WriteLine("Real Gemini read-aloud completed on the Windows output device with ChatGPT selected, zero volume and no microphone.");
            return 0;
        }
        finally
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "voice-results.json"), JsonSerializer.Serialize(new
            { Playback = live.LastSpeechPlayback, ConfiguredVoice = settings.Current.GenerativeAi.LiveVoice, Volume = 0,
                MicrophoneOpened = live.IsListening, AcousticAudibility = "Not tested", Diagnostics = LauraDiagnostics.Snapshot() }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
