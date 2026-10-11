namespace Laura.App.WinUI;

/// <summary>Windows bootstrap for the native MAUI application.</summary>
public partial class App : MauiWinUIApplication, IDisposable
{
    private readonly Mutex? _instance;
    /// <summary>Initializes the WinUI host.</summary>
    public App()
    {
        if (!Environment.GetCommandLineArgs().Contains("--storage", StringComparer.Ordinal))
        {
            _instance = new Mutex(false, "Laura.VirtualAssistant.SingleInstance", out bool first);
            if (!first) { Dispose(); Environment.Exit(0); }
        }
        InitializeComponent();
    }
    /// <inheritdoc />
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
    /// <inheritdoc />
    public void Dispose() { _instance?.Dispose(); GC.SuppressFinalize(this); }
}
