using ExchangeMail.Core.Ews;
using ExchangeMail.Core.Models;

namespace ExchangeMail.ViewModels;

/// <summary>A reminder that is due, with the online meeting to join when the event links to one.</summary>
public sealed record DueReminder(EventReminder Reminder, string? JoinUrl)
{
    /// <summary>Identifies the reminder of this event (occurrence), whatever reminder time Exchange reports for it.</summary>
    public string Key => ReminderWatcher.KeyOf(Reminder);
}

/// <summary>
/// Reads the mailbox's reminders every minute and reports the ones that are due, once each, so they can be shown as
/// notifications. Dismissing and snoozing go to Exchange, so Outlook and phones agree, and are also remembered here:
/// a reminder the user dismissed or snoozed is not shown again (before its snooze ends) even if Exchange still
/// lists it. Reminders of events that are already over are left to Outlook.
/// </summary>
public sealed class ReminderWatcher(EwsClient client, Func<IEnumerable<string>> meetingHosts)
{
    private readonly Dictionary<string, DueReminder> _shown = [];
    private readonly HashSet<string> _dismissed = [];
    private readonly Dictionary<string, DateTimeOffset> _snoozedUntil = [];

    public TimeSpan Interval { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>The clock; tests set it.</summary>
    public Func<DateTimeOffset> Now { get; init; } = () => DateTimeOffset.Now;

    /// <summary>A reminder became due.</summary>
    public event EventHandler<DueReminder>? Due;

    /// <summary>A reminder that was reported is no longer due (dismissed or snoozed elsewhere, in Outlook); the argument is its key.</summary>
    public event EventHandler<string>? Gone;

    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await CheckAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (ex is EwsException or HttpRequestException or IOException)
            {
                Diagnostics.Log("Reminders", ex); // the next check tries again
            }

            try
            {
                await Task.Delay(Interval, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>One check: reports newly due reminders and the ones that went away.</summary>
    public async Task CheckAsync(CancellationToken ct = default)
    {
        var now = Now();
        var reminders = await client.GetRemindersAsync(now.AddDays(-1), now.AddDays(1), ct);
        var due = reminders
            .Where(r => r.IsCalendar && r.ReminderTime <= now + Interval && r.End > now
                && !_dismissed.Contains(r.ItemId.Id)
                && !(_snoozedUntil.TryGetValue(r.ItemId.Id, out var until) && until > now))
            .GroupBy(KeyOf).ToDictionary(g => g.Key, g => g.First());

        foreach (var key in _shown.Keys.Except(due.Keys).ToList())
        {
            _shown.Remove(key);
            Gone?.Invoke(this, key);
        }
        foreach (var (key, reminder) in due)
        {
            if (_shown.ContainsKey(key)) continue;
            var shown = new DueReminder(reminder, await FindJoinUrlAsync(reminder, ct));
            _shown[key] = shown;
            // Times only: the subject stays out of the log.
            Diagnostics.Log($"Reminder due: reminder {reminder.ReminderTime:u}, start {reminder.Start:u}, {reminders.Count} current");
            Due?.Invoke(this, shown);
        }
    }

    /// <summary>Dismisses the reminder in Exchange; it is not shown again here either way.</summary>
    public async Task DismissAsync(EwsItemId id)
    {
        _dismissed.Add(id.Id);
        Forget(id);
        var changed = await client.DismissReminderAsync(id);
        Diagnostics.Log($"Reminder dismissed, Exchange {(changed ? "updated the item" : "reported no change")}");
    }

    /// <summary>Snoozes the reminder in Exchange; it is shown again here when the delay is over.</summary>
    public async Task SnoozeAsync(EwsItemId id, TimeSpan delay)
    {
        _snoozedUntil[id.Id] = Now() + delay;
        Forget(id);
        var changed = await client.SnoozeReminderAsync(id, Now() + delay);
        Diagnostics.Log($"Reminder snoozed for {delay.TotalMinutes:0} min, Exchange {(changed ? "updated the item" : "reported no change")}");
    }

    /// <summary>
    /// The item and the start of the event: Exchange can report a different reminder time for the same reminder
    /// from one check to the next, which must not make it a new one.
    /// </summary>
    internal static string KeyOf(EventReminder r) => $"{r.ItemId.Id}|{r.Start.UtcTicks}";

    private void Forget(EwsItemId id)
    {
        foreach (var key in _shown.Where(p => p.Value.Reminder.ItemId.Id == id.Id).Select(p => p.Key).ToList()) _shown.Remove(key);
    }

    /// <summary>The meeting address, from the event's details (the reminder itself has only subject, place and time).</summary>
    private async Task<string?> FindJoinUrlAsync(EventReminder reminder, CancellationToken ct)
    {
        try
        {
            var detail = await client.GetAppointmentAsync(reminder.ItemId, ct);
            return MeetingLink.Find(detail.JoinUrl, detail.Event.Location, detail.Body, meetingHosts());
        }
        catch (EwsException)
        {
            return MeetingLink.Find(null, reminder.Location, null, meetingHosts());
        }
    }
}
