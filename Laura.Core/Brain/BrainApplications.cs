using System.Security.Cryptography;
using Laura.Core.Abstractions;
using Laura.Core.Permissions;
using Laura.Core.Text;
using Laura.Core.Skills;
using Microsoft.Extensions.Logging;

namespace Laura.Core.Brain;

/// <summary>An observed executable reference stored as knowledge, never as an execution authorization.</summary>
public sealed record BrainApplicationReference(string Name, string Target, string Hash);

/// <summary>Distinguishes unknown applications from known but unsafe, obsolete or ambiguous knowledge, so discovery cannot silently override it.</summary>
public sealed record BrainApplicationResolution(bool Known, BrainApplicationReference? Reference, string Reason = "");

/// <summary>Reuses actual process-launch observations while validating local identity; every launch still requires the platform permission gate.</summary>
public sealed class BrainApplications(BrainService brain, ISettingsService settings, AssistantStorage storage, ILogger<BrainApplications> logger) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private long _failedObservations;
    /// <summary>Gets knowledge-write failures separately from actual native launch results.</summary>
    public long FailedObservations => Interlocked.Read(ref _failedObservations);
    /// <summary>Gets the current knowledge location for pinning an observation during native approval.</summary>
    public string Location => brain.Location;
    /// <summary>Attempts knowledge persistence without converting a successful native action into a failed action.</summary>
    public async Task<BrainNote?> TryRememberAsync(string name, string alias, ProcessLaunchResult launch, string expectedVault, CancellationToken cancellationToken = default)
    {
        try { return await RememberAsync(name, alias, launch, expectedVault, cancellationToken).ConfigureAwait(false); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or PermissionDeniedException)
        { Interlocked.Increment(ref _failedObservations); logger.LogWarning("Application reference learning unavailable ({ErrorType}).", error.GetType().Name); return null; }
    }
    /// <summary>Finds a unique exact title/alias and revalidates the learned executable before returning a hint. Invalid references fall back to discovery.</summary>
    public async Task<BrainApplicationReference?> FindAsync(string name, CancellationToken cancellationToken = default)
        => (await ResolveAsync(name, cancellationToken).ConfigureAwait(false)).Reference;
    /// <summary>Resolves declared or observed knowledge uniquely and validates a bounded local executable before the original launch permission gate.</summary>
    public async Task<BrainApplicationResolution> ResolveAsync(string name, CancellationToken cancellationToken = default)
    {
        if (!settings.Current.RememberConversations || name.Length is 0 or > 120) { return new(false, null); }
        try { await brain.RefreshIfDirtyAsync(cancellationToken).ConfigureAwait(false); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException) { return new(false, null); }
        string normalized = TextNormalizer.Normalize(name);
        BrainNote[] matches = brain.Snapshot.Notes.Where(note => note.IsKnowledge && note.Type == "application" && note.Application is not null
            && (TextNormalizer.Normalize(note.Title) == normalized || MatchExecutableName(note.Title, name) >= 100
                || note.Aliases.Any(alias => TextNormalizer.Normalize(alias) == normalized))
            && !brain.Snapshot.Issues.Any(issue => issue.Path == note.Path && issue.Code == "conflicting_id")).Take(2).ToArray();
        if (matches.Length == 0) { return new(false, null); }
        if (matches.Length != 1) { return new(true, null, "Stored application aliases are ambiguous; clarify the intended application."); }
        if (matches[0].Verification == "unverified" || brain.Snapshot.Edges.Any(edge => edge.Type == "contradicts" && (edge.Source == matches[0].Id || edge.Target == matches[0].Id)))
        { return new(true, null, "Stored installation evidence is unverified or contradictory; request clarification."); }
        BrainApplicationReference reference = matches[0].Application!;
        try
        {
            string target = reference.Target;
            if (reference.Hash.Length == 0)
            {
                ValidateLocalTarget(target);
                if (!target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    string[] files = await Task.Run(() => Directory.EnumerateFiles(target, "*.exe", SearchOption.TopDirectoryOnly).Take(33).ToArray(), cancellationToken).ConfigureAwait(false);
                    if (files.Length > 32) { return new(true, null, "Installation folder exceeds the bounded lookup; request the exact executable."); }
                    var ranked = files.Select(file => (File: file, Score: MatchExecutableName(Path.GetFileNameWithoutExtension(file), matches[0].Title)))
                        .Where(item => item.Score >= 85).OrderByDescending(item => item.Score).ToArray();
                    if (ranked.Length == 0 || ranked.Length > 1 && ranked[0].Score == ranked[1].Score)
                    { return new(true, null, "No unique matching executable exists in the declared folder; request the exact executable."); }
                    target = ranked[0].File;
                }
            }
            string identity = await ReadIdentityAsync(target, cancellationToken).ConfigureAwait(false);
            if (identity.Length != 64 || reference.Hash.Length > 0 && !identity.Equals(reference.Hash, StringComparison.OrdinalIgnoreCase))
            { return new(true, null, "Known executable is missing or changed; no speculative fallback was attempted."); }
            return new(true, new(matches[0].Title, target, identity));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or PermissionDeniedException or ArgumentException)
        { return new(true, null, "Declared installation cannot be safely validated; request a corrected local path."); }
    }
    /// <summary>Records only actual native process creation for a bounded executable identity; shell acceptance alone cannot create a verified reference.</summary>
    public async Task<BrainNote?> RememberAsync(string name, string alias, ProcessLaunchResult launch, string expectedVault, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(launch);
        if (!settings.Current.RememberConversations || !settings.Current.Brain.AutoLearn || !launch.Accepted || launch.ProcessId is not > 0
            || launch.TargetHash.Length != 64 || name.Length is < 2 or > 120 || alias.Length > 120 || BrainLearning.Sensitive(name + " " + alias)) { return null; }
        if (!(await ReadIdentityAsync(launch.Target, cancellationToken).ConfigureAwait(false)).Equals(launch.TargetHash, StringComparison.OrdinalIgnoreCase)) { return null; }
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await brain.InitializeAsync(cancellationToken).ConfigureAwait(false); string key = "application." + FileBrainStorage.Hash(TextNormalizer.Normalize(name))[..20];
            StringComparison pathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            BrainNote? prior = brain.Snapshot.Notes.SingleOrDefault(note => note.IsKnowledge && note.Type == "application" && note.Application?.Target.Equals(launch.Target, pathComparison) == true);
            prior ??= brain.Snapshot.Notes.SingleOrDefault(note => note.IsKnowledge && note.Type == "application" && note.Key == key);
            var aliases = (prior?.Aliases ?? []).Concat(alias.Length > 0 ? [alias, name] : [name]).Where(value => value.Length <= 120).Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToArray();
            if (prior?.Application is { } existing && existing.Target.Equals(launch.Target, pathComparison) && existing.Hash == launch.TargetHash && prior.Aliases.SequenceEqual(aliases)) { return prior; }
            string canonicalName = prior?.Title ?? name;
            var note = prior ?? new BrainNote { Path = "Knowledge/Applications/" + BrainNaming.FileName(name, key), Key = key, Title = name, Type = "application",
                Source = "native_process_creation", Verification = "observed_execution", Confidence = 1, Importance = .6, Tags = ["application"] };
            return await brain.SaveAtLocationAsync(note with { Source = "native_process_creation", Verification = "observed_execution", Confidence = 1,
                Aliases = aliases, Application = new(canonicalName, launch.Target, launch.TargetHash),
                Body = "# " + canonicalName + "\n\nWindows reported actual process creation using this local executable.\nThis reference grants no permission and does not prove window readiness.\nArguments are not retained; each new launch revalidates the target and requires authorization.\n" }, expectedVault, cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }
    private async Task<string> ReadIdentityAsync(string path, CancellationToken cancellationToken)
    {
        if (!Path.IsPathFullyQualified(path) || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) { return ""; }
        ValidateLocalTarget(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length > 64 * 1024 * 1024) { return ""; }
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
    }
    private void ValidateLocalTarget(string path)
    {
        if (!Path.IsPathFullyQualified(path)) { throw new ArgumentException("Local absolute target required."); }
        var action = new PermissionAction(PermissionOperation.Process, "Observed application reference", "Validate executable identity", [path]);
        PermissionPolicy.ValidateResources(action, [storage.Directory, brain.Location], Path.Combine(storage.Directory, "Workspace"));
    }
    private static int MatchExecutableName(string candidate, string name) =>
        TextNormalizer.Normalize(candidate).Replace(" ", "", StringComparison.Ordinal) == TextNormalizer.Normalize(name).Replace(" ", "", StringComparison.Ordinal)
            ? 100 : ApplicationNameMatcher.Score(candidate, name);
    /// <inheritdoc />
    public void Dispose() { _gate.Dispose(); GC.SuppressFinalize(this); }
}
