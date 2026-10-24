using System.Security.Cryptography;
using System.Text.Json;
using Laura.Core.Abstractions;
using Laura.Core.Configuration;

namespace Laura.Platform.Windows.Conversation;

/// <summary>Restores a user-selected AI Studio credential from Mark-LV without changing the source.</summary>
public sealed class MarkLvConfigurationImporter(IApiKeyStore keys, ISettingsService settings, GeminiLiveClient gemini)
{
    /// <summary>Validates the candidate through Gemini Live, protects it, and selects Gemini for Light work.</summary>
    public async Task ImportAsync(string path, CancellationToken cancellationToken = default)
    {
        byte[] before = SHA256.HashData(await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false));
        using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false));
        string key = document.RootElement.GetProperty("gemini_api_key").GetString()?.Trim() ?? "";
        if (key.Length == 0) { throw new InvalidOperationException("The selected configuration has no AI Studio credential."); }
        GenerativeAiOptions options = settings.Current.GenerativeAi with
        {
            Enabled = true,
            Provider = "gemini",
            LightProvider = LightProviderSelection.Gemini
        };
        await gemini.ValidateCredentialAsync(key, options, settings.Current.Culture, cancellationToken).ConfigureAwait(false);
        byte[] after = SHA256.HashData(await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false));
        if (!CryptographicOperations.FixedTimeEquals(before, after))
        { throw new IOException("The selected configuration changed during import."); }
        keys.Save(key);
        await settings.UpdateAsync(current => current with
        {
            GenerativeAi = options,
            ProviderSelection = ConversationProviderSelection.Automatic,
            LocalModel = current.LocalModel with { Enabled = false }
        }, cancellationToken).ConfigureAwait(false);
    }
}
