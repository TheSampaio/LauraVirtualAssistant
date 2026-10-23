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
    /// <summary>Gets explicit permission to route exceptional Max tasks through the verified included plan.</summary>
    public bool AllowMax { get; init; }
    /// <summary>Gets the separately probed account model selected for exceptional Max work.</summary>
    public string MaxModel { get; init; } = "";
    /// <summary>Gets the native registration for which the owner saved these disclosure and routing choices.</summary>
    public Guid? Registration { get; init; }
    /// <summary>Validates native options without enabling access or inventing model capabilities.</summary>
    public ChatGptOptions Sanitized() => this with
    {
        Model = ValidModel(Model),
        MaxModel = ValidModel(MaxModel)
    };

    /// <summary>Preserves only bounded account model identifiers supplied by the broker.</summary>
    private static string ValidModel(string? model) => model?.Trim() is { Length: > 0 and <= 256 } value
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' or ':') ? value : "";
}

/// <summary>Explicit native provider choice; manual modes take precedence over all task-routing signals.</summary>
public enum ConversationProviderSelection
{
    /// <summary>Use the optional account-authorized ChatGPT text provider.</summary>
    ChatGpt,
    /// <summary>Prefer a capable local model and consider only explicitly authorized text providers.</summary>
    Automatic,
    /// <summary>Use only the explicitly enabled app-managed local model.</summary>
    Local,
    /// <summary>Use only the explicitly enabled Google AI Studio connection.</summary>
    Gemini
}
