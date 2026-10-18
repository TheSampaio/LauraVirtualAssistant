namespace Laura.Core.Configuration;

/// <summary>Merges a native settings draft without overwriting concurrent changes to untouched fields.</summary>
public static class SettingsDraft
{
    /// <summary>Applies only fields edited since the page loaded. Args: baseline: Loaded state; edited: UI draft; current: Latest state. Returns: Merged state.</summary>
    public static LauraSettings Merge(LauraSettings baseline, LauraSettings edited, LauraSettings current) => current with
    {
        Culture = Choose(baseline.Culture, edited.Culture, current.Culture),
        UiCulture = Choose(baseline.UiCulture, edited.UiCulture, current.UiCulture),
        UserNickname = Choose(baseline.UserNickname, edited.UserNickname, current.UserNickname),
        WeatherLocation = Choose(baseline.WeatherLocation, edited.WeatherLocation, current.WeatherLocation),
        MicrophoneMuted = Choose(baseline.MicrophoneMuted, edited.MicrophoneMuted, current.MicrophoneMuted),
        RememberConversations = Choose(baseline.RememberConversations, edited.RememberConversations, current.RememberConversations),
        ClapWakeEnabled = Choose(baseline.ClapWakeEnabled, edited.ClapWakeEnabled, current.ClapWakeEnabled),
        ClapWakeSensitivity = Choose(baseline.ClapWakeSensitivity, edited.ClapWakeSensitivity, current.ClapWakeSensitivity),
        InputDevice = Choose(baseline.InputDevice, edited.InputDevice, current.InputDevice),
        OutputDevice = Choose(baseline.OutputDevice, edited.OutputDevice, current.OutputDevice),
        GreetOnStartup = Choose(baseline.GreetOnStartup, edited.GreetOnStartup, current.GreetOnStartup),
        AnnounceHourly = Choose(baseline.AnnounceHourly, edited.AnnounceHourly, current.AnnounceHourly),
        StartWithWindows = Choose(baseline.StartWithWindows, edited.StartWithWindows, current.StartWithWindows),
        Permissions = Choose(baseline.Permissions, edited.Permissions, current.Permissions),
        Screenshots = Choose(baseline.Screenshots, edited.Screenshots, current.Screenshots),
        Voice = Choose(baseline.Voice, edited.Voice, current.Voice),
        GenerativeAi = Choose(baseline.GenerativeAi, edited.GenerativeAi, current.GenerativeAi),
        ChatGpt = Choose(baseline.ChatGpt, edited.ChatGpt, current.ChatGpt),
        ProviderSelection = Choose(baseline.ProviderSelection, edited.ProviderSelection, current.ProviderSelection),
    };

    /// <summary>Keeps concurrent values when the corresponding UI field was untouched.</summary>
    private static T Choose<T>(T baseline, T edited, T current) => EqualityComparer<T>.Default.Equals(baseline, edited) ? current : edited;
}
