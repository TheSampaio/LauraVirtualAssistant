using Laura.Core.Abstractions;
using Laura.Core.Localization;

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

    /// <summary>
    /// Initializes the skill.
    ///
    /// Args:
    ///     localizer: Source of the prefixes, aliases and responses.
    ///     launcher: Service that starts processes and opens addresses.
    /// </summary>
    public LaunchApplicationSkill(ILocalizer localizer, IProcessLauncher launcher)
        : base(localizer)
    {
        ArgumentNullException.ThrowIfNull(launcher);
        _launcher = launcher;
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
            return SkillResponse.Speak(Localizer.Get(
                await _launcher.TryStartAsync(applicationName, cancellationToken: cancellationToken).ConfigureAwait(false) ? LocalizationKeys.Skills.LaunchResponse : LocalizationKeys.Skills.LaunchNotFound, applicationName));
        }

        return await LaunchAsync(target, cancellationToken).ConfigureAwait(false)
            ? SkillResponse.Speak(Localizer.Get(LocalizationKeys.Skills.LaunchResponse, applicationName))
            : SkillResponse.Speak(Localizer.Get(LocalizationKeys.Skills.LaunchNotFound, applicationName));
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
    private async Task<bool> LaunchAsync(ApplicationTarget target, CancellationToken token)
    {
        if (target.IsUri)
        {
            return await _launcher.TryOpenAsync(new Uri(target.Target), token).ConfigureAwait(false);
        }

        return await _launcher.TryStartAsync(target.Target, cancellationToken: token).ConfigureAwait(false)
            || (target.Fallback is not null && await _launcher.TryStartAsync(target.Fallback, cancellationToken: token).ConfigureAwait(false));
    }
}
