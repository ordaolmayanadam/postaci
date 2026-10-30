using System.Xml.Linq;
using ExchangeMail.Core.Models;
using static ExchangeMail.Core.Ews.Xml;

namespace ExchangeMail.Core.Ews;

public sealed partial class EwsClient
{
    /// <summary>
    /// The mailbox's reminders between <paramref name="begin"/> and <paramref name="end"/> that are not dismissed,
    /// as Outlook shows them; snoozed ones carry their new reminder time.
    /// </summary>
    public async Task<IReadOnlyList<EventReminder>> GetRemindersAsync(DateTimeOffset begin, DateTimeOffset end, CancellationToken ct = default)
    {
        var request = M("GetReminders",
            M("BeginTime", EwsDate(begin)),
            M("EndTime", EwsDate(end)),
            M("MaxItems", 100),
            M("ReminderType", "All"));

        var response = Ok(await ExecuteAsync(request, ct));
        return response.Element(MNs + "Reminders")?.Elements(TNs + "Reminder").Select(r => new EventReminder(
                ParseItemId(r.Element(TNs + "ItemId")),
                (string?)r.Element(TNs + "Subject") ?? "",
                (string?)r.Element(TNs + "Location") ?? "",
                (DateTimeOffset?)r.Element(TNs + "StartDate") ?? DateTimeOffset.MinValue,
                (DateTimeOffset?)r.Element(TNs + "EndDate") ?? DateTimeOffset.MinValue,
                (DateTimeOffset?)r.Element(TNs + "ReminderTime") ?? DateTimeOffset.MinValue,
                (string?)r.Element(TNs + "ReminderGroup") != "Task"))
            .ToList() ?? [];
    }

    /// <summary>Dismisses a reminder in the mailbox, so Outlook and phones no longer show it either.</summary>
    public Task DismissReminderAsync(EwsItemId id, CancellationToken ct = default) =>
        PerformReminderActionAsync(T("ReminderItemAction", T("ActionType", "Dismiss"), CurrentId(id)), ct);

    /// <summary>Snoozes a reminder in the mailbox until <paramref name="until"/>.</summary>
    public Task SnoozeReminderAsync(EwsItemId id, DateTimeOffset until, CancellationToken ct = default) =>
        PerformReminderActionAsync(T("ReminderItemAction", T("ActionType", "Snooze"), CurrentId(id), T("NewReminderTime", EwsDate(until))), ct);

    // Without the change key: a notification may be older than the latest change to the event.
    private static XElement CurrentId(EwsItemId id) => ItemIdElement(id with { ChangeKey = null });

    private async Task PerformReminderActionAsync(XElement action, CancellationToken ct)
    {
        var request = M("PerformReminderAction", M("ReminderItemActions", action));
        Ok(await ExecuteAsync(request, ct));
    }
}
