using Laura.Core.Abstractions;
using Laura.Core.Localization;
using Laura.Core.Brain;

namespace Laura.Core.Skills.Builtin;

/// <summary>
/// Opens known applications from commands like "open the calculator".
///
/// The catalog maps a technical target to a language key; the spoken aliases live in
/// the language file, so adding another way to ask for the same application requires
/// no recompilation.
/// </summary>
public sealed class LaunchApplicationSkill : PhraseSkillBase
{
    private static readonly IReadOnlyList<ApplicationTarget> Catalog =
    [
        new("application.notepad", "notepad.exe"),
        new("application.calculator", "calc.exe"),
        new("application.explorer", "explorer.exe"),
        new("application.taskManager", "taskmgr.exe"),
        new("application.paint", "mspaint.exe"),
        new("application.terminal", "wt.exe", IsUri: false, Fallback: "powershell.exe"),
        new("application.browser", "https://www.google.com", IsUri: true),
        new("application.windowsSettings", "ms-settings:", IsUri: true),
    ];

    private readonly IProcessLauncher _launcher;
    private readonly BrainApplications? _applications;

    /// <summary>
    /// Initializes the skill.
    ///
    /// Args:
    ///     localizer: Source of the prefixes, aliases and responses.
    ///     launcher: Service that starts processes and opens addresses.
    /// </summary>
    public LaunchApplicationSkill(ILocalizer localizer, IProcessLauncher launcher, BrainApplications? applications = null)
        : base(localizer)
    {
        ArgumentNullException.ThrowIfNull(launcher);
        _launcher = launcher;
        _applications = applications;
    }

    /// <inheritdoc />
    public override string Id => "builtin.launchApplication";

    /// <inheritdoc />
    public override int Priority => 60;

    /// <inheritdoc />
    protected override string PhrasesKey => LocalizationKeys.Skills.LaunchPrefixes;

    /// <inheritdoc />
    public override async Task<SkillResponse> ExecuteAsync(
        SkillRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!PhraseMatcher.TryMatchPrefix(
                request.NormalizedText,
                Localizer.GetPhrases(PhrasesKey),
                out string applicationName)
            || string.IsNullOrWhiteSpace(applicationName))
        {
            return SkillResponse.NotHandled;
        }

        ApplicationTarget? target = FindTarget(applicationName);

        if (target is null)
        {
            ProcessLaunchResult result = await LaunchNamedAsync(applicationName, null, cancellationToken).ConfigureAwait(false);
            return SkillResponse.Speak(Localizer.Get(
                result.Accepted ? LocalizationKeys.Skills.LaunchResponse : result.StopFallback ? LocalizationKeys.Skills.LaunchStopped : LocalizationKeys.Skills.LaunchNotFound, applicationName));
        }

        ProcessLaunchResult launched = await LaunchAsync(target, applicationName, cancellationToken).ConfigureAwait(false);
        return SkillResponse.Speak(Localizer.Get(launched.Accepted ? LocalizationKeys.Skills.LaunchResponse : launched.StopFallback ? LocalizationKeys.Skills.LaunchStopped : LocalizationKeys.Skills.LaunchNotFound, applicationName));
    }

    /// <summary>
    /// Finds the catalog application whose alias appears in the command.
    ///
    /// Args:
    ///     spokenName: The portion of the command after the open prefix.
    ///
    /// Returns:
    ///     The matching target, or <see langword="null"/> when no known alias
    ///     appears in the text.
    /// </summary>
    private ApplicationTarget? FindTarget(string spokenName) => Catalog
        .FirstOrDefault(target => PhraseMatcher.MatchesAny(spokenName, Localizer.GetPhrases(target.AliasKey)));

    /// <summary>
    /// Launches the target, falling back to the alternative when the main one fails.
    ///
    /// Args:
    ///     target: Application to open.
    ///
    /// Returns:
    ///     <see langword="true"/> when the system agreed to open the application.
    /// </summary>
    private async Task<ProcessLaunchResult> LaunchAsync(ApplicationTarget target, string requestedName, CancellationToken token)
    {
        if (target.IsUri)
        {
            return new(await _launcher.TryOpenAsync(new Uri(target.Target), token).ConfigureAwait(false), target.Target);
        }

        ProcessLaunchResult result = await LaunchNamedAsync(target.Target, requestedName, token).ConfigureAwait(false);
        if (result.Accepted || result.StopFallback || target.Fallback is null) { return result; }
        return await LaunchNamedAsync(target.Fallback, requestedName, token).ConfigureAwait(false);
    }
    private async Task<ProcessLaunchResult> LaunchNamedAsync(string name, string? requestedName, CancellationToken token)
    {
        BrainApplicationReference? reference = _applications is null ? null : await _applications.FindAsync(name, token).ConfigureAwait(false);
        string? vault = _applications?.Location;
        ProcessLaunchResult result = await _launcher.TryStartObservedAsync(reference?.Target ?? name, expectedHash: reference?.Hash, cancellationToken: token).ConfigureAwait(false);
        if (result.Accepted && _applications is not null && vault is not null)
        {
            await _applications.TryRememberAsync(reference?.Name ?? requestedName ?? name, requestedName ?? name, result, vault, token).ConfigureAwait(false);
        }
        return result;
    }
}
