using Laura.App.Infrastructure;
using Laura.App.Shell;
using Laura.App.Views;
using Laura.Core.Abstractions;
using Laura.Core.Configuration;
using Laura.Core.Conversation;
using Laura.Core.Engine;
using Laura.Core.Localization;
using Laura.Core.Skills.Builtin;
using Laura.Core.Weather;
using Laura.Platform.Windows.Audio;
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
    public static IServiceCollection AddLauraServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddLogging(static builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddDebug();
        });

        AddDomain(services);
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
    private static void AddDomain(IServiceCollection services)
    {
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IUserContext, UserContext>();

        services.AddSingleton<ISettingsStore>(provider => new IniSettingsStore(
            IniSettingsStore.GetDefaultFilePath(),
            provider.GetRequiredService<ILogger<IniSettingsStore>>()));
        services.AddSingleton<ISettingsService, SettingsService>();

        services.AddSingleton<ILocalizer>(provider => new JsonLocalizer(
            GetLocalesDirectory(),
            provider.GetRequiredService<ILogger<JsonLocalizer>>()));

        services.AddSingleton<IModelCatalog>(provider => new OllamaModelCatalog(
            OllamaModelCatalog.GetDefaultLibraryDirectory(),
            provider.GetRequiredService<ILogger<OllamaModelCatalog>>()));

        // A short timeout keeps a slow network from making the weather skill hang.
        services.AddSingleton(_ => new HttpClient { Timeout = TimeSpan.FromSeconds(8) });
        services.AddSingleton<IWeatherProvider, WttrWeatherProvider>();

        services.AddSingleton<IConversationEngine, OllamaConversationEngine>();
        services.AddSingleton<GreetingComposer>();

        AddSkills(services);
        services.AddSingleton<ISkillDispatcher, SkillDispatcher>();

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
        // WinRT gives access to the natural OneCore/neural voices; SAPI only had the
        // robotic desktop voices.
        services.AddSingleton<ISpeechSynthesizer, WinRtSpeechSynthesizer>();
        services.AddSingleton<ISpeechRecognizer, SapiSpeechRecognizer>();
        services.AddSingleton<ISystemController, WindowsSystemController>();
        services.AddSingleton<IProcessLauncher, WindowsProcessLauncher>();
        services.AddSingleton<IStartupRegistration, RegistryStartupRegistration>();
        services.AddSingleton<IAudioDeviceCatalog, WindowsAudioDeviceCatalog>();
    }

    /// <summary>
    /// Registers the Windows Forms presentation layer.
    ///
    /// Args:
    ///     services: Service collection.
    /// </summary>
    private static void AddPresentation(IServiceCollection services)
    {
        services.AddSingleton<IUiDispatcher, WinFormsUiDispatcher>();
        services.AddSingleton<WinFormsShellController>();
        services.AddSingleton<IShellController>(provider => provider.GetRequiredService<WinFormsShellController>());
        services.AddSingleton<SettingsForm>();
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
