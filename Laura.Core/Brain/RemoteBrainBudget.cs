namespace Laura.Core.Brain;

/// <summary>Shared hard acquisition limits, including metadata and redirects; no automatic rate-limit retries.</summary>
public sealed class RemoteBrainBudget
{
    /// <summary>Gets the maximum HTTP calls per acquisition.</summary>
    public const int MaximumRequests = 40;
    /// <summary>Gets the maximum received bytes including API encoding and inventory.</summary>
    public const int MaximumBytes = 2 * 1024 * 1024;
    /// <summary>Gets the observed HTTP request count.</summary>
    public int Requests { get; private set; }
    /// <summary>Gets the observed received byte count.</summary>
    public int Bytes { get; private set; }
    /// <summary>Charges a request before dispatch.</summary>
    public void Request() { if (++Requests > MaximumRequests) { throw new IOException("Repository request budget exhausted."); } }
    /// <summary>Charges each received chunk before it can enter a buffer.</summary>
    public void Receive(int bytes) { Bytes = checked(Bytes + bytes); if (Bytes > MaximumBytes) { throw new IOException("Repository byte budget exhausted."); } }
}
