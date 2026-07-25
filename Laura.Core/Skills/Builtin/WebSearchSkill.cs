using System.Globalization;
using System.Text;
using Laura.Core.Abstractions;
using Laura.Core.Localization;

namespace Laura.Core.Skills.Builtin;

/// <summary>
/// Opens a web search in the default browser.
/// </summary>
public sealed class WebSearchSkill : PhraseSkillBase
{
    private static readonly CompositeFormat SearchUrlFormat =
        CompositeFormat.Parse("https://www.google.com/search?q={0}");

    private readonly IProcessLauncher _launcher;

    /// <summary>
    /// Initializes the skill.
    ///
    /// Args:
    ///     localizer: Source of the prefixes and the responses.
    ///     launcher: Service that opens the default browser.
    /// </summary>
    public WebSearchSkill(ILocalizer localizer, IProcessLauncher launcher)
        : base(localizer)
    {
        ArgumentNullException.ThrowIfNull(launcher);
        _launcher = launcher;
    }

    /// <inheritdoc />
    public override string Id => "builtin.webSearch";

    /// <inheritdoc />
    public override int Priority => 50;

    /// <inheritdoc />
    protected override string PhrasesKey => LocalizationKeys.Skills.SearchPrefixes;

    /// <inheritdoc />
    public override Task<SkillResponse> ExecuteAsync(
        SkillRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!PhraseMatcher.TryMatchPrefix(request.NormalizedText, Localizer.GetPhrases(PhrasesKey), out string query))
        {
            return Task.FromResult(SkillResponse.NotHandled);
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            return Task.FromResult(SkillResponse.Speak(
                Localizer.Get(LocalizationKeys.Skills.SearchMissingQuery)));
        }

        var searchUri = new Uri(string.Format(
            CultureInfo.InvariantCulture,
            SearchUrlFormat,
            Uri.EscapeDataString(query)));

        return Task.FromResult(_launcher.TryOpen(searchUri)
            ? SkillResponse.Speak(Localizer.Get(LocalizationKeys.Skills.SearchResponse, query))
            : SkillResponse.Speak(Localizer.Get(LocalizationKeys.Assistant.SkillFailed)));
    }
}
