using Laura.App.Theming;
using Laura.Core.Conversation;
using Laura.Core.Abstractions;
using Laura.App.Infrastructure;

namespace Laura.App.Controls;

/// <summary>Displays a readable message with an explicit author, source icon and quiet timestamp.</summary>
public sealed class ConversationBubble : Border
{
    private readonly Label _author;
    /// <summary>Gets the original message for language refresh and bounded history.</summary>
    public ConversationMessage Message { get; }

    /// <summary>Creates a message surface using source-specific color and alignment.</summary>
    public ConversationBubble(ConversationMessage message, string author, IUiLocalizer? ui = null)
    {
        Message = message;
        bool assistant = message.Source == ConversationMessageSource.Assistant;
        _author = Theme.Caption(author); _author.TextColor = assistant ? Theme.Accent : Theme.Muted; _author.CharacterSpacing = .8;
        var header = new Grid { ColumnDefinitions = [new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto)], ColumnSpacing = 8 };
        header.Add(Theme.Icon(assistant ? "audio_lines" : "user_round", assistant, 16)); header.Add(_author, 1);
        header.Add(Theme.Label(message.Timestamp.ToLocalTime().ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture), 10, Theme.Muted), 2);
        var copy = Theme.IconButton("copy", async () => { if (await ConversationClipboard.CopyAsync(message.Text).ConfigureAwait(true)) { _author.Text = ui?.Get("ui.chat.copied") ?? "Copied"; } });
        var copyHost = new ContentView { Content = copy, Opacity = 0 };
        Theme.DescribeAction(copy, ui?.Get("ui.chat.copy") ?? "Copy message");
        copy.Focused += (_, _) => copyHost.Opacity = 1; copy.Unfocused += (_, _) => copyHost.Opacity = 0;
        header.Add(copyHost, 3);
        var body = Theme.Label(message.Text, 14);
        body.HandlerChanged += (_, _) =>
        {
            if (body.Handler?.PlatformView is not Microsoft.UI.Xaml.Controls.TextBlock native) { return; }
            native.IsTextSelectionEnabled = true;
            var menu = new Microsoft.UI.Xaml.Controls.MenuFlyout();
            var selected = new Microsoft.UI.Xaml.Controls.MenuFlyoutItem { Text = ui?.Get("ui.chat.copySelection") ?? "Copy selection" };
            selected.Click += async (_, _) => { if (native.SelectedText.Length > 0 && await ConversationClipboard.CopyAsync(native.SelectedText).ConfigureAwait(true)) { _author.Text = ui?.Get("ui.chat.copied") ?? "Copied"; } };
            var whole = new Microsoft.UI.Xaml.Controls.MenuFlyoutItem { Text = ui?.Get("ui.chat.copy") ?? "Copy message" };
            whole.Click += async (_, _) => { if (await ConversationClipboard.CopyAsync(message.Text).ConfigureAwait(true)) { _author.Text = ui?.Get("ui.chat.copied") ?? "Copied"; } };
            var all = new Microsoft.UI.Xaml.Controls.MenuFlyoutItem { Text = ui?.Get("ui.chat.selectAll") ?? "Select all" };
            all.Click += (_, _) => native.SelectAll();
            menu.Items.Add(selected); menu.Items.Add(whole); menu.Items.Add(all); native.ContextFlyout = menu;
        };
        var content = Theme.Stack(header, body); content.Spacing = 8;
        var pointer = new PointerGestureRecognizer(); pointer.PointerEntered += (_, _) => copyHost.Opacity = 1; pointer.PointerExited += (_, _) => copyHost.Opacity = 0; content.GestureRecognizers.Add(pointer);
        Content = content; Padding = 12; StrokeThickness = 0;
        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = Theme.ControlRadius };
        BackgroundColor = assistant ? Theme.AssistantSurface : Theme.Field;
        Margin = assistant ? new Thickness(0, 0, 16, 0) : new Thickness(16, 0, 0, 0);
    }
    /// <summary>Refreshes the author label when the interface language changes.</summary>
    public void SetAuthor(string author) => _author.Text = author.ToUpperInvariant();
}
