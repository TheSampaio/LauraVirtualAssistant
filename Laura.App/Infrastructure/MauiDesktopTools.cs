using Laura.Core.Abstractions;
using Laura.Core.Files;
using Laura.Core.Skills;
using Laura.App.Platforms.Windows;

namespace Laura.App.Infrastructure;

/// <summary>Retains trusted confirmations and scoped file operations in the MAUI presentation.</summary>
public sealed class MauiDesktopTools(IUiLocalizer ui, ILocalizer conversation, UserFileWorkspace files) : IDesktopTools
{
    /// <inheritdoc />
    public async Task<string> ExecuteAsync(DesktopAction action, string? note, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(action)) { throw new ArgumentOutOfRangeException(nameof(action)); }
        if (action is DesktopAction.ReadClipboard or DesktopAction.CaptureScreen or DesktopAction.Undo)
        {
            bool approved = await WindowsConfirmation.AskAsync(ui.Get("ui.tools.confirm." + action), ui.Get("ui.yes"), ui.Get("ui.no"), cancellationToken).ConfigureAwait(false);
            if (!approved) { return conversation.Get("tools.denied"); }
        }
        cancellationToken.ThrowIfCancellationRequested();
        switch (action)
        {
            case DesktopAction.ReadClipboard:
                string text = await MainThread.InvokeOnMainThreadAsync(() => Clipboard.Default.GetTextAsync()).ConfigureAwait(false) ?? "";
                return text.Length == 0 ? conversation.Get("tools.clipboardEmpty") : text.Length > 2000 ? text[..2000] : text;
            case DesktopAction.CaptureScreen:
                byte[] png = await WindowsScreenCapture.CaptureAsync(cancellationToken).ConfigureAwait(false);
                return conversation.Get("tools.saved", files.SaveArtifact(".png", png));
            case DesktopAction.SaveNote:
                return conversation.Get("tools.saved", await Task.Run(() => files.SaveNote(note ?? ""), cancellationToken).ConfigureAwait(false));
            case DesktopAction.ListFiles:
                IReadOnlyList<string> names = await Task.Run(files.ListFiles, cancellationToken).ConfigureAwait(false);
                return names.Count == 0 ? conversation.Get("tools.filesEmpty") : string.Join(", ", names);
            case DesktopAction.Undo:
                bool undone = await Task.Run(files.Undo, cancellationToken).ConfigureAwait(false);
                return conversation.Get(undone ? "tools.undone" : "tools.nothingToUndo");
            default: throw new ArgumentOutOfRangeException(nameof(action));
        }
    }
}
