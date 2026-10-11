using Laura.Core.Abstractions;
using Laura.Core.Text;

namespace Laura.Core.Skills.Builtin;

/// <summary>Routes explicit localized user commands to bounded desktop capabilities.</summary>
public sealed class DesktopToolSkill : ISkill
{
    private readonly ILocalizer _localizer;
    private readonly IDesktopTools _tools;

    /// <summary>Connects the existing skill system to trusted desktop operations.</summary>
    public DesktopToolSkill(ILocalizer localizer, IDesktopTools tools) { _localizer = localizer; _tools = tools; }
    /// <inheritdoc />
    public string Id => "builtin.desktopTools";
    /// <inheritdoc />
    public int Priority => 25;
    /// <inheritdoc />
    public bool CanHandle(SkillRequest request) => Match(request) is not null;
    /// <inheritdoc />
    public async Task<SkillResponse> ExecuteAsync(SkillRequest request, CancellationToken cancellationToken = default)
    {
        DesktopAction? action = Match(request);
        if (action is null) { return SkillResponse.NotHandled; }
        string? note = null;
        if (action == DesktopAction.SaveNote)
        {
            string? prefix = _localizer.GetPhrases("phrases.saveNote").FirstOrDefault(phrase =>
                request.RawText.StartsWith(phrase + " ", StringComparison.OrdinalIgnoreCase));
            if (prefix is null) { return SkillResponse.NotHandled; }
            note = request.RawText[(prefix.Length + 1)..].Trim();
        }
        string result = await _tools.ExecuteAsync(action.Value, note, cancellationToken).ConfigureAwait(false);
        return SkillResponse.Speak(result);
    }

    /// <summary>Matches whole commands, avoiding substring activation from quoted or negative text.</summary>
    private DesktopAction? Match(SkillRequest request)
    {
        (string Key, DesktopAction Action)[] commands =
        [
            ("phrases.readClipboard", DesktopAction.ReadClipboard),
            ("phrases.captureScreen", DesktopAction.CaptureScreen),
            ("phrases.listFiles", DesktopAction.ListFiles),
            ("phrases.undo", DesktopAction.Undo),
        ];
        foreach (var command in commands)
        {
            if (_localizer.GetPhrases(command.Key).Any(phrase => TextNormalizer.Normalize(phrase) == request.NormalizedText))
            {
                return command.Action;
            }
        }
        return _localizer.GetPhrases("phrases.saveNote").Any(phrase => request.RawText.StartsWith(phrase + " ", StringComparison.OrdinalIgnoreCase))
            ? DesktopAction.SaveNote : null;
    }
}
