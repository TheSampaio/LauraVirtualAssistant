namespace Laura.Core.Conversation;

/// <summary>Tracks debounced hold-to-talk and edge-triggered local keyboard actions.</summary>
public sealed class ShortcutStateTracker
{
    private long? _pressedAt;
    private bool _held;
    private bool _escape;
    private bool _mute;
    private bool _fullscreen;
    /// <summary>Returns only state transitions, so repeat key presses never toggle twice.</summary>
    public ShortcutChanges Update(long now, bool chord, bool foreground, bool editing, bool escape, bool mute, bool fullscreen)
    {
        if (chord) { _pressedAt ??= now; } else { _pressedAt = null; }
        bool held = chord && now - _pressedAt!.Value >= 60;
        bool? changed = held == _held ? null : held;
        _held = held;
        var changes = new ShortcutChanges(changed, foreground && escape && !_escape,
            foreground && !editing && mute && !_mute, foreground && fullscreen && !_fullscreen);
        _escape = escape; _mute = mute; _fullscreen = fullscreen;
        return changes;
    }
}

/// <summary>Describes a single polling interval's keyboard changes.</summary>
public sealed record ShortcutChanges(bool? PushToTalk, bool Interrupt, bool ToggleMute, bool ToggleFullscreen);
