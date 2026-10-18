namespace Laura.Core.Configuration;

/// <summary>Independent optional ChatGPT plan settings; all disclosure and automatic routing start disabled.</summary>
public sealed record ChatGptOptions
{
    /// <summary>Gets whether the owner enabled text inference after connecting.</summary>
    public bool Enabled { get; init; }
    /// <summary>Gets the account-discovered selected model; no model name is supplied by default.</summary>
    public string Model { get; init; } = "";
    /// <summary>Gets explicit permission to send bounded history, retrieved Brain context and native tool results to this recipient.</summary>
    public bool ShareContext { get; init; }
    /// <summary>Gets owner permission for structured task routing to ChatGPT in automatic mode.</summary>
    public bool AllowAutomatic { get; init; }
    /// <summary>Gets the owner's declaration that this application's additional-credit allowance is zero in ChatGPT Usage.</summary>
    public bool IncludedPlanOnly { get; init; }
    /// <summary>Gets the native registration for which the owner saved these disclosure and routing choices.</summary>
    public Guid? Registration { get; init; }
    /// <summary>Validates native options without enabling access or inventing model capabilities.</summary>
    public ChatGptOptions Sanitized() => this with
    {
        Model = Model?.Trim() is { Length: > 0 and <= 256 } value && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' or ':') ? value : ""
    };
}

/// <summary>Explicit native provider choice; manual modes take precedence over all task-routing signals.</summary>
public enum ConversationProviderSelection
{
    /// <summary>Use Gemini for conversation and compatible operations.</summary>
    Gemini,
    /// <summary>Use the optional account-authorized ChatGPT text provider.</summary>
    ChatGpt,
    /// <summary>Prefer Gemini and consider ChatGPT only with structural task demand and explicit owner permission.</summary>
    Automatic
}
