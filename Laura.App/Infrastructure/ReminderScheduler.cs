using System.Text.Json;
using Laura.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Laura.App.Infrastructure;

/// <summary>Persists explicitly requested reminders and speaks them through Gemini while Laura is running.</summary>
public sealed class ReminderScheduler(AssistantStorage storage, IServiceProvider services) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly string _path = Path.Combine(storage.Directory, "reminders.json");
    private List<Reminder> _reminders = [];
    private Task? _worker;
    private DateTimeOffset _retryAfter;
    /// <summary>Loads existing reminders without creating schedules.</summary>
    public async Task StartAsync()
    {
        if (_worker is not null) { return; }
        if (File.Exists(_path)) { _reminders = JsonSerializer.Deserialize<List<Reminder>>(await File.ReadAllTextAsync(_path).ConfigureAwait(false)) ?? []; }
        _worker = RunAsync(_lifetime.Token);
    }
    /// <summary>Adds a bounded reminder with a future, offset-aware due time.</summary>
    public async Task<Reminder> AddAsync(string text, DateTimeOffset due, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 4000 || due <= DateTimeOffset.UtcNow) { throw new ArgumentException("Provide a future time and bounded reminder text.", nameof(text)); }
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try { if (_reminders.Count >= 100) { throw new InvalidOperationException("Reminder limit reached."); } var reminder = new Reminder(Guid.NewGuid().ToString("N"), text, due); _reminders.Add(reminder); await SaveAsync(token).ConfigureAwait(false); return reminder; }
        finally { _gate.Release(); }
    }
    /// <summary>Lists or cancels a reminder without blocking the UI.</summary>
    public async Task<IReadOnlyList<Reminder>> ListAsync(string? remove, CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try { if (remove is not null) { _reminders.RemoveAll(reminder => reminder.Id == remove); await SaveAsync(token).ConfigureAwait(false); } return _reminders.ToArray(); }
        finally { _gate.Release(); }
    }
    /// <summary>Atomically replaces the schedule file in the selected data root.</summary>
    private async Task SaveAsync(CancellationToken token)
    { Directory.CreateDirectory(storage.Directory); await File.WriteAllTextAsync(_path + ".tmp", JsonSerializer.Serialize(_reminders), token).ConfigureAwait(false); File.Move(_path + ".tmp", _path, true); }
    /// <summary>Queues due notifications only while native voice is available and the user is not speaking.</summary>
    private async Task RunAsync(CancellationToken token)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                if (DateTimeOffset.UtcNow < _retryAfter) { continue; }
                Reminder[] due; await _gate.WaitAsync(token).ConfigureAwait(false);
                try { due = _reminders.Where(reminder => reminder.Due <= DateTimeOffset.UtcNow).ToArray(); } finally { _gate.Release(); }
                foreach (Reminder reminder in due)
                {
                    var live = services.GetRequiredService<Laura.Platform.Windows.Conversation.GeminiLiveClient>();
                    if (!live.IsAvailable || live.IsListening || live.IsSpeaking) { continue; }
                    try { await live.SpeakAsync(reminder.Text, token).ConfigureAwait(false); await ListAsync(reminder.Id, token).ConfigureAwait(false); }
                    catch (Exception error) when (error is InvalidOperationException or IOException or System.Net.WebSockets.WebSocketException or OperationCanceledException)
                    { token.ThrowIfCancellationRequested(); _retryAfter = DateTimeOffset.UtcNow.AddMinutes(1); break; }
                }
            }
        }
        catch (OperationCanceledException) { }
    }
    /// <inheritdoc />
    public async ValueTask DisposeAsync() { await _lifetime.CancelAsync().ConfigureAwait(false); if (_worker is not null) { await _worker.ConfigureAwait(false); } _lifetime.Dispose(); _gate.Dispose(); }
}

/// <summary>Stores one user reminder and its due time.</summary>
public sealed record Reminder(string Id, string Text, DateTimeOffset Due);
