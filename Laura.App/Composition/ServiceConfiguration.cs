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
            builder.SetMinimumLevel(LogLevel.Information);
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
        services.AddSingleton(new AssistantStorage(Path.GetDirectoryName(Path.GetFullPath(settingsPath))!));
        services.AddSingleton<ISettingsStore>(provider => new IniSettingsStore(
            settingsPath,
            provider.GetRequiredService<ILogger<IniSettingsStore>>()));
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton(provider => new PermissionStore(Path.Combine(provider.GetRequiredService<AssistantStorage>().Directory, "Security", "permissions.json")));
        services.AddSingleton(provider => new PermissionManager(provider.GetRequiredService<PermissionStore>(),
            () => provider.GetRequiredService<ISettingsService>().Current.Permissions,
            () => new[] { provider.GetRequiredService<AssistantStorage>().Directory,
                provider.GetRequiredService<ISettingsService>().Current.Brain.Directory }.Where(path => !string.IsNullOrWhiteSpace(path)),
            Path.Combine(provider.GetRequiredService<AssistantStorage>().Directory, "Workspace")));
        services.AddSingleton<SqliteConversationMemory>(_ => new SqliteConversationMemory(
            Path.Combine(Path.GetDirectoryName(settingsPath)!, "memory.db")));
        services.AddSingleton<IBrainEmbedding, OnnxBrainEmbedding>();
        string brainCache = storageDirectory is null ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Laura", "BrainCache") : Path.Combine(storageDirectory, "BrainCache");
        services.AddSingleton(provider => new BrainIndex(brainCache, provider.GetRequiredService<IBrainEmbedding>()));
        services.AddSingleton<Func<string, IBrainStorage>>(_ => location => new FileBrainStorage(location));
        services.AddSingleton<BrainService>();
        services.AddSingleton<BrainLearning>();
        services.AddSingleton<BrainExperiences>();
        services.AddSingleton<BrainApplications>();
        services.AddSingleton<BrainSources>();
        services.AddSingleton(provider => new BrainSkills(provider.GetRequiredService<BrainService>(), provider.GetRequiredService<ISettingsService>(),
            () => provider.GetRequiredService<IAssistantTools>() is MauiAssistantTools native ? native.RegisteredToolNames : new HashSet<string>(StringComparer.Ordinal),
            new BrainIndex(Path.Combine(brainCache, "Skills"), provider.GetRequiredService<IBrainEmbedding>())));
        services.AddSingleton<BrainConversationMemory>();
        services.AddSingleton<BrainSkillUsage>();
        services.AddSingleton<IConversationMemory>(provider => provider.GetRequiredService<BrainConversationMemory>());
        // Explicit live diagnostics read the already protected credential in-process; it is neither printed nor copied into fixtures.
        string credentialDirectory = storageDirectory is not null && (Environment.GetCommandLineArgs().Contains("--permissions-live-smoke", StringComparer.Ordinal) || Environment.GetCommandLineArgs().Contains("--screenshot-live-smoke", StringComparer.Ordinal) || Environment.GetCommandLineArgs().Contains("--brain-live-smoke", StringComparer.Ordinal))
            ? Path.GetDirectoryName(IniSettingsStore.GetDefaultFilePath())! : Path.GetDirectoryName(settingsPath)!;
        services.AddSingleton<IApiKeyStore>(_ => new ProtectedApiKeyStore(credentialDirectory));
        services.AddSingleton(_ => new UserFileWorkspace(Path.Combine(Path.GetDirectoryName(settingsPath)!, "Workspace")));

        services.AddSingleton<ILocalizer>(provider => new JsonLocalizer(
            GetLocalesDirectory(),
            provider.GetRequiredService<ILogger<JsonLocalizer>>()));
        services.AddSingleton<IUiLocalizer>(provider => new JsonLocalizer(
            GetLocalesDirectory(), provider.GetRequiredService<ILogger<JsonLocalizer>>()));


        // A short timeout keeps a slow network from making the weather skill hang.
        services.AddSingleton(_ => new HttpClient { Timeout = TimeSpan.FromSeconds(8) });
        services.AddSingleton<IWeatherProvider, WttrWeatherProvider>();

        services.AddSingleton<GeminiLiveClient>();
        services.AddSingleton<ListeningControl>();
        services.AddSingleton<WindowsClapListener>();
        services.AddSingleton<WakeBriefing>();
        services.AddSingleton<IBriefingContributor, NewsBriefingContributor>();
        services.AddSingleton<IBriefingContributor, CurrencyBriefingContributor>();
        services.AddSingleton<IBriefingContributor, ReminderBriefingContributor>();
        services.AddSingleton<ClapWakeCoordinator>();
        services.AddSingleton<MarkLvConfigurationImporter>();
        services.AddSingleton<IConversationEngine>(provider => provider.GetRequiredService<GeminiLiveClient>());
        services.AddSingleton<WindowsApplicationCatalog>();
        services.AddSingleton<WindowsResourceMonitor>();
        services.AddSingleton<WindowsDiskMonitor>();
        services.AddSingleton<WindowsGpuMonitor>();
        services.AddSingleton<IAssistantTools, MauiAssistantTools>();
        services.AddSingleton<ReminderScheduler>();
        services.AddSingleton<GreetingComposer>();

        AddSkills(services);
        services.AddSingleton<ISkillDispatcher, GeminiSkillDispatcher>();

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
        services.AddSingleton<ISpeechSynthesizer, SpeechRouter>();
        services.AddSingleton<ISystemController, WindowsSystemController>();
        services.AddSingleton<IProcessLauncher, WindowsProcessLauncher>();
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
        services.AddSingleton<ScreenshotCoordinator>();
        services.AddSingleton<Laura.App.Platforms.Windows.WindowsApprovalCenter>();
        services.AddSingleton<IDesktopTools, MauiDesktopTools>();
        services.AddSingleton<MauiShellController>();
        services.AddSingleton<Laura.Core.Abstractions.IShellController>(provider => provider.GetRequiredService<MauiShellController>());
        services.AddSingleton<MainPage>();
        services.AddTransient<SetupPage>();
        services.AddTransient<MemoryPage>();
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
