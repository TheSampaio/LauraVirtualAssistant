using System.Drawing.Imaging;
using Laura.Core.Abstractions;
using Laura.Core.Files;
using Laura.Core.Skills;

namespace Laura.App.Infrastructure;

/// <summary>Trusted UI authorization for clipboard, screen capture and scoped reversible file operations.</summary>
public sealed class WinFormsDesktopTools : IDesktopTools
{
    private readonly IUiDispatcher _dispatcher;
    private readonly IUiLocalizer _ui;
    private readonly ILocalizer _conversation;
    private readonly UserFileWorkspace _files;

    /// <summary>Connects trusted UI authorization and the dedicated file workspace.</summary>
    public WinFormsDesktopTools(IUiDispatcher dispatcher, IUiLocalizer ui, ILocalizer conversation, UserFileWorkspace files)
    {
        _dispatcher = dispatcher; _ui = ui; _conversation = conversation; _files = files;
    }

    /// <inheritdoc />
    public async Task<string> ExecuteAsync(DesktopAction action, string? note, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(action)) { throw new ArgumentOutOfRangeException(nameof(action)); }
        if (action is DesktopAction.ReadClipboard or DesktopAction.CaptureScreen or DesktopAction.Undo)
        {
            bool approved = await OnUiAsync(() => MessageBox.Show(_ui.Get("ui.tools.confirm." + action), "Laura",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes,
                cancellationToken).ConfigureAwait(false);
            if (!approved) { return _conversation.Get("tools.denied"); }
        }
        cancellationToken.ThrowIfCancellationRequested();
        switch (action)
        {
            case DesktopAction.ReadClipboard:
                string text = await OnUiAsync(() => Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty, cancellationToken).ConfigureAwait(false);
                return text.Length == 0 ? _conversation.Get("tools.clipboardEmpty") : text.Length > 2000 ? text[..2000] : text;
            case DesktopAction.CaptureScreen:
                Rectangle bounds = await OnUiAsync(() => Screen.PrimaryScreen?.Bounds ?? throw new InvalidOperationException("No display is available."), cancellationToken).ConfigureAwait(false);
                string path = await Task.Run(() =>
                {
                    using var bitmap = new Bitmap(bounds.Width, bounds.Height);
                    using (Graphics graphics = Graphics.FromImage(bitmap)) { graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size); }
                    using var png = new MemoryStream();
                    bitmap.Save(png, ImageFormat.Png);
                    cancellationToken.ThrowIfCancellationRequested();
                    return _files.SaveArtifact(".png", png.ToArray());
                }, cancellationToken).ConfigureAwait(false);
                return _conversation.Get("tools.saved", path);
            case DesktopAction.ListFiles:
                IReadOnlyList<string> names = await Task.Run(_files.ListFiles, cancellationToken).ConfigureAwait(false);
                return names.Count == 0 ? _conversation.Get("tools.filesEmpty") : string.Join(", ", names);
            case DesktopAction.SaveNote:
                string saved = await Task.Run(() => _files.SaveNote(note ?? string.Empty), cancellationToken).ConfigureAwait(false);
                return _conversation.Get("tools.saved", saved);
            case DesktopAction.Undo:
                bool undone = await Task.Run(_files.Undo, cancellationToken).ConfigureAwait(false);
                return _conversation.Get(undone ? "tools.undone" : "tools.nothingToUndo");
            default: throw new ArgumentOutOfRangeException(nameof(action));
        }
    }

    /// <summary>Executes trusted approval/clipboard operations on the UI thread; cancellation never grants authority.</summary>
    private Task<T> OnUiAsync<T>(Func<T> action, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _dispatcher.Post(() =>
        {
            if (cancellationToken.IsCancellationRequested) { completion.TrySetCanceled(cancellationToken); return; }
            try
            {
                T result = action();
                if (cancellationToken.IsCancellationRequested) { completion.TrySetCanceled(cancellationToken); }
                else { completion.TrySetResult(result); }
            }
            catch (Exception exception) { completion.TrySetException(exception); }
        });
        return completion.Task.WaitAsync(cancellationToken);
    }
}
