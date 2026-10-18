namespace Laura.Core.Screenshots;

/// <summary>Capture entry point shared by shortcuts and the existing assistant tool catalog.</summary>
public enum ScreenshotMode
{
    /// <summary>Select and adjust a physical region.</summary>
    Region,
    /// <summary>Select a real top-level window.</summary>
    Window,
    /// <summary>Capture all displays or the configured current display.</summary>
    FullScreen
}
/// <summary>Persistent, explicitly configurable shortcut choices; Windows conflict checks occur on registration.</summary>
public sealed record ScreenshotOptions
{
    /// <summary>Gets whether global shortcuts are enabled.</summary>
    public bool ShortcutsEnabled { get; init; } = true;
    /// <summary>Gets the requested region shortcut.</summary>
    public string RegionShortcut { get; init; } = "Ctrl+Shift+F8";
    /// <summary>Gets the requested window shortcut.</summary>
    public string WindowShortcut { get; init; } = "Ctrl+Shift+F9";
    /// <summary>Gets the requested full-desktop shortcut.</summary>
    public string FullScreenShortcut { get; init; } = "Ctrl+Shift+F10";
    /// <summary>Gets whether full-screen means the entire virtual desktop.</summary>
    public bool AllDisplays { get; init; } = true;
    /// <summary>Bounds and validates stored chords without silently assigning a replacement.</summary>
    public ScreenshotOptions Sanitized()
    {
        // Migrate only the original default set. Individually customized choices stay explicit.
        if (string.Equals(RegionShortcut, "PrtSc", StringComparison.OrdinalIgnoreCase) && string.Equals(WindowShortcut, "Ctrl+PrtSc", StringComparison.OrdinalIgnoreCase)
            && string.Equals(FullScreenShortcut, "Shift+PrtSc", StringComparison.OrdinalIgnoreCase))
        { var defaults = new ScreenshotOptions(); return this with { RegionShortcut = defaults.RegionShortcut, WindowShortcut = defaults.WindowShortcut, FullScreenShortcut = defaults.FullScreenShortcut }; }
        return this with { RegionShortcut = Valid(RegionShortcut), WindowShortcut = Valid(WindowShortcut), FullScreenShortcut = Valid(FullScreenShortcut) };
    }
    /// <summary>Parses a finite keyboard vocabulary, rejecting bare letters and Windows-reserved modifiers.</summary>
    public static bool TryParse(string chord, out uint modifiers, out uint key)
    {
        modifiers = 0x4000; key = 0; var parts = chord.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is < 1 or > 4) { return false; }
        foreach (string part in parts[..^1])
        {
            uint flag = part.ToUpperInvariant() switch { "CTRL" => 2u, "ALT" => 1u, "SHIFT" => 4u, _ => 0u };
            if (flag == 0 || (modifiers & flag) != 0) { return false; } modifiers |= flag;
        }
        string last = parts[^1].ToUpperInvariant();
        if (last == "PRTSC") { key = 0x2c; return true; }
        if (last.StartsWith('F') && int.TryParse(last.AsSpan(1), out int function) && function is >= 1 and <= 11) { key = (uint)(0x6f + function); return true; }
        if ((modifiers & 7) != 0 && last.Length == 1 && char.IsAsciiLetter(last[0])) { key = last[0]; return true; }
        return false;
    }
    private static string Valid(string? chord) => chord is { Length: <= 40 } && TryParse(chord, out _, out _) ? chord : "";
}
