using Laura.Core.Abstractions;
using Laura.Core.Conversation;

namespace Laura.App.Infrastructure;

/// <summary>Includes only actual saved reminders due in the next day.</summary>
public sealed class ReminderBriefingContributor(ReminderScheduler reminders, IClock clock) : IBriefingContributor
{
    /// <inheritdoc />
    public async Task<BriefingSection?> CollectAsync(CancellationToken cancellationToken)
    {
        var upcoming = (await reminders.ListAsync(null, cancellationToken).ConfigureAwait(false))
            .Where(reminder => reminder.Due >= clock.Now && reminder.Due <= clock.Now.AddDays(1)).OrderBy(reminder => reminder.Due).Take(3)
            .Select(reminder => new { reminder.Text, reminder.Due }).ToArray();
        return upcoming.Length == 0 ? null : new("Reminders", "Saved Laura reminders", clock.Now, upcoming);
    }
}
