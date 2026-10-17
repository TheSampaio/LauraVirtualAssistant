using System.Text.Json;

namespace Laura.Core.Brain;

/// <summary>Reads an optional shared vault candidate without switching storage or migrating documents.</summary>
public static class SharedBrainConfiguration
{
    /// <summary>Returns a validated absolute candidate from bounded configuration data. Args: configPath: Explicit configuration file. Returns: Candidate or null. Raises: IOException for invalid configuration.</summary>
    public static string? ReadCandidate(string configPath)
    {
        if (!File.Exists(configPath)) { return null; }
        if (new FileInfo(configPath).Length > 16384) { throw new IOException("Shared Brain configuration exceeds the supported limit."); }
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(configPath));
            if (document.RootElement.ValueKind != JsonValueKind.Object) { throw new IOException("Shared Brain configuration must be an object."); }
            if (!document.RootElement.TryGetProperty("vaultPath", out JsonElement value)) { return null; }
            if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()) || !Path.IsPathFullyQualified(value.GetString()!))
            { throw new IOException("Shared Brain vaultPath must be an absolute path."); }
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(value.GetString()!));
        }
        catch (JsonException) { throw new IOException("Shared Brain configuration is invalid."); }
    }
}
