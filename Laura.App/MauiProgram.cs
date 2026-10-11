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
        builder.Services.AddLauraServices(storage >= 0 && storage + 1 < args.Length ? args[storage + 1] : null);
        return builder.Build();
    }
}
