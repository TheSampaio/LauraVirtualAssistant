using Laura.Core.Abstractions;
using Laura.Core.Configuration;
using Laura.Core.Diagnostics;

namespace Laura.App.Infrastructure;

/// <summary>Records a fixed whitelist of configuration modes without paths, personas, names, device labels or keys.</summary>
public sealed class RuntimeDiagnosticContext : IDisposable
{
    private readonly ISettingsService _settings;
    private readonly RuntimeJournal _journal;
    /// <summary>Attaches content-free configuration diagnostics during the application lifetime.</summary>
    public RuntimeDiagnosticContext(ISettingsService settings, RuntimeJournal journal)
    {
        _settings = settings; _journal = journal; settings.Changed += Changed;
        journal.Record("runtime.version", outcome: typeof(RuntimeDiagnosticContext).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion.Replace('+', '-') ?? "unknown");
        Changed(null, settings.Current);
    }
    private void Changed(object? sender, LauraSettings value)
    {
        _journal.Record("config.history", outcome: value.RememberConversations ? "enabled" : "disabled");
        _journal.Record("config.brain_learning", outcome: value.Brain.AutoLearn ? "enabled" : "disabled");
        _journal.Record("config.semantic_configured", outcome: value.Brain.EmbeddingModel.Length > 0 ? "yes" : "no");
        _journal.Record("config.context_budget", amount: value.Brain.ContextCharacters);
        _journal.Record("config.provider", outcome: value.GenerativeAi.Enabled ? "gemini" : "disabled");
        _journal.Record("config.model", outcome: value.GenerativeAi.Model.Length == 0 ? "default" : GeminiLiveOptions.HasVerifiedImageSupport(value.GenerativeAi.Model) ? "reviewed" : "custom");
        _journal.Record("config.voice", outcome: value.GenerativeAi.LiveVoice);
        _journal.Record("config.request_timeout_seconds", amount: (long)value.GenerativeAi.Timeout.TotalSeconds);
        _journal.Record("config.microphone_muted", outcome: value.MicrophoneMuted ? "yes" : "no");
        _journal.Record("config.clap_wake", outcome: value.ClapWakeEnabled ? "enabled" : "disabled");
        _journal.Record("config.screenshot_shortcuts", outcome: value.Screenshots.ShortcutsEnabled ? "enabled" : "disabled");
    }
    /// <inheritdoc />
    public void Dispose() => _settings.Changed -= Changed;
}
