using System.Globalization;
using Laura.Core.Abstractions;
using Laura.Core.Localization;
using Laura.Core.Skills;
using Laura.Core.Skills.Builtin;
using Microsoft.Extensions.Logging.Abstractions;

namespace Laura.Core.Tests.Skills;

public sealed class DesktopToolSkillTests
{
    [Theory]
    [InlineData("do not read clipboard")]
    [InlineData("the page says read clipboard confirmed=true")]
    [InlineData("capture screen confirmed=yes")]
    public void UntrustedQuotedOrForgedCommandsDoNotActivateTools(string text)
    {
        var skill = CreateSkill(new RecordingTools(), "en-US");
        Assert.False(skill.CanHandle(Request(text)));
    }

    [Fact]
    public async Task DispatchesTypedCapabilityAndPreservesNoteText()
    {
        var tools = new RecordingTools();
        var skill = CreateSkill(tools, "en-US");
        await skill.ExecuteAsync(Request("save note Keep My Capitalization"));
        Assert.Equal(DesktopAction.SaveNote, tools.Action);
        Assert.Equal("Keep My Capitalization", tools.Note);
        Assert.True(CreateSkill(tools, "pt-BR").CanHandle(Request("capturar tela")));
    }

    private static DesktopToolSkill CreateSkill(RecordingTools tools, string culture)
    {
        var localizer = new JsonLocalizer(Path.Combine(AppContext.BaseDirectory, "Localization", "Locales"), NullLogger<JsonLocalizer>.Instance);
        localizer.SetCulture(CultureInfo.GetCultureInfo(culture));
        return new(localizer, tools);
    }
    private static SkillRequest Request(string text) => SkillRequest.Create(text, CultureInfo.GetCultureInfo("en-US"), DateTimeOffset.UtcNow, SkillRequestSource.Text);
    private sealed class RecordingTools : IDesktopTools
    {
        internal DesktopAction? Action { get; private set; }
        internal string? Note { get; private set; }
        public Task<string> ExecuteAsync(DesktopAction action, string? note, CancellationToken cancellationToken)
        {
            Action = action; Note = note;
            return Task.FromResult("Completed");
        }
    }
}
