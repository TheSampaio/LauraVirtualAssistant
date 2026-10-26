using Microsoft.Maui.Handlers;

namespace Laura.App.Platforms.Windows;

/// <summary>Prevents native style mappings after MAUI has disconnected an image button.</summary>
public sealed class WindowsImageButtonHandler : ImageButtonHandler
{
    /// <summary>
    /// Applies property changes only while the native button exists.
    /// Args: property identifies the virtual property to map.
    /// </summary>
    public override void UpdateValue(string property)
    {
        // MAUI 10 clears PlatformView before resetting focus; that reset can still change visual-state setters.
        if (((IElementHandler)this).PlatformView is not null) { base.UpdateValue(property); }
    }
}
