namespace Laura.Core.Localization;

/// <summary>
/// Language-file keys, centralized so a typo becomes a compile error instead of a
/// phrase missing in production.
/// </summary>
public static class LocalizationKeys
{
    /// <summary>Local listening controls and the verified wake briefing request.</summary>
    public static class Listening
    {
        /// <summary>Whole-command standby phrases.</summary>
        public const string RestPhrases = "phrases.rest";
        /// <summary>Standby confirmation.</summary>
        public const string RestConfirmation = "assistant.rest";
        /// <summary>Instructions for summarizing explicitly collected briefing data.</summary>
        public const string BriefingRequest = "assistant.wakeBriefing";
        /// <summary>Concise history label for the wake gesture.</summary>
        public const string BriefingTitle = "assistant.wakeTitle";
        /// <summary>Short varied clap acknowledgments, optionally including the owner's name.</summary>
        public const string WakeGreeting = "assistant.clapGreeting";
    }
    /// <summary>Messages spoken by the engine itself, outside any skill.</summary>
    public static class Assistant
    {
        /// <summary>Response when no skill recognizes the command.</summary>
        public const string NotUnderstood = "assistant.notUnderstood";

        /// <summary>Notice that a skill failed while running.</summary>
        public const string SkillFailed = "assistant.skillFailed";

        /// <summary>Spoken confirmation that settings were saved.</summary>
        public const string SettingsSaved = "assistant.settingsSaved";

        /// <summary>Short filler while the generative model is thinking.</summary>
        public const string Thinking = "assistant.thinking";
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
        /// <summary>Observed weather and forecast phrases.</summary>
        public const string WeatherCurrent = "greeting.weather.current";
        /// <summary>Forecast condition when no current observation is available.</summary>
        public const string WeatherForecastCondition = "greeting.weather.forecastCondition";
        /// <summary>Sunny observed condition.</summary>
        public const string WeatherSunny = "greeting.weather.sunny";
        /// <summary>Clear nighttime condition.</summary>
        public const string WeatherClear = "greeting.weather.clear";
        /// <summary>Partially cloudy observed condition.</summary>
        public const string WeatherPartlyCloudy = "greeting.weather.partlyCloudy";
        /// <summary>Cloudy observed condition.</summary>
        public const string WeatherCloudy = "greeting.weather.cloudy";
        /// <summary>Rainy observed condition.</summary>
        public const string WeatherRaining = "greeting.weather.raining";
        /// <summary>Heavy rainy observed condition.</summary>
        public const string WeatherHeavyRaining = "greeting.weather.heavyRaining";
        /// <summary>Stormy observed condition.</summary>
        public const string WeatherStorming = "greeting.weather.storming";
        /// <summary>Snowy observed condition.</summary>
        public const string WeatherSnowing = "greeting.weather.snowing";
        /// <summary>Possible forecast rain.</summary>
        public const string WeatherRain = "greeting.weather.rain";
        /// <summary>Forecast heavy rain.</summary>
        public const string WeatherHeavyRain = "greeting.weather.heavyRain";
        /// <summary>Possible thunderstorms.</summary>
        public const string WeatherThunder = "greeting.weather.thunder";
        /// <summary>Explicit low rain probability.</summary>
        public const string WeatherNoRain = "greeting.weather.noRain";
        /// <summary>Forecast temperature range.</summary>
        public const string WeatherTemperatures = "greeting.weather.temperatures";
        /// <summary>Forecast minimum when the maximum is unavailable.</summary>
        public const string WeatherMinimum = "greeting.weather.minimum";
        /// <summary>Forecast maximum when the minimum is unavailable.</summary>
        public const string WeatherMaximum = "greeting.weather.maximum";
        /// <summary>Provider-issued weather alert.</summary>
        public const string WeatherAlert = "greeting.weather.alert";
        /// <summary>Near-term forecast period.</summary>
        public const string WeatherNextHours = "greeting.weather.nextHours";
        /// <summary>Morning forecast period.</summary>
        public const string WeatherMorning = "greeting.weather.morning";
        /// <summary>Afternoon forecast period.</summary>
        public const string WeatherAfternoon = "greeting.weather.afternoon";
        /// <summary>Evening forecast period.</summary>
        public const string WeatherEvening = "greeting.weather.evening";
        /// <summary>Overnight forecast period.</summary>
        public const string WeatherOvernight = "greeting.weather.overnight";
        /// <summary>Tomorrow morning forecast period.</summary>
        public const string WeatherTomorrowMorning = "greeting.weather.tomorrowMorning";
        /// <summary>Dated Banco Central sale reference.</summary>
        public const string CurrencyReference = "greeting.currency.reference";

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
        /// <summary>A sequence stopped after a native capability declined; previous effects must not be replayed.</summary>
        public const string SequenceStopped = "tools.sequenceStopped";
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
        /// <summary>Response when launch authorization or identity validation stopped the action.</summary>
        public const string LaunchStopped = "skill.launch.stopped";

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

    /// <summary>Localized conservative Brain-learning patterns and attributed entity text.</summary>
    public static class Brain
    {
        /// <summary>Prefix of durable declaration pattern keys.</summary>
        public const string LearningPrefix = "brain.learn.";
        /// <summary>Explicit project-use relationship pattern.</summary>
        public const string ProjectUse = "brain.learn.projectUse";
        /// <summary>Attribution for a named entity, without inventing a definition.</summary>
        public const string EntityContext = "brain.entityContext";
        /// <summary>Whole-request patterns for an explicit overview of the owner's existing Brain knowledge.</summary>
        public const string OverviewPattern = "brain.recallOverview";
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
        /// <summary>Requests explicit provider configuration.</summary>
        public const string ConfigureProvider = "ui.ai.configureProvider";
        /// <summary>Explains an unavailable local-first model without implying cloud authorization.</summary>
        public const string NoProvider = "ui.models.noProvider";
        /// <summary>Reports a provider failure without inventing a cause.</summary>
        public const string ProviderFailed = "ui.ai.providerFailed";
        /// <summary>Identifies a rejected credential.</summary>
        public const string AuthenticationFailed = "ui.ai.failure.authentication";
        /// <summary>Identifies denied provider access.</summary>
        public const string AccessDenied = "ui.ai.failure.access";
        /// <summary>Identifies a reported billing or account limit.</summary>
        public const string UsageLimit = "ui.ai.failure.usage";
        /// <summary>Identifies throttling or an undifferentiated quota limit.</summary>
        public const string CapacityLimit = "ui.ai.failure.capacity";
        /// <summary>Identifies an unavailable configured model.</summary>
        public const string ModelUnavailable = "ui.ai.failure.model";
        /// <summary>Identifies excessive current context.</summary>
        public const string ContextExceeded = "ui.ai.failure.context";
        /// <summary>Identifies an elapsed generation deadline.</summary>
        public const string Timeout = "ui.ai.failure.timeout";
        /// <summary>Identifies a failed transport.</summary>
        public const string NetworkFailed = "ui.ai.failure.network";
        /// <summary>Identifies incomplete or malformed provider output.</summary>
        public const string InvalidResponse = "ui.ai.failure.response";
        /// <summary>Identifies unavailable provider service.</summary>
        public const string ServiceUnavailable = "ui.ai.failure.unavailable";
        /// <summary>Identifies failed local weight verification.</summary>
        public const string LocalIntegrity = "ui.ai.failure.integrity";
        /// <summary>Identifies insufficient currently available RAM.</summary>
        public const string InsufficientMemory = "ui.ai.failure.memory";
        /// <summary>Identifies a provider request rejection.</summary>
        public const string RequestRejected = "ui.ai.failure.request";
        /// <summary>Identifies exhausted operation budgets.</summary>
        public const string OperationLimit = "ui.ai.failure.limit";
        /// <summary>A bounded protocol cycle saved its already confirmed tool results for continuation.</summary>
        public const string TaskCycleSaved = "ui.ai.taskCycleSaved";
        /// <summary>Explains that previous tool effects require inspection before retry.</summary>
        public const string PriorEffects = "ui.ai.failure.priorEffects";
        /// <summary>Refuses an unsupported image before dispatch.</summary>
        public const string ImagesUnsupported = "ui.models.imagesUnsupported";
        /// <summary>Notice that the generative model did not answer.</summary>
        public const string Unavailable = "ai.unavailable";
    }
    /// <summary>Trusted workspace reviews use the independent interface culture.</summary>
    public static class Permissions
    {
        /// <summary>Gets the native bounded repository study review text.</summary>
        public const string RepositoryStudyReview = "ui.permissions.repositoryStudyReview";
        /// <summary>One-hour bounded workspace capability review.</summary>
        public const string WorkspaceReview = "ui.permissions.workspaceReview";
        /// <summary>Actual runtime disclosure destination.</summary>
        public const string WorkspaceCloudReview = "ui.permissions.workspaceCloudReview";
        /// <summary>Strictly local workspace access.</summary>
        public const string WorkspaceLocalReview = "ui.permissions.workspaceLocalReview";
    }
}
