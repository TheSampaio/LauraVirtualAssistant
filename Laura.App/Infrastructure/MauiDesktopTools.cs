using Laura.Core.Abstractions;
using Laura.Core.Files;
using Laura.Core.Skills;
using Laura.App.Platforms.Windows;
using Laura.Core.Permissions;

namespace Laura.App.Infrastructure;

/// <summary>Retains trusted confirmations and scoped file operations in the MAUI presentation.</summary>
public sealed class MauiDesktopTools(IUiLocalizer ui, ILocalizer conversation, UserFileWorkspace files, PermissionManager permissions, ScreenshotCoordinator screenshots, ISettingsService settings) : IDesktopTools
{
    /// <inheritdoc />
    public async Task<string> ExecuteAsync(DesktopAction action, string? note, CancellationToken cancellationToken)
    {
        using var provider = new Laura.Core.Conversation.ProviderExecutionScope(Laura.Core.Conversation.ProviderExecutionScope.Provider ?? settings.Current.GenerativeAi.Provider);
        if (!Enum.IsDefined(action)) { throw new ArgumentOutOfRangeException(nameof(action)); }
        PermissionOperation operation = action switch
        {
            DesktopAction.ReadClipboard => PermissionOperation.ClipboardCloud, DesktopAction.CaptureScreen => PermissionOperation.Internal,
            DesktopAction.SaveNote => PermissionOperation.WorkspaceNote, DesktopAction.Undo => PermissionOperation.Undo,
            DesktopAction.ListFiles => PermissionOperation.Internal, _ => PermissionOperation.Unknown
        };
        try { return await permissions.ExecuteAsync(new(operation, "Local desktop command", ui.Get("ui.tools.confirm." + action),
            resources: action == DesktopAction.SaveNote ? [files.RootDirectory] : [],
            parameters: note ?? "", destination: action == DesktopAction.ReadClipboard ? MauiToolPermissions.CloudDestination : ""),
            token => ExecuteAuthorizedAsync(action, note, token), cancellationToken).ConfigureAwait(false); }
        catch (PermissionDeniedException) { return conversation.Get("tools.denied"); }
    }

    private async Task<string> ExecuteAuthorizedAsync(DesktopAction action, string? note, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        switch (action)
        {
            case DesktopAction.ReadClipboard:
                string text = await MainThread.InvokeOnMainThreadAsync(() => Clipboard.Default.GetTextAsync()).ConfigureAwait(false) ?? "";
                return text.Length == 0 ? conversation.Get("tools.clipboardEmpty") : text.Length > 2000 ? text[..2000] : text;
            case DesktopAction.CaptureScreen:
                return await screenshots.CaptureAsync(Laura.Core.Screenshots.ScreenshotMode.Region, false, cancellationToken).ConfigureAwait(false)
                    ? ui.Get("ui.screenshot.opened") : ui.Get("ui.screenshot.cancelled");
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
