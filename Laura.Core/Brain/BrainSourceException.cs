namespace Laura.Core.Brain;

/// <summary>A safe native acquisition diagnostic, containing no remote error body or credentials.</summary>
public sealed class BrainSourceException(string message, string category = "source_failure") : IOException(message)
{
    /// <summary>Gets a closed payload-free category independent from safe explanatory text.</summary>
    public string Category { get; } = category is "rate_limit" or "unavailable" or "response_bound" or "endpoint_policy" or "integrity" or "redirect_limit" ? category : "source_failure";
}
