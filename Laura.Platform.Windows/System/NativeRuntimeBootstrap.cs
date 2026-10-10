using System.Runtime.InteropServices;

namespace Laura.Platform.Windows.System;

/// <summary>Loads the packaged Microsoft CRT before Windows speech can select a global copy.</summary>
public static class NativeRuntimeBootstrap
{
    /// <summary>Preloads trusted app-local runtime DLLs for the process lifetime; development builds may use the installed CRT.</summary>
    public static void Initialize()
    {
        string[] names = ["vcruntime140.dll", "vcruntime140_1.dll", "msvcp140.dll", "vcomp140.dll"];
        if (!File.Exists(Path.Combine(AppContext.BaseDirectory, names[0]))) { return; }
        // Engines and Windows speech share these module names. Keep their references
        // until process exit, after all native engines and callbacks have shut down.
        foreach (string name in names) { _ = NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, name)); }
    }
}
