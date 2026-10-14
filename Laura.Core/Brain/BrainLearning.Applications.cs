using System.Text.RegularExpressions;
using Laura.Core.Text;

namespace Laura.Core.Brain;

public sealed partial class BrainLearning
{
    /// <summary>Stores a literal user-declared installation location as inert knowledge; neither file existence nor execution is implied.</summary>
    private async Task<BrainMemoryResult> LearnApplicationAsync(string name, string target, string evidence, string change, string targetId, string vault, CancellationToken token)
    {
        if (name.Length is < 2 or > 80 || target.Length is < 3 or > 500 || !evidence.Contains(name, StringComparison.Ordinal)
            || !evidence.Contains(target, StringComparison.Ordinal) || !Path.IsPathFullyQualified(target) || target.StartsWith("\\\\", StringComparison.Ordinal)
            || target.StartsWith("//", StringComparison.Ordinal) || target.IndexOf(':', 2) >= 0 || Sensitive(name + " " + target)
            || change is not ("new" or "correction" or "contradiction" or "unverified"))
        { return new("rejected", Reason: "Application knowledge requires a bounded literal name and local absolute location; no permission is granted."); }
        string key = "application." + FileBrainStorage.Hash(TextNormalizer.Normalize(name))[..20];
        await _consolidation.WaitAsync(token).ConfigureAwait(false);
        try
        {
            await brain.InitializeAsync(token).ConfigureAwait(false);
            BrainNote[] matches = brain.Snapshot.Notes.Where(note => note.IsKnowledge && note.Key == key).Take(2).ToArray();
            if (matches.Length > 1) { return new("rejected", Reason: "Conflicting application identities require review."); }
            BrainNote? prior = matches.SingleOrDefault();
            if (change == "correction" && (prior is null || prior.Id != targetId)) { return new("rejected", Reason: "Correction requires the actual current application ID."); }
            if (prior?.Application?.Target.Equals(target, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) == true)
            { return new("repeated", prior.Id); }
            var note = new BrainNote { Key = key, Title = name, Type = "application", Path = "Knowledge/Applications/" + BrainNaming.FileName(name, key),
                Body = evidence, Source = "user_application_declaration", Verification = change == "unverified" ? "unverified" : "user_reported", Confidence = .8, Importance = .7,
                Application = new(name, target, ""), Aliases = [name], Tags = ["application", "installation"] };
            BrainNote saved = await ConsolidateCoreAsync(note, change == "correction", vault, token).ConfigureAwait(false);
            return new(saved.Status == "draft" ? "contradiction" : prior is null ? "new" : "correction", saved.Id,
                "Declared installation location only; executable identity and launch require independent native validation and permission.");
        }
        finally { _consolidation.Release(); }
    }
    /// <summary>Recognizes only explicit installation declarations; arbitrary operational natural language may use grounded proposals.</summary>
    private async Task<bool> LearnApplicationDeclarationAsync(string text, string vault, CancellationToken token)
    {
        Match match = ApplicationDeclaration().Match(text.Trim());
        if (!match.Success) { return false; }
        string name = match.Groups["name"].Value.Trim(), target = match.Groups["path"].Value.Trim().TrimEnd('.');
        bool correction = match.Groups["correction"].Success;
        string key = "application." + FileBrainStorage.Hash(TextNormalizer.Normalize(name))[..20];
        string id = brain.Snapshot.Notes.FirstOrDefault(note => note.IsKnowledge && note.Key == key)?.Id ?? "";
        await LearnApplicationAsync(name, target, text.Trim(), correction ? "correction" : "new", id, vault, token).ConfigureAwait(false);
        return true;
    }
    [GeneratedRegex("^(?:Laura[, ]+)?(?:(?<correction>Corre[cç][aã]o|Correction):\\s*)?(?:o |a |the )?(?<name>[^:\\r\\n]{2,80}?)\\s+(?:est[aá]\\s+instalad[oa]\\s+em|is\\s+installed\\s+(?:at|in))\\s+[\"']?(?<path>(?:[a-zA-Z]:[\\\\/]|/)[^\\r\\n\"']{2,500})[\"']?\\.?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 100)]
    private static partial Regex ApplicationDeclaration();
}
