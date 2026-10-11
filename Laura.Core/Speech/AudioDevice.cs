namespace Laura.Core.Speech;

/// <summary>One enumerated audio device.</summary>
/// <param name="Id">Windows audio device index.</param>
/// <param name="Name">Human-readable device name.</param>
public sealed record AudioDevice(int Id, string Name);
