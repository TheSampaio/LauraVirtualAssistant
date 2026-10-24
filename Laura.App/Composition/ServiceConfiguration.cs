using Laura.App.Infrastructure;
using Laura.App.Shell;
using Laura.App.Views;
using Laura.Core.Abstractions;
using Laura.Core.Configuration;
using Laura.Core.Conversation;
using Laura.Core.Engine;
using Laura.Core.Localization;
using Laura.Core.Models;
using Laura.Core.Memory;
using Laura.Core.Brain;
using Laura.Core.Files;
using Laura.Core.Permissions;
using Laura.Core.Skills.Builtin;
using Laura.Core.Weather;
using Laura.Platform.Windows.Conversation;
using Laura.Platform.Windows.Speech;
using Laura.Platform.Windows.System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Laura.App.Composition;

/// <summary>
/// Composition root: registers every Laura dependency in a single place.
///
/// Concentrating the abstraction-to-implementation mapping here is what makes the
/// platform and UI choices replaceable without touching the domain.
/// </summary>
public static class ServiceConfiguration
{
    /// <summary>
    /// Registers the domain, platform and presentation services.
    ///
    /// Args:
    ///     services: Collection the services are registered into.
    ///
    /// Returns:
    ///     The same collection, for chaining.
    /// </summary>
    public static IServiceCollection AddLauraServices(this IServiceCollection services, string? storageDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddLogging(static builder =>
        {
            builder.SetMinimumLevel(LogLevel.Trace);
            builder.AddDebug();
        });

        AddDomain(services, storageDirectory);
        AddPlatform(services);
        AddPresentation(services);

        return services;
    }

    /// <summary>
    /// Registers the domain: settings, localization, skills and the engine.
    ///
    /// Args:
    ///     services: Service collection.
    /// </summary>
    private static void AddDomain(IServiceCollection services, string? storageDirectory)
    {
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IUserContext, UserContext>();

        string settingsPath = storageDirectory is null ? IniSettingsStore.GetDefaultFilePath() : Path.Combine(storageDirectory, "settings.ini");
        // Diagnostics use only Laura's existing protected credentials, never fixture copies or model-provided paths.
        string credentialDirectory = storageDirectory is not null && (Environment.GetCommandLineArgs().Contains("--permissions-live-smoke", StringComparer.Ordinal) || Environment.GetCommandLineArgs().Contains("--screenshot-live-smoke", StringComparer.Ordinal) || Environment.GetCommandLineArgs().Contains("--brain-live-smoke", StringComparer.Ordinal) || Environment.GetCommandLineArgs().Contains("--agent-live-smoke", StringComparer.Ordinal))
            ? Path.GetDirectoryName(IniSettingsStore.GetDefaultFilePath())! : Path.GetDirectoryName(settingsPath)!;
        string chatGptDirectory = storageDirectory is not null && Environment.GetCommandLineArgs().Contains("--chatgpt-live-smoke", StringComparer.Ordinal)
            ? Path.GetDirectoryName(IniSettingsStore.GetDefaultFilePath())! : Path.GetDirectoryName(settingsPath)!;
        services.AddSingleton(new AssistantStorage(Path.GetDirectoryName(Path.GetFullPath(settingsPath))!));
        services.AddSingleton(_ => new LegacyProviderCleanup(Path.GetDirectoryName(Path.GetFullPath(settingsPath))!));
        services.AddSingleton(provider => new Laura.Core.Diagnostics.RuntimeJournal(storageDirectory is null
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Laura", "Diagnostics")
            : Path.Combine(provider.GetRequiredService<AssistantStorage>().Directory, "Diagnostics")));
        services.AddSingleton<ILoggerProvider, Laura.Core.Diagnostics.RuntimeLogProvider>();
        services.AddSingleton<ISettingsStore>(provider => new IniSettingsStore(
            settingsPath,
            provider.GetRequiredService<ILogger<IniSettingsStore>>()));
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IPrivateDataProtector, WindowsPrivateDataProtector>();
        services.AddSingleton<ICognitiveTaskJournal>(provider => new FileCognitiveTaskJournal(
            Path.Combine(provider.GetRequiredService<AssistantStorage>().Directory, "Tasks"), provider.GetRequiredService<IPrivateDataProtector>()));
        services.AddSingleton<CognitiveTasks>();
        services.AddSingleton(provider => new PermissionStore(Path.Combine(provider.GetRequiredService<AssistantStorage>().Directory, "Security", "permissions.json")));
        services.AddSingleton(provider => new PermissionManager(provider.GetRequiredService<PermissionStore>(),
            () =>
            {
                PermissionOptions options = provider.GetRequiredService<ISettingsService>().Current.Permissions;
                return options.Sanitized();
            },
            () => new[] { provider.GetRequiredService<AssistantStorage>().Directory,
                provider.GetRequiredService<ISettingsService>().Current.Brain.Directory, Path.Combine(chatGptDirectory, "Credentials"),
                Path.Combine(credentialDirectory, "google-key.dat"), Path.Combine(credentialDirectory, "google-key.previous.dat"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "LauraCredentialBroker") }.Where(path => !string.IsNullOrWhiteSpace(path)),
            Path.Combine(provider.GetRequiredService<AssistantStorage>().Directory, "Workspace"), provider.GetRequiredService<IUiLocalizer>()));
        services.AddSingleton<SqliteConversationMemory>(_ => new SqliteConversationMemory(
            Path.Combine(Path.GetDirectoryName(settingsPath)!, "memory.db")));
        services.AddSingleton<IBrainEmbedding, LexicalOnlyEmbedding>();
        string brainCache = storageDirectory is null ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Laura", "BrainCache") : Path.Combine(storageDirectory, "BrainCache");
        services.AddSingleton(provider => new BrainIndex(brainCache, provider.GetRequiredService<IBrainEmbedding>()));
        services.AddSingleton<Func<string, IBrainStorage>>(_ => location => new FileBrainStorage(location));
        services.AddSingleton<BrainService>();
        services.AddSingleton<BrainLearning>();
        services.AddSingleton<BrainExperiences>();
        services.AddSingleton<BrainApplications>();
        services.AddSingleton<IRemoteBrainSource>(_ => new GitHubBrainSource(new HttpClient(GitHubBrainSource.CreateHandler()) { Timeout = TimeSpan.FromSeconds(25) }));
        services.AddSingleton<BrainSources>();
        services.AddSingleton<IRepositoryStudyStore>(provider => new FileRepositoryStudyStore(
            Path.Combine(provider.GetRequiredService<AssistantStorage>().Directory, "RepositoryStudies"), provider.GetRequiredService<IPrivateDataProtector>()));
        services.AddSingleton<RepositoryStudy>();
        services.AddSingleton<RepositoryAnalysis>();
        services.AddSingleton(provider => new Laura.Core.Mcp.McpServerStore(Path.Combine(provider.GetRequiredService<AssistantStorage>().Directory, "Security", "mcp-servers.json")));
        services.AddSingleton(provider => new Laura.Core.Mcp.McpRuntime(provider.GetRequiredService<Laura.Core.Mcp.McpServerStore>(), provider.GetRequiredService<PermissionManager>(), WindowsMcpSession.ConnectAsync));
        services.AddSingleton(provider => new BrainSkills(provider.GetRequiredService<BrainService>(), provider.GetRequiredService<ISettingsService>(),
            () => (provider.GetRequiredService<IAssistantTools>() is MauiAssistantTools native ? native.RegisteredToolNames : new HashSet<string>(StringComparer.Ordinal))
                .Concat(provider.GetRequiredService<Laura.Core.Mcp.McpRuntime>().ToolNames).ToHashSet(StringComparer.Ordinal),
            new BrainIndex(Path.Combine(brainCache, "Skills"), provider.GetRequiredService<IBrainEmbedding>()), provider.GetRequiredService<Laura.Core.Mcp.McpRuntime>().DependencyAvailable));
        services.AddSingleton<BrainConversationMemory>();
        services.AddSingleton<BrainSkillUsage>();
        services.AddSingleton<BrainContextBuilder>();
        services.AddSingleton<BrainMaintenance>();
        services.AddSingleton<IConversationMemory>(provider => provider.GetRequiredService<BrainConversationMemory>());
        services.AddSingleton<IApiKeyStore>(_ => new ProtectedApiKeyStore(credentialDirectory));
        services.AddSingleton(provider => new ChatGptCredentialStore(Path.Combine(chatGptDirectory, "Credentials"),
            provider.GetRequiredService<IPrivateDataProtector>()));
        services.AddSingleton(provider => new ChatGptBrokerClient(provider.GetRequiredService<ChatGptCredentialStore>(), Path.Combine(chatGptDirectory, "Credentials")));
        services.AddSingleton<UiSmokeChatGptConnection>();
        bool offlineChatGptSmoke = Environment.GetCommandLineArgs().Contains("--ui-smoke", StringComparer.Ordinal)
            && !Environment.GetCommandLineArgs().Contains("--chatgpt-live-smoke", StringComparer.Ordinal)
            && !Environment.GetCommandLineArgs().Contains("--spoken-providers-smoke", StringComparer.Ordinal);
        services.AddSingleton<IChatGptConnection>(provider => offlineChatGptSmoke ? provider.GetRequiredService<UiSmokeChatGptConnection>() : provider.GetRequiredService<ChatGptBrokerClient>());
        services.AddSingleton<IChatGptInference>(provider => offlineChatGptSmoke ? provider.GetRequiredService<UiSmokeChatGptConnection>() : provider.GetRequiredService<ChatGptBrokerClient>());
        services.AddSingleton<IChatGptBrokerSetup>(_ => new ChatGptBrokerSetup(AppContext.BaseDirectory));
        services.AddSingleton<ChatGptConversationEngine>();
        services.AddSingleton(_ => new UserFileWorkspace(Path.Combine(Path.GetDirectoryName(settingsPath)!, "Workspace")));
        services.AddSingleton<IKnownFolderResolver, WindowsKnownFolderResolver>();
        services.AddSingleton<IVersionedFileEditor>(provider => new VersionedFileEditor(Path.Combine(provider.GetRequiredService<AssistantStorage>().Directory, "Undo", "Edits")));

        services.AddSingleton<ILocalizer>(provider => new JsonLocalizer(
            GetLocalesDirectory(),
            provider.GetRequiredService<ILogger<JsonLocalizer>>()));
        services.AddSingleton<IUiLocalizer>(provider => new JsonLocalizer(
            GetLocalesDirectory(), provider.GetRequiredService<ILogger<JsonLocalizer>>()));


        // A short timeout keeps a slow network from making the weather skill hang.
        services.AddSingleton(_ => new HttpClient { Timeout = TimeSpan.FromSeconds(8) });
        services.AddSingleton<IWeatherProvider, WttrWeatherProvider>();
        services.AddSingleton<WeatherGreetingFormatter>();

        services.AddSingleton(provider => new LlamaCppRuntime(provider.GetRequiredService<ISettingsService>(),
            new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false, ConnectTimeout = TimeSpan.FromSeconds(3) }) { Timeout = Timeout.InfiniteTimeSpan }));
        services.AddSingleton(provider => new LlamaCppTextClient(provider.GetRequiredService<LlamaCppRuntime>(),
            new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false }) { Timeout = Timeout.InfiniteTimeSpan }, provider.GetRequiredService<ISettingsService>()));
        services.AddKeyedSingleton<TextConversationEngine>("local", (provider, _) => new TextConversationEngine(provider.GetRequiredService<LlamaCppTextClient>(),
            provider.GetRequiredService<ISettingsService>(), provider.GetRequiredService<IConversationMemory>(), provider.GetRequiredService<IAssistantTools>(),
            provider.GetRequiredService<BrainContextBuilder>(), provider.GetRequiredService<CognitiveTasks>(), provider.GetRequiredService<BrainSkills>(), provider.GetRequiredService<PermissionManager>()));
        services.AddSingleton<GeminiLiveClient>();
        services.AddSingleton<IVoiceInput, WhisperSpeechRecognizer>();
        services.AddSingleton<LocalVoiceWorkerClient>();
        services.AddSingleton<LocalVoiceSession>();
        services.AddSingleton(_ => new LocalPackInstaller(new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan }));
        services.AddSingleton<ListeningControl>();
        services.AddSingleton<WindowsClapListener>();
        services.AddSingleton<WakeBriefing>();
        services.AddSingleton<IBriefingContributor, NewsBriefingContributor>();
        services.AddSingleton<IBriefingContributor, CurrencyBriefingContributor>();
        services.AddSingleton<IBriefingContributor, WeatherBriefingContributor>();
        services.AddSingleton<IBriefingContributor, ReminderBriefingContributor>();
        services.AddSingleton<ClapWakeCoordinator>();
        services.AddSingleton(provider => new ConversationProviderRouter(provider.GetRequiredService<ISettingsService>(),
            provider.GetRequiredService<ChatGptConversationEngine>(), provider.GetRequiredService<CognitiveTasks>(),
            provider.GetRequiredKeyedService<TextConversationEngine>("local"), provider.GetRequiredService<GeminiLiveClient>()));
        services.AddSingleton<IConversationEngine>(provider => provider.GetRequiredService<ConversationProviderRouter>());
        services.AddSingleton<WindowsApplicationCatalog>();
        services.AddSingleton<WindowsResourceMonitor>();
        services.AddSingleton<WindowsDiskMonitor>();
        services.AddSingleton<WindowsGpuMonitor>();
        services.AddSingleton<IAssistantTools, MauiAssistantTools>();
        services.AddSingleton<ReminderScheduler>();
        services.AddSingleton<GreetingComposer>();

        AddSkills(services);
        services.AddSingleton<SkillDispatcher>();
        services.AddSingleton<ISkillDispatcher, AssistantSkillDispatcher>();

        services.AddSingleton<IAssistantEngine, AssistantEngine>();
        services.AddSingleton<HourlyAnnouncer>();
    }

    /// <summary>
    /// Registers the built-in skills.
    ///
    /// Args:
    ///     services: Service collection.
    /// </summary>
    private static void AddSkills(IServiceCollection services)
    {
        services.AddSingleton<ISkill, CancelTaskSkill>();
        services.AddSingleton<ISkill>(provider => new SwitchProviderSkill(provider.GetRequiredService<ILocalizer>(),
            provider.GetRequiredService<ISettingsService>(), () => provider.GetRequiredService<ChatGptConversationEngine>().IsAvailable,
            () => provider.GetRequiredService<LlamaCppTextClient>().IsAvailable,
            () => provider.GetRequiredService<GeminiLiveClient>().IsAvailable));
        services.AddSingleton<ISkill, OpenSettingsSkill>();
        services.AddSingleton<ISkill, DesktopToolSkill>();
        services.AddSingleton<ISkill, TimeSkill>();
        services.AddSingleton<ISkill, DateSkill>();
        services.AddSingleton<ISkill, WeatherSkill>();
        services.AddSingleton<ISkill, LockWorkstationSkill>();
        services.AddSingleton<ISkill, VolumeSkill>();
        services.AddSingleton<ISkill, HelpSkill>();
        services.AddSingleton<ISkill, FarewellSkill>();
        services.AddSingleton<ISkill, WebSearchSkill>();
        services.AddSingleton<ISkill, LaunchApplicationSkill>();
        services.AddSingleton<ISkill, GreetingSkill>();
    }

    /// <summary>
    /// Registers the Windows-specific adapters.
    ///
    /// Args:
    ///     services: Service collection.
    /// </summary>
    private static void AddPlatform(IServiceCollection services)
    {
        services.AddSingleton<KokoroSpeechSynthesizer>();
        services.AddSingleton<ISpeechSynthesizer, ProviderSpeechRouter>();
        services.AddSingleton<ISystemController, WindowsSystemController>();
        services.AddSingleton<IProcessLauncher, WindowsProcessLauncher>();
        services.AddSingleton<ICommandExecutor, WindowsCommandExecutor>();
        services.AddSingleton<Laura.Core.Execution.CommandJobs>();
        services.AddSingleton<IStartupRegistration, RegistryStartupRegistration>();
    }

    /// <summary>
    /// Registers the native MAUI presentation layer.
    ///
    /// Args:
    ///     services: Service collection.
    /// </summary>
    private static void AddPresentation(IServiceCollection services)
    {
        services.AddSingleton<IUiDispatcher, MauiUiDispatcher>();
        services.AddSingleton<LauraNotifications>();
        services.AddSingleton<ConversationActions>();
        services.AddSingleton<RuntimeDiagnosticContext>();
        services.AddSingleton<ScreenshotCoordinator>();
        services.AddSingleton<Laura.App.Platforms.Windows.WindowsApprovalCenter>();
        services.AddSingleton<IDesktopTools, MauiDesktopTools>();
        services.AddSingleton<MauiShellController>();
        services.AddSingleton<Laura.Core.Abstractions.IShellController>(provider => provider.GetRequiredService<MauiShellController>());
        services.AddSingleton<Laura.App.Controls.VulkanReactorHost>();
        services.AddSingleton<MainPage>();
        services.AddTransient<SetupPage>();
        services.AddTransient<MemoryPage>();
        services.AddTransient<TasksPage>();
        services.AddTransient<LogsPage>();
    }

    /// <summary>
    /// Resolves the folder of language files copied to the build output.
    ///
    /// Returns:
    ///     The path of the locales folder.
    /// </summary>
    private static string GetLocalesDirectory() =>
        Path.Combine(AppContext.BaseDirectory, "Localization", "Locales");

}
