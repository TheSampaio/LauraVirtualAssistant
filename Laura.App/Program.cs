using System.Threading;
using System.Windows.Forms;
using Laura.App.Composition;
using Laura.App.Shell;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Laura.App;

/// <summary>
/// Entry point for the Laura application.
/// </summary>
internal static class Program
{
    /// <summary>
    /// Initializes Windows Forms, composes dependencies, and hands control to the
    /// system tray.
    ///
    /// A single instance is guaranteed by a named mutex: two processes
    /// would contend for the microphone and global hotkey.
    /// </summary>
    [STAThread]
    private static void Main()
    {
        using var singleInstanceMutex = new Mutex(
            initiallyOwned: true,
            "Laura.VirtualAssistant.SingleInstance",
            out bool isFirstInstance);

        if (!isFirstInstance)
        {
            return;
        }

        // This must run before any control exists on the thread; after that,
        // Windows Forms refuses to change the mode.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        ApplicationConfiguration.Initialize();

        // Installs the Windows Forms synchronization context before composing
        // dependencies so IUiDispatcher captures the UI thread.
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());

        ServiceProvider provider = new ServiceCollection()
            .AddLauraServices()
            .BuildServiceProvider();

        try
        {
            InstallExceptionHandlers(provider.GetRequiredService<ILogger<LauraApplicationContext>>());

            using var context = new LauraApplicationContext(provider);
            Application.Run(context);
        }
        finally
        {
            provider.Dispose();
        }
    }

    /// <summary>
    /// Registers Laura as responsible for unhandled exceptions.
    ///
    /// Without this, a failure in an event handler tears down the message loop and
    /// the application gets stuck in the background: the tray remains, but nothing else
    /// responds. Logging and continuing keeps the assistant usable.
    ///
    /// Args:
    ///     logger: Destination for failure logs.
    /// </summary>
    private static void InstallExceptionHandlers(ILogger logger)
    {
        Application.ThreadException += (_, args) =>
            logger.LogError(args.Exception, "Unhandled exception on the UI thread.");

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            logger.LogError(args.ExceptionObject as Exception, "Unhandled exception in the application domain.");
    }
}
