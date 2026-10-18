using Laura.App.Composition;
using Laura.Platform.Windows.System;
using Microsoft.Extensions.Logging;

namespace Laura.App;

/// <summary>Creates the MAUI host and its application services.</summary>
public static class MauiProgram
{
    /// <summary>Builds the Windows MAUI application.</summary>
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder().UseMauiApp<LauraApplication>();
        builder.Logging.AddDebug();
        string[] args = Environment.GetCommandLineArgs();
        int storage = Array.IndexOf(args, "--storage");
        bool isolated = args.Any(argument => argument is "--ui-smoke" or "--permissions-smoke" or "--screenshot-smoke" or "--brain-smoke");
        if (isolated)
        {
            string owner = Path.GetDirectoryName(Laura.Core.Configuration.IniSettingsStore.GetDefaultFilePath())!;
            _ = Laura.Core.Configuration.DiagnosticStoragePolicy.RequireIsolated(storage < 0 || storage + 1 >= args.Length ? null : args[storage + 1], owner);
        }
        builder.Services.AddLauraServices(storage >= 0 && storage + 1 < args.Length ? args[storage + 1] : null);
        return builder.Build();
    }
}
