using System.Threading.Channels;
using Laura.Core.Memory;
using Microsoft.Extensions.Logging;

namespace Laura.Core.Brain;

public sealed partial class BrainLearning
{
    private readonly HashSet<string> _scheduledReceipts = new(StringComparer.Ordinal);
    private SqliteConversationMemory? _journal;
    private Task? _recovery;
    private bool _recovering, _recoveryRequested;
    /// <summary>Replays only unacknowledged selected transcript receipts into the existing bounded worker. Receipts survive failed attempts and restart.</summary>
    public void RecoverPending(SqliteConversationMemory journal)
    {
        ArgumentNullException.ThrowIfNull(journal);
        lock (_workerGate)
        {
            if (_stopping || !settings.Current.RememberConversations || !settings.Current.Brain.AutoLearn) { return; }
            _journal = journal; _recoveryRequested = true;
            if (!_recovering) { _recovering = true; _recovery = Task.Run(FeedJournalAsync); }
        }
    }
    private async Task FeedJournalAsync()
    {
        try
        {
            while (true)
            {
                SqliteConversationMemory journal; string vault;
                lock (_workerGate)
                {
                    if (_stopping || !_recoveryRequested) { _recovering = false; return; }
                    _recoveryRequested = false; journal = _journal!; vault = brain.Location;
                }
                string identity = FileBrainStorage.Hash(vault);
                IReadOnlyList<PendingBrainLearning> pending = journal.ReadPendingLearning(identity);
                var retained = pending.Select(item => identity + ":" + item.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToHashSet(StringComparer.Ordinal);
                lock (_workerGate) { _scheduledReceipts.RemoveWhere(key => !retained.Contains(key)); }
                foreach (PendingBrainLearning item in pending)
                {
                    lock (_workerGate)
                    {
                        if (_stopping) { _recovering = false; return; }
                        if (!_scheduledReceipts.Add(identity + ":" + item.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))) { continue; }
                        StartWorker();
                    }
                    await _queue.Writer.WriteAsync(new(item.Text, vault, Journal: journal, Receipt: item.Id), _lifetime.Token).ConfigureAwait(false);
                    Interlocked.Increment(ref _accepted);
                }
            }
        }
        catch (Exception error) when (error is ChannelClosedException or OperationCanceledException or IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        {
            lock (_workerGate) { _recovering = false; }
            if (error is not (ChannelClosedException or OperationCanceledException)) { logger.LogWarning("Brain receipt recovery unavailable ({ErrorType}); pending receipts were retained.", error.GetType().Name); }
        }
    }
}
