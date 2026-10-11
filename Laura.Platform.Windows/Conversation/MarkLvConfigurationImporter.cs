using System.Text.Json;
using Laura.Core.Abstractions;
using Laura.Core.Configuration;

namespace Laura.Platform.Windows.Conversation;

/// <summary>Imports only the credential, native voice and user name from an explicitly selected Mark-LV configuration.</summary>
public sealed class MarkLvConfigurationImporter(IApiKeyStore keys, ISettingsService settings)
{
    /// <summary>Reads the reference configuration without editing it, then protects the key in Laura's storage.</summary>
    public async Task ImportAsync(string path, CancellationToken cancellationToken = default)
    {
        using JsonDocument config = JsonDocument.Parse(await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false));
        string key = config.RootElement.GetProperty("gemini_api_key").GetString() ?? "";
        if (string.IsNullOrWhiteSpace(key)) { throw new InvalidOperationException("The selected file has no Google API key."); }
        string voice = config.RootElement.TryGetProperty("voice_name", out JsonElement value) ? value.GetString() ?? "Charon" : "Charon";
        if (!GeminiLiveOptions.Voices.Contains(voice, StringComparer.Ordinal)) { voice = "Charon"; }
        string nickname = config.RootElement.TryGetProperty("user_name", out value) ? value.GetString() ?? "" : "";
        keys.Save(key);
        await settings.UpdateAsync(settings.Current with
        {
            UserNickname = string.IsNullOrWhiteSpace(nickname) ? settings.Current.UserNickname : nickname,
            GenerativeAi = settings.Current.GenerativeAi with { Enabled = true, Provider = "gemini", Model = GeminiLiveOptions.Model, LiveVoice = voice },
        }, cancellationToken).ConfigureAwait(false);
    }
}
