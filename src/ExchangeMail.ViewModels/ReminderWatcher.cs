using ExchangeMail.Core.Ews;
using ExchangeMail.Core.Models;

namespace ExchangeMail.ViewModels;

/// <summary>A reminder that is due, with the online meeting to join when the event links to one.</summary>
public sealed record DueReminder(EventReminder Reminder, string? JoinUrl)
{
    /// <summary>Identifies this showing of the reminder; a snoozed reminder comes back with a new key.</summary>
    public string Key => ReminderWatcher.KeyOf(Reminder);
}

/// <summary>
/// Reads the mailbox's reminders every minute and reports the ones that are due, once each, so they can be shown as
/// notifications. Dismissing and snoozing go to Exchange, so Outlook and phones agree. Reminders of events that are
/// already over are left to Outlook.
/// </summary>
public sealed class ReminderWatcher(EwsClient client, Func<IEnumerable<string>> meetingHosts)
{
    private readonly Dictionary<string, DueReminder> _shown = [];

    public TimeSpan Interval { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>The clock; tests set it.</summary>
    public Func<DateTimeOffset> Now { get; init; } = () => DateTimeOffset.Now;

    /// <summary>A reminder became due.</summary>
    public event EventHandler<DueReminder>? Due;

    /// <summary>A reminder that was reported is no longer due (dismissed or snoozed, here or in Outlook); the argument is its key.</summary>
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
        var due = reminders.Where(r => r.IsCalendar && r.ReminderTime <= now && r.End > now)
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
            Due?.Invoke(this, shown);
        }
    }

    public async Task DismissAsync(EwsItemId id)
    {
        await client.DismissReminderAsync(id);
        Forget(id);
    }

    public async Task SnoozeAsync(EwsItemId id, TimeSpan delay)
    {
        await client.SnoozeReminderAsync(id, Now() + delay);
        Forget(id); // comes back as due with its new reminder time
    }

    internal static string KeyOf(EventReminder r) => $"{r.ItemId.Id}|{r.ReminderTime.UtcTicks}";

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
