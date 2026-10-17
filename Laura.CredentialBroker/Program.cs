using System.ServiceProcess;

namespace Laura.CredentialBroker;

/// <summary>Windows SCM entry point; arbitrary command-line execution and token export are unsupported.</summary>
internal static class Program
{
    /// <summary>Runs only under SCM using the least-privileged LocalService account and a service-owned data root.</summary>
    private static void Main()
    {
        using var service = new CredentialService(); ServiceBase.Run(service);
    }
}

/// <summary>Owns broker lifecycle without logging payloads or exposing an external execution facility.</summary>
internal sealed class CredentialService : ServiceBase
{
    private CancellationTokenSource? _stop;
    private Task? _worker;

    /// <summary>Creates the service control endpoint with no automatic event-log payload capture.</summary>
    internal CredentialService() { ServiceName = "LauraCredentialBroker"; CanStop = true; AutoLog = false; }

    /// <inheritdoc />
    protected override void OnStart(string[] args)
    {
        _stop = new CancellationTokenSource();
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "LauraCredentialBroker");
        _worker = BrokerHost.RunAsync(root, _stop.Token);
        _ = StopOnFailureAsync(_worker);
    }

    /// <summary>Stops SCM service on a fatal listener failure instead of reporting a healthy dead broker.</summary>
    private async Task StopOnFailureAsync(Task worker)
    {
        try { await worker.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch (Exception) { ExitCode = 1; Stop(); }
    }

    /// <inheritdoc />
    protected override void OnStop()
    {
        _stop?.Cancel();
        if (_worker is not null) { try { _worker.GetAwaiter().GetResult(); } catch (OperationCanceledException) { } }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing) { _stop?.Cancel(); _stop?.Dispose(); }
        base.Dispose(disposing);
    }
}
