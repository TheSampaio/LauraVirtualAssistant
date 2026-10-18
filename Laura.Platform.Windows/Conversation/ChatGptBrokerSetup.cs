using System.ComponentModel;
using System.Diagnostics;
using Laura.Core.Abstractions;
using Laura.Core.ChatGpt;

namespace Laura.Platform.Windows.Conversation;

/// <summary>Elevates only the bundled fixed service installer after a native owner button gesture.</summary>
public sealed class ChatGptBrokerSetup(string packageDirectory) : IChatGptBrokerSetup
{
    /// <inheritdoc />
    public async Task InstallAsync(CancellationToken cancellationToken = default)
    {
        string script = Path.Combine(packageDirectory, "Install-CredentialBroker.ps1"), payload = Path.Combine(packageDirectory, "CredentialBroker");
        if (!File.Exists(script) || !File.Exists(Path.Combine(payload, "broker-manifest.json"))) { throw new ChatGptConnectionException("broker_installer_missing"); }
        string shell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        // Paths are composition-owned application paths, never model arguments; embedded quotes are invalid on Windows.
        var start = new ProcessStartInfo(shell, $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" -PayloadDirectory \"{payload}\"")
        { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = packageDirectory };
        try
        {
            using var process = Process.Start(start) ?? throw new ChatGptConnectionException("broker_install_failed");
            // Cancellation stops waiting; it cannot roll back an already approved system installation.
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            if (process.ExitCode != 0) { throw new ChatGptConnectionException("broker_install_failed"); }
        }
        catch (Win32Exception error) when (error.NativeErrorCode == 1223) { throw new ChatGptConnectionException("canceled"); }
    }
}
