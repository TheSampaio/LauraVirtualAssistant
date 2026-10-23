namespace Laura.Core.Models;

/// <summary>Native task demand tags; higher tiers do not imply billing or provider authorization.</summary>
public enum TaskDifficulty
{
    /// <summary>Registered deterministic execution without inference.</summary>
    Native,
    /// <summary>Routine conversation or bounded single-tool reasoning.</summary>
    Light,
    /// <summary>Moderate analysis or planning.</summary>
    Standard,
    /// <summary>Complex integration, debugging or multi-stage synthesis.</summary>
    Advanced,
    /// <summary>Exceptional depth requiring an explicitly configured budget.</summary>
    Max
}
