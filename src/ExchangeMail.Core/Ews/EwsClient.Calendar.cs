using System.Globalization;
using System.Xml.Linq;
using ExchangeMail.Core.Models;
using static ExchangeMail.Core.Ews.Xml;

namespace ExchangeMail.Core.Ews;

public sealed partial class EwsClient
{
    private static readonly string[] CalendarSummaryFields =
    [
        "item:Subject", "calendar:Start", "calendar:End", "calendar:IsAllDayEvent", "calendar:Location",
        "calendar:Organizer", "calendar:MyResponseType", "calendar:IsMeeting", "calendar:IsCancelled",
        "calendar:IsRecurring", "calendar:LegacyFreeBusyStatus",
    ];

    /// <summary>Returns calendar items overlapping [start, end); recurring meetings are expanded into occurrences.</summary>
    public async Task<IReadOnlyList<CalendarEvent>> FindAppointmentsAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken ct = default)
    {
        var request = M("FindItem", new XAttribute("Traversal", "Shallow"),
            M("ItemShape",
                T("BaseShape", "IdOnly"),
                T("AdditionalProperties", CalendarSummaryFields.Select(Field))),
            M("CalendarView",
                new XAttribute("MaxEntriesReturned", 1000),
                new XAttribute("StartDate", EwsDate(start)),
                new XAttribute("EndDate", EwsDate(end))),
            M("ParentFolderIds", DistinguishedFolder("calendar")));

        var root = Single(await ExecuteAsync(request, ct, withTimeZone: true)).Element(MNs + "RootFolder");
        return root?.Element(TNs + "Items")?.Elements().Select(ParseCalendarEvent).OrderBy(e => e.Start).ToList() ?? [];
    }

    public async Task<CalendarEventDetail> GetAppointmentAsync(EwsItemId id, CancellationToken ct = default)
    {
        var request = M("GetItem",
            M("ItemShape",
                T("BaseShape", "IdOnly"),
                T("BodyType", "HTML"),
                T("FilterHtmlContent", "true"),
                T("AdditionalProperties",
                    CalendarSummaryFields
                        .Concat(["item:Body", "calendar:RequiredAttendees", "calendar:OptionalAttendees", "item:ReminderIsSet", "item:ReminderMinutesBeforeStart"])
                        .Select(Field))),
            M("ItemIds", ItemIdElement(id)));

        var item = Single(await ExecuteAsync(request, ct, withTimeZone: true)).Element(MNs + "Items")?.Elements().FirstOrDefault()
                   ?? throw new EwsException("Etkinlik bulunamadı.", "ErrorItemNotFound");
        var body = item.Element(TNs + "Body");

        return new CalendarEventDetail(
            ParseCalendarEvent(item),
            body?.Value ?? "",
            !string.Equals((string?)body?.Attribute("BodyType"), "Text", StringComparison.OrdinalIgnoreCase),
            ParseAttendees(item.Element(TNs + "RequiredAttendees")),
            ParseAttendees(item.Element(TNs + "OptionalAttendees")),
            (bool?)item.Element(TNs + "ReminderIsSet") == true ? (int?)item.Element(TNs + "ReminderMinutesBeforeStart") ?? 15 : null);
    }

    /// <summary>
    /// Updates an appointment or a meeting you organize. With <paramref name="notifyAttendees"/> attendees receive the update.
    /// The body and the attendee list are only written when the caller says they changed, so formatting and
    /// optional attendees set in Outlook are preserved.
    /// </summary>
    public async Task UpdateAppointmentAsync(EwsItemId id, NewAppointment appointment, bool updateBody, bool updateAttendees,
        bool notifyAttendees, CancellationToken ct = default)
    {
        if (appointment.End <= appointment.Start)
            throw new ArgumentException("Bitiş zamanı başlangıçtan sonra olmalı.", nameof(appointment));

        var updates = new List<XElement>
        {
            SetCalendarField("item:Subject", T("Subject", appointment.Subject)),
            SetCalendarField("item:ReminderIsSet", T("ReminderIsSet", appointment.ReminderMinutes is null ? "false" : "true")),
            SetCalendarField("calendar:Start", T("Start", EwsDate(appointment.Start))),
            SetCalendarField("calendar:End", T("End", EwsDate(appointment.End))),
            SetCalendarField("calendar:IsAllDayEvent", T("IsAllDayEvent", appointment.IsAllDay ? "true" : "false")),
            SetCalendarField("calendar:LegacyFreeBusyStatus", T("LegacyFreeBusyStatus", appointment.FreeBusy.ToString())),
            string.IsNullOrWhiteSpace(appointment.Location)
                ? T("DeleteItemField", Field("calendar:Location"))
                : SetCalendarField("calendar:Location", T("Location", appointment.Location)),
        };
        if (appointment.ReminderMinutes is { } minutes)
            updates.Add(SetCalendarField("item:ReminderMinutesBeforeStart", T("ReminderMinutesBeforeStart", minutes)));
        if (updateBody)
            updates.Add(SetCalendarField("item:Body", T("Body", new XAttribute("BodyType", "HTML"), appointment.HtmlBody)));
        if (updateAttendees)
            updates.Add(appointment.RequiredAttendees.Count > 0
                ? SetCalendarField("calendar:RequiredAttendees", T("RequiredAttendees", appointment.RequiredAttendees.Select(a => T("Attendee", MailboxElement(a)))))
                : T("DeleteItemField", Field("calendar:RequiredAttendees")));

        await UpdateCalendarItemAsync(id, updates, notifyAttendees, ct);
    }

    /// <summary>Moves an appointment to a new time (drag and drop in the calendar grid).</summary>
    public Task MoveAppointmentAsync(EwsItemId id, DateTimeOffset start, DateTimeOffset end, bool notifyAttendees, CancellationToken ct = default) =>
        UpdateCalendarItemAsync(id,
        [
            SetCalendarField("calendar:Start", T("Start", EwsDate(start))),
            SetCalendarField("calendar:End", T("End", EwsDate(end))),
        ], notifyAttendees, ct);

    private async Task UpdateCalendarItemAsync(EwsItemId id, IEnumerable<XElement> updates, bool notifyAttendees, CancellationToken ct)
    {
        var request = M("UpdateItem",
            new XAttribute("ConflictResolution", "AlwaysOverwrite"),
            new XAttribute("SendMeetingInvitationsOrCancellations", notifyAttendees ? "SendToAllAndSaveCopy" : "SendToNone"),
            M("ItemChanges", T("ItemChange", ItemIdElement(id with { ChangeKey = null }), T("Updates", updates))));
        foreach (var m in ResponseMessages(await ExecuteAsync(request, ct, withTimeZone: true))) Ok(m);
    }

    private static XElement SetCalendarField(string uri, XElement value) => T("SetItemField", Field(uri), T("CalendarItem", value));

    /// <summary>Free/busy blocks of the given people (and rooms) between <paramref name="start"/> and <paramref name="end"/>.</summary>
    public async Task<IReadOnlyList<AttendeeAvailability>> GetAvailabilityAsync(IReadOnlyList<string> addresses, DateTimeOffset start,
        DateTimeOffset end, CancellationToken ct = default)
    {
        if (addresses.Count == 0) return [];

        // The legacy TimeZone element is mandatory; describing UTC (no bias, no daylight saving) lets us exchange UTC times.
        static XElement UtcRule(string name) =>
            T(name, T("Bias", 0), T("Time", "00:00:00"), T("DayOrder", 1), T("Month", 1), T("DayOfWeek", "Sunday"));

        var request = M("GetUserAvailabilityRequest",
            T("TimeZone", T("Bias", 0), UtcRule("StandardTime"), UtcRule("DaylightTime")),
            M("MailboxDataArray", addresses.Select(a => T("MailboxData",
                T("Email", T("Address", a)),
                T("AttendeeType", "Required"),
                T("ExcludeConflicts", "false")))),
            T("FreeBusyViewOptions",
                T("TimeWindow", T("StartTime", UtcWallClock(start)), T("EndTime", UtcWallClock(end))),
                T("MergedFreeBusyIntervalInMinutes", 30),
                T("RequestedView", "FreeBusy")));

        var response = await ExecuteAsync(request, ct);
        var results = response.Descendants().Where(e => e.Name.LocalName == "FreeBusyResponse").ToList();
        var list = new List<AttendeeAvailability>();
        for (var i = 0; i < addresses.Count; i++)
        {
            var r = i < results.Count ? results[i] : null;
            var message = r?.Elements().FirstOrDefault(e => e.Name.LocalName == "ResponseMessage");
            if (r is null || (string?)message?.Attribute("ResponseClass") == "Error")
            {
                var text = message?.Elements().FirstOrDefault(e => e.Name.LocalName == "MessageText")?.Value;
                list.Add(new AttendeeAvailability(addresses[i], [], text ?? "Uygunluk bilgisi alınamadı."));
                continue;
            }
            var busy = r.Descendants(TNs + "CalendarEvent").Select(e => new BusyInterval(
                ParseUtcWallClock((string)e.Element(TNs + "StartTime")!),
                ParseUtcWallClock((string)e.Element(TNs + "EndTime")!),
                Enum.TryParse<FreeBusyStatus>((string?)e.Element(TNs + "BusyType"), out var status) ? status : FreeBusyStatus.Busy))
                .Where(b => b.Status != FreeBusyStatus.Free)
                .ToList();
            list.Add(new AttendeeAvailability(addresses[i], busy, null));
        }
        return list;
    }

    private static string UtcWallClock(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseUtcWallClock(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    /// <summary>Creates an appointment; when attendees are given it becomes a meeting and invitations are sent.</summary>
    public async Task<EwsItemId> CreateAppointmentAsync(NewAppointment appointment, CancellationToken ct = default)
    {
        if (appointment.End <= appointment.Start)
            throw new ArgumentException("Bitiş zamanı başlangıçtan sonra olmalı.", nameof(appointment));

        var isMeeting = appointment.RequiredAttendees.Count + appointment.OptionalAttendees.Count > 0;
        // Element order follows the EWS schema (ItemType fields, then CalendarItemType fields).
        var item = T("CalendarItem",
            T("Subject", appointment.Subject),
            T("Body", new XAttribute("BodyType", "HTML"), appointment.HtmlBody),
            T("ReminderIsSet", appointment.ReminderMinutes is null ? "false" : "true"),
            appointment.ReminderMinutes is { } minutes ? T("ReminderMinutesBeforeStart", minutes) : null,
            T("Start", EwsDate(appointment.Start)),
            T("End", EwsDate(appointment.End)),
            T("IsAllDayEvent", appointment.IsAllDay ? "true" : "false"),
            T("LegacyFreeBusyStatus", appointment.FreeBusy.ToString()),
            string.IsNullOrWhiteSpace(appointment.Location) ? null : T("Location", appointment.Location),
            Attendees("RequiredAttendees", appointment.RequiredAttendees),
            Attendees("OptionalAttendees", appointment.OptionalAttendees));

        var request = M("CreateItem",
            new XAttribute("SendMeetingInvitations", isMeeting ? "SendToAllAndSaveCopy" : "SendToNone"),
            M("SavedItemFolderId", DistinguishedFolder("calendar")),
            M("Items", item));

        var message = Single(await ExecuteAsync(request, ct, withTimeZone: true));
        return ParseItemId(message.Element(MNs + "Items")?.Elements().FirstOrDefault()?.Element(TNs + "ItemId"));

        static XElement? Attendees(string name, IList<string> addresses) =>
            addresses.Count == 0 ? null : T(name, addresses.Select(a => T("Attendee", MailboxElement(a))));
    }

    /// <summary>Deletes an appointment. For meetings you organize, attendees receive a cancellation.</summary>
    public async Task DeleteAppointmentAsync(EwsItemId id, bool sendCancellations, CancellationToken ct = default)
    {
        var request = M("DeleteItem",
            new XAttribute("DeleteType", "MoveToDeletedItems"),
            new XAttribute("SendMeetingCancellations", sendCancellations ? "SendToAllAndSaveCopy" : "SendToNone"),
            M("ItemIds", ItemIdElement(id with { ChangeKey = null })));
        foreach (var m in ResponseMessages(await ExecuteAsync(request, ct))) Ok(m);
    }

    /// <summary>Accepts, tentatively accepts or declines a meeting (calendar item or meeting request message).</summary>
    public async Task RespondToMeetingAsync(EwsItemId id, MeetingResponse response, bool sendResponse = true, CancellationToken ct = default)
    {
        var reference = await GetCurrentItemIdAsync(id, ct);
        var name = response switch
        {
            MeetingResponse.Accept => "AcceptItem",
            MeetingResponse.Tentative => "TentativelyAcceptItem",
            _ => "DeclineItem",
        };
        var request = M("CreateItem",
            new XAttribute("MessageDisposition", sendResponse ? "SendAndSaveCopy" : "SaveOnly"),
            M("Items", T(name, ItemIdElement(reference, "ReferenceItemId"))));
        Single(await ExecuteAsync(request, ct));
    }

    private static string EwsDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static CalendarEvent ParseCalendarEvent(XElement item) => new(
        ParseItemId(item.Element(TNs + "ItemId")),
        (string?)item.Element(TNs + "Subject") ?? "",
        (DateTimeOffset?)item.Element(TNs + "Start") ?? DateTimeOffset.MinValue,
        (DateTimeOffset?)item.Element(TNs + "End") ?? DateTimeOffset.MinValue,
        (bool?)item.Element(TNs + "IsAllDayEvent") ?? false,
        (string?)item.Element(TNs + "Location") ?? "",
        ParseMailbox(item.Element(TNs + "Organizer")?.Element(TNs + "Mailbox")),
        Enum.TryParse<ResponseType>((string?)item.Element(TNs + "MyResponseType"), out var response) ? response : ResponseType.Unknown,
        (bool?)item.Element(TNs + "IsMeeting") ?? false,
        (bool?)item.Element(TNs + "IsCancelled") ?? false,
        (bool?)item.Element(TNs + "IsRecurring") ?? false,
        Enum.TryParse<FreeBusyStatus>((string?)item.Element(TNs + "LegacyFreeBusyStatus"), out var fb) ? fb : FreeBusyStatus.Busy);

    private static IReadOnlyList<Attendee> ParseAttendees(XElement? list) =>
        list?.Elements(TNs + "Attendee")
            .Select(a => (Mailbox: ParseMailbox(a.Element(TNs + "Mailbox")), Response: (string?)a.Element(TNs + "ResponseType")))
            .Where(a => a.Mailbox is not null)
            .Select(a => new Attendee(a.Mailbox!, Enum.TryParse<ResponseType>(a.Response, out var r) ? r : ResponseType.Unknown))
            .ToList() ?? [];
}
