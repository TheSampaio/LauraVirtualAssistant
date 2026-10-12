using Laura.App.Theming;
using Laura.Core.Conversation;

namespace Laura.App.Controls;

/// <summary>Displays a readable message with an explicit author, source icon and quiet timestamp.</summary>
public sealed class ConversationBubble : Border
{
    private readonly Label _author;
    /// <summary>Gets the original message for language refresh and bounded history.</summary>
    public ConversationMessage Message { get; }

    /// <summary>Creates a message surface using source-specific color and alignment.</summary>
    public ConversationBubble(ConversationMessage message, string author)
    {
        Message = message;
        bool assistant = message.Source == ConversationMessageSource.Assistant;
        _author = Theme.Caption(author); _author.TextColor = assistant ? Theme.Accent : Theme.Muted; _author.CharacterSpacing = .8;
        var header = new Grid { ColumnDefinitions = [new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto)], ColumnSpacing = 8 };
        header.Add(Theme.Icon(assistant ? "audio_lines" : "user_round", assistant, 16)); header.Add(_author, 1);
        header.Add(Theme.Label(message.Timestamp.ToLocalTime().ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture), 10, Theme.Muted), 2);
        var content = Theme.Stack(header, Theme.Label(message.Text, 14)); content.Spacing = 8;
        Content = content; Padding = 12; StrokeThickness = 0;
        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = Theme.ControlRadius };
        BackgroundColor = assistant ? Theme.AssistantSurface : Theme.Field;
        Margin = assistant ? new Thickness(0, 0, 16, 0) : new Thickness(16, 0, 0, 0);
    }
    /// <summary>Refreshes the author label when the interface language changes.</summary>
    public void SetAuthor(string author) => _author.Text = author.ToUpperInvariant();
}
