using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Laura.Core.Conversation;

/// <summary>Exports visible dialogue only, from a stable full retained snapshot, with UTF-8 and credential redaction.</summary>
public static partial class ConversationExport
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    /// <summary>Preserves bodies, Unicode and chronological order; credentials recognizable by format or exact configured value are removed.</summary>
    public static string Render(IReadOnlyList<ConversationMessage> snapshot, string format, IReadOnlyList<string>? knownSecrets = null)
    {
        ConversationMessage[] messages = snapshot.OrderBy(message => message.Timestamp).Select(message => message with { Text = Redact(message.Text, knownSecrets) }).ToArray();
        if (format == "json") { return JsonSerializer.Serialize(new { SchemaVersion = 1, Messages = messages }, JsonOptions); }
        if (format is not ("md" or "txt")) { throw new ArgumentException("Choose md, txt or json.", nameof(format)); }
        var text = new StringBuilder(format == "md" ? "# Conversa com Laura\n\n" : "Conversa com Laura\n\n");
        foreach (var message in messages)
        {
            string author = message.Source == ConversationMessageSource.Assistant ? "Laura" : message.Source == ConversationMessageSource.UserVoice ? "Você (voz)" : "Você";
            text.Append(format == "md" ? "## " : "").Append(message.Timestamp.ToString("yyyy-MM-dd HH:mm:ss zzz", System.Globalization.CultureInfo.InvariantCulture)).Append(" — ").Append(author).Append("\n\n");
            text.Append(message.Text).Append("\n\n");
        }
        return text.ToString();
    }
    /// <summary>Defense in depth for visible secrets; does not pretend to anonymize all personal dialogue.</summary>
    public static string Redact(string text, IReadOnlyList<string>? knownSecrets = null)
    {
        foreach (string secret in knownSecrets ?? []) { if (secret.Length >= 8) { text = text.Replace(secret, "[CREDENCIAL REMOVIDA]", StringComparison.Ordinal); } }
        text = PrivateKey().Replace(text, "[CHAVE PRIVADA REMOVIDA]");
        text = CredentialAssignment().Replace(text, "$1[CREDENCIAL REMOVIDA]");
        return Token().Replace(text, "[CREDENCIAL REMOVIDA]");
    }
    [GeneratedRegex(@"-----BEGIN (?:[A-Z ]+ )?PRIVATE KEY-----[\s\S]*?-----END (?:[A-Z ]+ )?PRIVATE KEY-----", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex PrivateKey();
    [GeneratedRegex("(?im)((?:api[_ -]?key|access[_ -]?token|refresh[_ -]?token|password|senha|authorization)[\"']?\\s*[:=]\\s*[\"']?)(?:Bearer\\s+)?[^\\s\"'`,;]+", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex CredentialAssignment();
    [GeneratedRegex(@"(?:\bsk-(?:proj-|svcacct-)?[A-Za-z0-9_-]{12,}|\bAIza[A-Za-z0-9_-]{20,}|\bBearer\s+[A-Za-z0-9._~+/-]{8,}=?|\beyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+)", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex Token();
}
