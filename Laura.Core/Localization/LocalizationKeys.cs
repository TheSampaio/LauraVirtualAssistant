namespace Laura.Core.Localization;

/// <summary>
/// Language-file keys, centralized so a typo becomes a compile error instead of a
/// phrase missing in production.
/// </summary>
public static class LocalizationKeys
{
    /// <summary>Messages spoken by the engine itself, outside any skill.</summary>
    public static class Assistant
    {
        /// <summary>Short acknowledgement right after the wake word.</summary>
        public const string Acknowledge = "assistant.acknowledge";

        /// <summary>Response when no skill recognizes the command.</summary>
        public const string NotUnderstood = "assistant.notUnderstood";

        /// <summary>Notice that voice listening could not be started.</summary>
        public const string RecognitionUnavailable = "assistant.recognitionUnavailable";

        /// <summary>Notice that a skill failed while running.</summary>
        public const string SkillFailed = "assistant.skillFailed";

        /// <summary>Prompt spoken when the command window opened but nothing was heard.</summary>
        public const string HeardNothing = "assistant.heardNothing";

        /// <summary>Spoken confirmation that settings were saved.</summary>
        public const string SettingsSaved = "assistant.settingsSaved";
    }

    /// <summary>Opening greeting and full-hour announcement.</summary>
    public static class Greeting
    {
        /// <summary>Morning greeting.</summary>
        public const string Morning = "greeting.morning";

        /// <summary>Afternoon greeting.</summary>
        public const string Afternoon = "greeting.afternoon";

        /// <summary>Evening greeting.</summary>
        public const string Evening = "greeting.evening";

        /// <summary>Date-and-time summary spoken at startup.</summary>
        public const string Summary = "greeting.summary";

        /// <summary>Announcement of any full hour.</summary>
        public const string HourlyChime = "greeting.hourlyChime";

        /// <summary>Midnight announcement.</summary>
        public const string Midnight = "greeting.midnight";

        /// <summary>Noon announcement.</summary>
        public const string Noon = "greeting.noon";
    }

    /// <summary>Texts and triggers for each skill.</summary>
    public static class Skills
    {
        /// <summary>Triggers for the time skill.</summary>
        public const string TimePhrases = "phrases.time";

        /// <summary>Response of the time skill.</summary>
        public const string TimeResponse = "skill.time.response";

        /// <summary>Triggers for the date skill.</summary>
        public const string DatePhrases = "phrases.date";

        /// <summary>Response of the date skill.</summary>
        public const string DateResponse = "skill.date.response";

        /// <summary>Triggers for the weather skill.</summary>
        public const string WeatherPhrases = "phrases.weather";

        /// <summary>Response of the weather skill.</summary>
        public const string WeatherResponse = "skill.weather.response";

        /// <summary>Response when the weather service cannot be reached.</summary>
        public const string WeatherUnavailable = "skill.weather.unavailable";

        /// <summary>Triggers for the greeting skill.</summary>
        public const string GreetingPhrases = "phrases.greeting";

        /// <summary>Response of the greeting skill.</summary>
        public const string GreetingResponse = "skill.greeting.response";

        /// <summary>Triggers for the help skill.</summary>
        public const string HelpPhrases = "phrases.help";

        /// <summary>Response of the help skill.</summary>
        public const string HelpResponse = "skill.help.response";

        /// <summary>Triggers for the farewell skill.</summary>
        public const string FarewellPhrases = "phrases.farewell";

        /// <summary>Response of the farewell skill.</summary>
        public const string FarewellResponse = "skill.farewell.response";

        /// <summary>Prefixes that start a web search.</summary>
        public const string SearchPrefixes = "phrases.search";

        /// <summary>Confirmation of a web search.</summary>
        public const string SearchResponse = "skill.search.response";

        /// <summary>Response when a search comes with no query.</summary>
        public const string SearchMissingQuery = "skill.search.missingQuery";

        /// <summary>Prefixes that start opening an application.</summary>
        public const string LaunchPrefixes = "phrases.launch";

        /// <summary>Confirmation of opening an application.</summary>
        public const string LaunchResponse = "skill.launch.response";

        /// <summary>Response when the requested application is not mapped.</summary>
        public const string LaunchNotFound = "skill.launch.notFound";

        /// <summary>Triggers for locking the workstation.</summary>
        public const string LockPhrases = "phrases.lock";

        /// <summary>Confirmation of locking.</summary>
        public const string LockResponse = "skill.lock.response";

        /// <summary>Triggers for raising the volume.</summary>
        public const string VolumeUpPhrases = "phrases.volumeUp";

        /// <summary>Triggers for lowering the volume.</summary>
        public const string VolumeDownPhrases = "phrases.volumeDown";

        /// <summary>Triggers for muting.</summary>
        public const string VolumeMutePhrases = "phrases.volumeMute";

        /// <summary>Confirmation of a volume change.</summary>
        public const string VolumeResponse = "skill.volume.response";

        /// <summary>Triggers for opening the settings window.</summary>
        public const string SettingsPhrases = "phrases.settings";

        /// <summary>Confirmation of opening the settings window.</summary>
        public const string SettingsResponse = "skill.settings.response";
    }

    /// <summary>Friendly names for the applications Laura can open.</summary>
    public static class Applications
    {
        /// <summary>Prefix of the application-alias keys.</summary>
        public const string AliasPrefix = "application.";
    }

    /// <summary>Texts for the optional generative mode.</summary>
    public static class GenerativeAi
    {
        /// <summary>Notice that the generative model did not answer.</summary>
        public const string Unavailable = "ai.unavailable";
    }
}
