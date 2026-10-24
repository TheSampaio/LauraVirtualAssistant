using System.Text.Json;
using Laura.Core.Abstractions;
using Laura.Core.Configuration;
using Laura.Core.Diagnostics;
using Laura.Core.Memory;
using Laura.Core.Skills;
using Laura.Core.Speech;
using Laura.Core.Brain;
using Laura.Core.ChatGpt;
using Laura.Core.Conversation;
using System.Diagnostics;
using Laura.Platform.Windows.Conversation;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>Explicit Windows credential and playback probes; never opens a microphone or writes owner credentials.</summary>
internal static class ReliabilitySmoke
{
    /// <summary>Repairs only the missing nonsecret binding lost by the diagnosed settings view; keeps all other owner settings and a guarded recovery version.</summary>
    public static async Task<int> RestoreMissingRegistrationAsync(string directory, string ownerDirectory)
    {
        directory = DiagnosticStoragePolicy.RequireIsolated(directory, ownerDirectory);
        if (Process.GetProcessesByName("Laura").Length != 0) { throw new InvalidOperationException("Close Laura before repairing its settings binding."); }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        string path = Path.Combine(ownerDirectory, "settings.ini"), legacy = Path.Combine(ownerDirectory, "Credentials");
        var store = new IniSettingsStore(path, NullLogger<IniSettingsStore>.Instance);
        using var editor = new Laura.Core.Files.VersionedFileEditor(Path.Combine(ownerDirectory, "Recovery", "Registration"));
        var snapshot = await editor.ReadAsync(path, timeout.Token);
        var before = await store.LoadAsync(timeout.Token);
        using var broker = new ChatGptBrokerClient(new ChatGptCredentialStore(legacy, new WindowsPrivateDataProtector()), legacy);
        var state = await broker.ReadAsync(timeout.Token);
        if (before.ChatGpt.Registration is not null || !before.ChatGpt.Enabled || !before.ChatGpt.IncludedPlanOnly
            || state.Status != "plan_authorized" || !state.IncludedPlanOnlyConfirmed || state.Active is null
            || state.Accounts.Count(account => account.Id == state.Active && account.PlanAuthorized && account.SignedIn) != 1)
        { throw new InvalidOperationException("The narrowly scoped missing-binding recovery preconditions were not met; no settings were changed."); }
        var section = System.Text.RegularExpressions.Regex.Match(snapshot.Content, @"(?ims)^\[ChatGPT\]\r?\n.*?(?=^\[|\z)");
        var empty = System.Text.RegularExpressions.Regex.Matches(section.Value, @"(?im)^Registration[ \t]*=[ \t]*(?=\r?$)");
        if (snapshot.Truncated || !section.Success || empty.Count != 1) { throw new InvalidOperationException("An exact empty ChatGPT registration setting is required."); }
        var entry = empty.Single();
        string repaired = section.Value[..entry.Index] + "Registration=" + state.Active.Value.ToString("N") + section.Value[(entry.Index + entry.Length)..];
        var receipt = await editor.PatchAsync(path, snapshot.Hash, section.Value, repaired, timeout.Token);
        var after = await store.LoadAsync(timeout.Token);
        var confirmed = await editor.ReadAsync(path, timeout.Token);
        if (after.ChatGpt.Registration != state.Active || confirmed.Content != snapshot.Content.Replace(section.Value, repaired, StringComparison.Ordinal))
        { await editor.RollbackAsync(receipt.Id, path, timeout.Token); throw new InvalidOperationException("Settings read-back differed beyond the repaired registration; the change was rolled back."); }
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "registration-recovery.json"), JsonSerializer.Serialize(new
        { MissingBindingRestored = true, OnlyRegistrationChanged = true, PriorVersionRetainedInOwnerStorage = true, BrokerSessionUnchanged = true }), timeout.Token);
        Console.WriteLine("Missing ChatGPT settings binding restored to the existing authorized broker registration; other settings and credentials preserved.");
        return 0;
    }

    /// <summary>Verifies edit and restart rollback when receipt storage and the edited file reside on different Windows volumes.</summary>
    public static async Task<int> RunCrossVolumeEditAsync(string directory)
    {
        directory = Path.GetFullPath(directory); Directory.CreateDirectory(directory);
        string receipts = Path.Combine(Path.GetTempPath(), "Laura21Edit-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "synthetic.html");
        const string original = "<style>.layout{display:grid}</style><h1>Configuracao</h1><footer>Preserved</footer>";
        await File.WriteAllTextAsync(path, original);
        Laura.Core.Files.FileEditReceipt result;
        using (var editor = new Laura.Core.Files.VersionedFileEditor(receipts))
        { var read = await editor.ReadAsync(path); result = await editor.PatchAsync(path, read.Hash, "Configuracao", "Configuração"); }
        if (await File.ReadAllTextAsync(path) != original.Replace("Configuracao", "Configuração", StringComparison.Ordinal))
        { throw new InvalidOperationException("The scoped cross-volume edit failed."); }
        using (var restarted = new Laura.Core.Files.VersionedFileEditor(receipts)) { await restarted.RollbackAsync(result.Id, path); }
        if (await File.ReadAllTextAsync(path) != original) { throw new InvalidOperationException("Cross-volume restart rollback failed."); }
        await File.WriteAllTextAsync(Path.Combine(directory, "cross-volume-result.json"), JsonSerializer.Serialize(new
        { SourceVolume = Path.GetPathRoot(path), ReceiptVolume = Path.GetPathRoot(receipts), ScopedEdit = "Passed", RestartRollback = "Passed", BytesRestored = true }));
        foreach (string file in Directory.EnumerateFiles(receipts)) { File.Delete(file); } Directory.Delete(receipts);
        Console.WriteLine("Native cross-volume edit and restart rollback verified using synthetic HTML.");
        return 0;
    }

    /// <summary>Compares the prior full-scan read path with exact-source incremental reads on the same fixed synthetic corpus.</summary>
    public static async Task<int> RunBrainBenchmarkAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        using var settings = new SettingsService(new IniSettingsStore(Path.Combine(directory, "settings.ini"), NullLogger<IniSettingsStore>.Instance), NullLogger<SettingsService>.Instance);
        await settings.UpdateAsync(LauraSettings.Default with { Brain = new() { Directory = Path.Combine(directory, "Brain") }, GreetOnStartup = false });
        await using var brain = new BrainService(settings, new AssistantStorage(directory), new BrainIndex(Path.Combine(directory, "Cache"), new NoEmbedding()),
            path => new FileBrainStorage(path), NullLogger<BrainService>.Instance);
        await brain.InitializeAsync();
        var corpus = new List<BrainNote>();
        for (int index = 0; index < 40; index++)
        { corpus.Add(await brain.SaveAsync(new() { Path = $"Knowledge/Fixture-{index}.md", Title = $"Synthetic module {index}", Body = $"Verified fixture answer {index}.\n" + new string('x', 2048) })); }
        await Task.Delay(100); await brain.RefreshIfDirtyAsync();
        var results = new List<object>(); using var capture = LauraDiagnostics.Capture();
        foreach (bool baseline in new[] { true, false })
        {
            Guid correlation = Guid.NewGuid(); using var scope = LauraDiagnostics.Correlate(correlation);
            long started = Stopwatch.GetTimestamp();
            for (int index = 0; index < 24; index++)
            {
                BrainNote expected = corpus[index % corpus.Count]; string text;
                if (baseline)
                { await brain.RefreshAsync(); text = brain.Snapshot.Notes.Single(note => note.Id == expected.Id && note.Hash == expected.Hash).Body[..64]; }
                else { text = await brain.ReadPassageAsync(expected.Id, expected.Hash, 0, 64); }
                if (text != expected.Body[..64]) { throw new InvalidOperationException("The benchmark returned a different source passage."); }
            }
            double milliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            var samples = LauraDiagnostics.Snapshot().Where(sample => sample.InteractionId == correlation).ToArray();
            results.Add(new { Algorithm = baseline ? "Prior forced full scan per read (reproduced)" : "Incremental snapshot plus exact source hash verification", Requests = 24,
                CorrectPassages = 24, ElapsedMilliseconds = milliseconds, FullScans = samples.Count(sample => sample.Operation == "brain.scan"),
                ModelCalls = 0, TokenAndCostBenchmark = "Not performed; this benchmark measures the local Brain read path only" });
        }
        await File.WriteAllTextAsync(Path.Combine(directory, "brain-read-benchmark.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Fixed synthetic Brain read benchmark completed; exact passages verified and before/after scan counts recorded.");
        return 0;
    }

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
        restarted.Save("synthetic-replacement-not-valid-for-any-provider");
        string previous = Path.Combine(directory, "google-key.previous.dat");
        if (!File.Exists(previous) || new ProtectedApiKeyStore(directory).Read() != "synthetic-replacement-not-valid-for-any-provider")
        { throw new InvalidOperationException("Atomic protected replacement or recovery copy failed."); }
        restarted.Save("synthetic-second-replacement-not-valid-for-any-provider");
        if (!File.Exists(previous)) { throw new InvalidOperationException("Repeated protected replacement lost recovery storage."); }
        string path = Path.Combine(directory, "google-key.dat");
        await File.WriteAllBytesAsync(path, [1, 2, 3, 4]);
        var corrupt = new ProtectedApiKeyStore(directory);
        if (corrupt.Inspect() != CredentialState.DecryptionFailed || !File.Exists(path)) { throw new InvalidOperationException("Corrupt storage was hidden or removed."); }
        File.Delete(path); Directory.CreateDirectory(path);
        var unavailable = new ProtectedApiKeyStore(directory);
        if (unavailable.Inspect() != CredentialState.Unavailable) { throw new InvalidOperationException("Inaccessible storage was treated as missing."); }
        Directory.Delete(path);
        await File.WriteAllTextAsync(Path.Combine(directory, "credential-results.json"), JsonSerializer.Serialize(new
        { Missing = "Passed", ProtectedRoundTripAndRestart = "Passed", ProtectedRecoveryCopy = "Passed", DecryptionFailureRetained = "Passed", UnavailableDistinct = "Passed", RealAuthentication = "Not tested" }));
        Console.WriteLine("Windows DPAPI and distinct storage failure states verified; no real authentication was simulated.");
        return 0;
    }

    /// <summary>Disables optional embeddings so this benchmark requires no model or network.</summary>
    private sealed class NoEmbedding : IBrainEmbedding
    {
        /// <inheritdoc />
        public string ModelId => "";
        /// <inheritdoc />
        public float[] Embed(string text) => [];
    }
}
