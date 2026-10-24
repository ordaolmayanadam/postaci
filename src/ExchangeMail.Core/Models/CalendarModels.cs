namespace ExchangeMail.Core.Models;

public enum ResponseType { Unknown, Organizer, Tentative, Accept, Decline, NoResponseReceived }

public enum FreeBusyStatus { Free, Tentative, Busy, OOF, WorkingElsewhere, NoData }

public enum MeetingResponse { Accept, Tentative, Decline }

public sealed record CalendarEvent(
    EwsItemId ItemId,
    string Subject,
    DateTimeOffset Start,
    DateTimeOffset End,
    bool IsAllDay,
    string Location,
    MailboxAddress? Organizer,
    ResponseType MyResponse,
    bool IsMeeting,
    bool IsCancelled,
    bool IsRecurring,
    FreeBusyStatus FreeBusy);

public sealed record Attendee(MailboxAddress Mailbox, ResponseType Response);

public sealed record CalendarEventDetail(
    CalendarEvent Event,
    string Body,
    bool IsHtml,
    IReadOnlyList<Attendee> RequiredAttendees,
    IReadOnlyList<Attendee> OptionalAttendees,
    int? ReminderMinutes = null);

public sealed record BusyInterval(DateTimeOffset Start, DateTimeOffset End, FreeBusyStatus Status);

/// <summary>Free/busy information of one attendee; <see cref="Error"/> is set when Exchange could not provide it.</summary>
public sealed record AttendeeAvailability(string Address, IReadOnlyList<BusyInterval> Busy, string? Error);

public sealed class NewAppointment
{
    public string Subject { get; init; } = "";
    public string Location { get; init; } = "";
    public DateTimeOffset Start { get; init; }
    public DateTimeOffset End { get; init; }
    public bool IsAllDay { get; init; }
    public string HtmlBody { get; init; } = "";
    public IList<string> RequiredAttendees { get; init; } = [];
    public IList<string> OptionalAttendees { get; init; } = [];
    /// <summary>Null means no reminder.</summary>
    public int? ReminderMinutes { get; init; } = 15;
    public FreeBusyStatus FreeBusy { get; init; } = FreeBusyStatus.Busy;
}
