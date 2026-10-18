namespace Laura.Core.ChatGpt;

/// <summary>Contains only a reviewed symbolic failure and HTTP metadata; remote response text is never retained.</summary>
public sealed class ChatGptConnectionException : Exception
{
    /// <summary>Gets the native failure code for localized presentation.</summary>
    public string Code { get; }
    /// <summary>Gets the actual HTTP status, when an HTTP response was received.</summary>
    public int? HttpStatus { get; }
    /// <summary>Gets a bounded server correlation ID, without prompt or credential contents.</summary>
    public string RequestId { get; }
    /// <summary>Gets the observed response structure, without its private diagnostic text.</summary>
    public string BodyShape { get; }
    /// <summary>Creates a payload-free connection error. Args: code: Native symbolic category; httpStatus: Actual status; requestId: Server correlation ID; bodyShape: Observed JSON envelope.</summary>
    public ChatGptConnectionException(string code, int? httpStatus = null, string requestId = "", string bodyShape = "") : base(code)
    { Code = code; HttpStatus = httpStatus; RequestId = requestId; BodyShape = bodyShape; }
}
