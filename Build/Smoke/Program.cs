using System.Globalization;
using System.Text.Json;
using Laura.Core.Abstractions;
using Laura.Core.Configuration;
using Laura.Core.Conversation;
using Laura.Core.Memory;
using Laura.Core.Models;
using Laura.Core.Skills;
using Laura.Platform.Windows.Conversation;
using Laura.Platform.Windows.System;
using Laura.Platform.Windows.Speech;
using Microsoft.Extensions.Logging.Abstractions;

// Explicit smoke tests use isolated storage and never open the microphone.
if (args.Length > 0 && args[0] == "--process-fixture") { return CommandSmoke.RunFixture(args[1..]); }
if (args.Length > 0 && args[0] == "--sandbox-fixture")
{
    try { return await SandboxSmoke.RunFixtureAsync(args[1..]); }
    catch (Exception failure) { Console.Error.WriteLine("fixture.failure:" + failure.GetType().Name + ":" + failure.HResult); return 2; }
}
if (args.Length > 0 && args[0] == "--window-fixture") { return ApplicationCloseSmoke.RunFixture(args.Length > 1 && args[1] == "hold"); }
if (args.Length == 2 && args[0] == "--inspect-gui-policy")
{
    using var image = File.OpenRead(args[1]);
    Console.WriteLine(JsonSerializer.Serialize(new { Metadata = Laura.Core.Files.ExecutableMetadata.Read(image), Automatic = WindowsApplicationLaunchPolicy.CanOpenAutomatically(args[1], null) }));
    return 0;
}
if (args.Length == 0) { Console.Error.WriteLine("Usage: Laura.Smoke <isolated-directory> <local, credential, or native smoke mode>"); return 2; }
string directory = Path.GetFullPath(args[0]);
if (args.Contains("--local-pack-install", StringComparer.Ordinal)) { return await LocalRuntimeSmoke.InstallAsync(directory); }
if (args.Contains("--migrate-owner-settings", StringComparer.Ordinal)) { return await LocalRuntimeSmoke.MigrateOwnerSettingsAsync(); }
if (args.Contains("--install-owner-local-pack", StringComparer.Ordinal)) { return await LocalRuntimeSmoke.InstallAsync(directory, true); }
if (args.Contains("--configure-ready-profile", StringComparer.Ordinal)) { return await LocalRuntimeSmoke.ConfigureReadyProfileAsync(directory); }
if (args.Contains("--offline-pipeline", StringComparer.Ordinal)) { return await LocalRuntimeSmoke.RunAsync(directory); }
if (args.Contains("--restore-missing-chatgpt-registration", StringComparer.Ordinal))
{ int credential = Array.IndexOf(args, "--protected-key-directory"); if (credential < 0 || credential + 1 >= args.Length) { return 2; } return await ReliabilitySmoke.RestoreMissingRegistrationAsync(directory, Path.GetFullPath(args[credential + 1])); }
if (args.Contains("--reliability-synthetic", StringComparer.Ordinal)) { return await ReliabilitySmoke.RunSyntheticAsync(directory); }
if (args.Contains("--brain-read-benchmark", StringComparer.Ordinal)) { return await ReliabilitySmoke.RunBrainBenchmarkAsync(directory); }
if (args.Contains("--cross-volume-edit-probe", StringComparer.Ordinal)) { return await ReliabilitySmoke.RunCrossVolumeEditAsync(directory); }
if (args.Contains("--credential-broker-live", StringComparer.Ordinal)) { return await CredentialBrokerLiveSmoke.RunAsync(directory, args); }
if (args.Contains("--credential-broker-synthetic", StringComparer.Ordinal)) { return await CredentialBrokerSmoke.RunAsync(directory); }
if (args.Contains("--chatgpt-synthetic", StringComparer.Ordinal)) { return await ChatGptSmoke.RunSyntheticAsync(directory); }
if (args.Contains("--chatgpt-login", StringComparer.Ordinal) || args.Contains("--chatgpt-probe-model", StringComparer.Ordinal)) { return await ChatGptSmoke.RunLiveAsync(directory, args); }
Directory.CreateDirectory(directory);
using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
using var settings = new SettingsService(new IniSettingsStore(Path.Combine(directory, "settings.ini"), NullLogger<IniSettingsStore>.Instance), NullLogger<SettingsService>.Instance);
await settings.LoadAsync(timeout.Token);
var memory = new SqliteConversationMemory(Path.Combine(directory, "memory.db"));
memory.Initialize();
if (args.Contains("--restore-gemini-from-marklv", StringComparer.Ordinal))
{
    int sourceIndex = Array.IndexOf(args, "--restore-gemini-from-marklv");
    if (sourceIndex + 1 >= args.Length) { Console.Error.WriteLine("A source file is required."); return 2; }
    string source = Path.GetFullPath(args[sourceIndex + 1]);
    var keys = new ProtectedApiKeyStore(directory);
    var gemini = new GeminiLiveClient(settings, keys, memory, [], new RecoveryShell());
    var importer = new MarkLvConfigurationImporter(keys, settings, gemini);
    await importer.ImportAsync(source, timeout.Token);
    Console.WriteLine("Gemini credential validated and stored. Light uses Gemini; local model is disabled.");
    return 0;
}
if (args.Contains("--gemini-live-smoke", StringComparer.Ordinal))
{
    var keys = new ProtectedApiKeyStore(directory);
    var gemini = new GeminiLiveClient(settings, keys, memory, [], new RecoveryShell());
    if (!gemini.IsAvailable) { Console.Error.WriteLine("Gemini is not configured."); return 2; }
    long audioBytes = 0;
    gemini.AudioOutputReceived += (_, bytes) => Interlocked.Add(ref audioBytes, bytes.Length);
    string? answer = await gemini.AskAsync(new ConversationTurn(
        "Responda em português brasileiro com uma frase curta: Laura está falando pela voz do Gemini.",
        CultureInfo.GetCultureInfo("pt-BR"), "Owner") { TurnId = Guid.NewGuid() }, timeout.Token);
    if (string.IsNullOrWhiteSpace(answer) || audioBytes < 48000 || gemini.PlaybackMetrics is not { ReadBytes: > 48000 })
    { Console.Error.WriteLine("Gemini Live text/audio failed: " + gemini.LastFailure?.Kind); return 1; }
    long nativeBytes = audioBytes;
    await gemini.SpeakAsync(Laura.Core.Speech.SpokenTextFormatter.Format(answer), timeout.Token);
    if (audioBytes != nativeBytes)
    { Console.Error.WriteLine("Gemini spoke the same reply twice."); return 1; }
    Console.WriteLine("Gemini Live text and native audio completed on the output device.");
    return 0;
}
if (args.Contains("--application-close-smoke", StringComparer.Ordinal))
{ await ApplicationCloseSmoke.RunAsync(timeout.Token); return 0; }
if (args.Contains("--command-smoke", StringComparer.Ordinal))
{ await CommandSmoke.RunAsync(directory, timeout.Token); return 0; }
if (args.Contains("--command-jobs-runtime-smoke", StringComparer.Ordinal))
{ await CommandJobsSmoke.RunAsync(directory, timeout.Token); return 0; }
if (args.Contains("--command-jobs-sdk-smoke", StringComparer.Ordinal))
{ await CommandJobsSdkSmoke.RunAsync(directory, timeout.Token); return 0; }
if (args.Contains("--sandbox-probe", StringComparer.Ordinal))
{ await SandboxSmoke.RunAsync(directory, timeout.Token); return 0; }
if (args.Contains("--powershell-smoke", StringComparer.Ordinal))
{ await PowerShellSmoke.RunAsync(directory, timeout.Token); return 0; }
if (args.Contains("--permission-autonomy-smoke", StringComparer.Ordinal))
{ await PermissionAutonomySmoke.RunAsync(directory, timeout.Token); return 0; }
Console.Error.WriteLine("Unknown or retired smoke mode.");
return 2;

/// <summary>Prevents a credential validation smoke from opening application windows.</summary>
file sealed class RecoveryShell : IShellController
{
    /// <inheritdoc />
    public void ShowSettings() { }
    /// <inheritdoc />
    public void ToggleSettings() { }
    /// <inheritdoc />
    public void Shutdown() { }
}
