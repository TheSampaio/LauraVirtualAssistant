using System.Runtime.InteropServices;
using Microsoft.Win32;
using Laura.Core.Skills;

namespace Laura.Platform.Windows.System;

/// <summary>Discovers Windows applications from shortcuts, App Paths, uninstall entries and portable app folders.</summary>
public sealed class WindowsApplicationCatalog : IDisposable
{
    private IReadOnlyList<InstalledApplication> _applications = [];
    private readonly SemaphoreSlim _refresh = new(1, 1);
    /// <summary>Gets the last completed application inventory.</summary>
    public IReadOnlyList<InstalledApplication> Applications => _applications;
    /// <summary>Returns matching installed apps, preserving ambiguity for the caller to resolve.</summary>
    public IReadOnlyList<InstalledApplication> Find(string name) => _applications
        .Select(app => (App: app, Score: ApplicationNameMatcher.Score(app.Name, name)))
        .Where(match => match.Score > 0).OrderByDescending(match => match.Score)
        .Select(match => match.App).DistinctBy(app => app.Target, StringComparer.OrdinalIgnoreCase).Take(8).ToArray();
    /// <summary>Builds a bounded local inventory outside the UI thread without launching anything.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _refresh.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { _applications = await Task.Run(() => Discover(cancellationToken), cancellationToken).ConfigureAwait(false); }
        finally { _refresh.Release(); }
    }
    /// <summary>Collects shell launch targets without executing shell commands.</summary>
    private static InstalledApplication[] Discover(CancellationToken token)
    {
        var apps = new List<InstalledApplication>();
        foreach (Environment.SpecialFolder folder in new[] { Environment.SpecialFolder.Programs, Environment.SpecialFolder.CommonPrograms, Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.CommonDesktopDirectory })
        {
            string root = Environment.GetFolderPath(folder);
            if (!Directory.Exists(root)) { continue; }
            foreach (string path in Enumerate(root, "*.lnk", 5, token))
            { string name = Path.GetFileNameWithoutExtension(path); if (!name.Contains("uninstall", StringComparison.OrdinalIgnoreCase)) { apps.Add(new(name, path)); } }
        }
        foreach (RegistryHive hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using RegistryKey registry = RegistryKey.OpenBaseKey(hive, view);
            using RegistryKey? paths = registry.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths");
            foreach (string name in paths?.GetSubKeyNames() ?? [])
            {
                using RegistryKey? entry = paths?.OpenSubKey(name);
                string target = (entry?.GetValue("") as string ?? "").Trim('"');
                if (File.Exists(target)) { apps.Add(new(Path.GetFileNameWithoutExtension(name), target)); }
            }
            using RegistryKey? uninstall = registry.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall");
            foreach (string name in uninstall?.GetSubKeyNames() ?? [])
            {
                using RegistryKey? entry = uninstall?.OpenSubKey(name);
                string display = entry?.GetValue("DisplayName") as string ?? "";
                string icon = (entry?.GetValue("DisplayIcon") as string ?? "").Split(',')[0].Trim('"');
                if (display.Length > 0 && icon.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(icon)
                    && !Path.GetFileName(icon).Contains("unins", StringComparison.OrdinalIgnoreCase)
                    && !Path.GetFileName(icon).Contains("setup", StringComparison.OrdinalIgnoreCase)) { apps.Add(new(display, icon)); }
            }
        }
        foreach (DriveInfo drive in DriveInfo.GetDrives().Where(drive => drive.IsReady && drive.DriveType == DriveType.Fixed))
        {
            foreach (string folder in new[] { "EmulationLibrary", "Emulators", "Emuladores", "Games", "GameLibrary", "Apps", "Programs", "Aplicativos" })
            {
                string root = Path.Combine(drive.RootDirectory.FullName, folder);
                if (!Directory.Exists(root)) { continue; }
                foreach (string path in Enumerate(root, "*.exe", 4, token).Take(2500))
                {
                    string name = Path.GetFileNameWithoutExtension(path);
                    if (!name.Contains("uninstall", StringComparison.OrdinalIgnoreCase) && !name.Contains("crash", StringComparison.OrdinalIgnoreCase)) { apps.Add(new(name, path)); }
                }
            }
        }
        return apps.DistinctBy(app => (app.Name.ToUpperInvariant(), app.Target.ToUpperInvariant())).OrderBy(app => app.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }
    /// <summary>Walks only bounded folders, skipping inaccessible locations and links.</summary>
    private static IEnumerable<string> Enumerate(string root, string pattern, int depth, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string[] files, folders;
        try { files = Directory.GetFiles(root, pattern); folders = depth > 0 ? Directory.GetDirectories(root) : []; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { yield break; }
        foreach (string file in files) { yield return file; }
        foreach (string folder in folders)
        {
            if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) { continue; }
            foreach (string file in Enumerate(folder, pattern, depth - 1, token)) { yield return file; }
        }
    }
    /// <inheritdoc />
    public void Dispose() => _refresh.Dispose();
}

/// <summary>Describes an observed Windows shell application target.</summary>
public sealed record InstalledApplication(string Name, string Target);
