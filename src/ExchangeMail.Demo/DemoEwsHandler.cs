using System.Globalization;
using System.Net;
using System.Text;
using System.Xml.Linq;

namespace ExchangeMail.Demo;

/// <summary>
/// Answers the EWS operations Postacı sends from a <see cref="DemoMailbox"/>, in memory. Reading, marking read,
/// moving, deleting, creating events (also recurring ones) and changing them (subject, location, times; also of a
/// whole series) change the mailbox; everything else (sending, saving, responding) succeeds without effect.
/// </summary>
public sealed class DemoEwsHandler(DemoMailbox mailbox) : HttpMessageHandler
{
    private static readonly XNamespace Soap = "http://schemas.xmlsoap.org/soap/envelope/";
    private static readonly XNamespace TNs = "http://schemas.microsoft.com/exchange/services/2006/types";
    private static readonly XNamespace MNs = "http://schemas.microsoft.com/exchange/services/2006/messages";

    private readonly Lock _lock = new();

    public DemoMailbox Mailbox { get; } = mailbox;

    /// <summary>A server address for the client; requests never leave the process.</summary>
    public static Uri EwsUrl { get; } = new("https://mail.demo.example/EWS/Exchange.asmx");

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var envelope = XDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
        var operation = envelope.Root!.Element(Soap + "Body")!.Elements().First();

        // The streaming connection stays open with nothing to report, like a quiet mailbox.
        if (operation.Name.LocalName == "GetStreamingEvents")
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new SilentStream()) };

        XElement body;
        lock (_lock) body = Answer(operation);
        var response = new XDocument(new XElement(Soap + "Envelope",
            new XAttribute(XNamespace.Xmlns + "s", Soap), new XAttribute(XNamespace.Xmlns + "t", TNs), new XAttribute(XNamespace.Xmlns + "m", MNs),
            new XElement(Soap + "Body", body)));
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(response.ToString(SaveOptions.DisableFormatting), Encoding.UTF8, "text/xml"),
        };
    }

    private XElement Answer(XElement op) => op.Name.LocalName switch
    {
        "GetFolder" => Response(op, op.Element(MNs + "FolderIds")!.Elements().Select(GetFolder)),
        "FindFolder" => Response(op, [Success(op, M("RootFolder", new XAttribute("IncludesLastItemInRange", true),
            T("Folders", Mailbox.Folders.Where(f => f.ParentId is not null).Select(FolderElement))))]),
        "FindItem" => Response(op, [FindItem(op)]),
        "GetItem" => Response(op, op.Element(MNs + "ItemIds")!.Elements().Select(id => GetItem(op, ItemKey(id)))),
        "FindConversation" => FindConversation(op),
        "GetConversationItems" => Response(op, op.Element(MNs + "Conversations")!.Elements().Select(c => GetConversation(op, Id(c.Element(TNs + "ConversationId")!)))),
        "UpdateItem" => Response(op, op.Descendants(TNs + "ItemChange").Select(c => UpdateItem(op, c))),
        "DeleteItem" => Response(op, op.Element(MNs + "ItemIds")!.Elements().Select(id => DeleteItem(op, ItemKey(id)))),
        "MoveItem" => Response(op, op.Element(MNs + "ItemIds")!.Elements()
            .Select(id => MoveItem(op, Id(id), Id(op.Element(MNs + "ToFolderId")!.Elements().First())))),
        "CreateItem" => Response(op, [op.Element(MNs + "Items")?.Element(TNs + "CalendarItem") is { } calendarItem
            ? Success(op, M("Items", T("CalendarItem", ItemId(CreateEvents(calendarItem)))))
            : Success(op, M("Items", T("Message", ItemId(Mailbox.NewId("created")))))]),
        "SendItem" or "DeleteAttachment" => Response(op, [Success(op)]),
        "CreateAttachment" => Response(op, [Success(op)]),
        "GetAttachment" => Response(op, op.Element(MNs + "AttachmentIds")!.Elements().Select(a => GetAttachment(op, Id(a)))),
        "ResolveNames" => Response(op, [ResolveNames(op)]),
        "GetUserAvailabilityRequest" => Availability(op),
        "GetUserOofSettingsRequest" => M("GetUserOofSettingsResponse", ResponseMessage("ResponseMessage"),
            T("OofSettings", T("OofState", "Disabled"), T("ExternalAudience", "Known"),
                T("Duration", T("StartTime", Utc(Mailbox.Now)), T("EndTime", Utc(Mailbox.Now.AddDays(1)))),
                T("InternalReply", T("Message", "")), T("ExternalReply", T("Message", "")))),
        "SetUserOofSettingsRequest" => M("SetUserOofSettingsResponse", ResponseMessage("ResponseMessage")),
        "Subscribe" => Response(op, [Success(op, M("SubscriptionId", "demo-subscription"))]),
        "GetReminders" => GetReminders(op),
        "PerformReminderAction" => PerformReminderAction(op),
        _ => Response(op, [Error(op, "ErrorInvalidOperation", "The demo mailbox does not support " + op.Name.LocalName + ".")]),
    };

    #region Folders

    private XElement GetFolder(XElement id)
    {
        var key = Id(id) == "msgfolderroot" ? "root" : Id(id);
        var folder = Mailbox.Folders.FirstOrDefault(f => f.Id == key);
        return folder is null
            ? Error("GetFolder", "ErrorFolderNotFound", "The folder does not exist.")
            : Success("GetFolder", M("Folders", FolderElement(folder)));
    }

    private XElement FolderElement(DemoFolder folder)
    {
        var messages = Mailbox.Messages.Where(m => m.FolderId == folder.Id).ToList();
        return T("Folder",
            T("FolderId", new XAttribute("Id", folder.Id)),
            folder.ParentId is null ? null : T("ParentFolderId", new XAttribute("Id", folder.ParentId)),
            T("FolderClass", "IPF.Note"),
            T("DisplayName", folder.Name),
            T("TotalCount", messages.Count),
            T("UnreadCount", messages.Count(m => !m.IsRead)));
    }

    #endregion

    #region Items

    private XElement FindItem(XElement op)
    {
        var parent = op.Element(MNs + "ParentFolderIds")!.Elements().First();
        if (op.Element(MNs + "CalendarView") is { } view)
        {
            var start = DateTimeOffset.Parse((string)view.Attribute("StartDate")!, CultureInfo.InvariantCulture);
            var end = DateTimeOffset.Parse((string)view.Attribute("EndDate")!, CultureInfo.InvariantCulture);
            var events = Mailbox.Events.Where(e => e.Start < end && e.End > start).Select(EventElement);
            return Success(op, M("RootFolder", new XAttribute("TotalItemsInView", events.Count()), new XAttribute("IncludesLastItemInRange", true),
                T("Items", events)));
        }

        var query = ((string?)op.Element(MNs + "QueryString"))?.Trim();
        var (offset, size) = Paging(op);
        if (Id(parent) == "contacts")
        {
            var contacts = Mailbox.Contacts.Where(c => Matches(query, c.Person.Name, c.Person.Address, c.Company)).ToList();
            return Page(contacts.Skip(offset).Take(size).Select(ContactElement), contacts.Count, offset);
        }

        var messages = Mailbox.Messages.Where(m => m.FolderId == Id(parent) && Matches(query, m.Subject, m.From.Name, m.Preview))
            .OrderByDescending(m => m.Received).ToList();
        return Page(messages.Skip(offset).Take(size).Select(m => MessageElement(m, withBody: false)), messages.Count, offset);

        XElement Page(IEnumerable<XElement> items, int total, int from)
        {
            var list = items.ToList();
            return Success(op, M("RootFolder",
                new XAttribute("TotalItemsInView", total),
                new XAttribute("IndexedPagingOffset", from + list.Count),
                new XAttribute("IncludesLastItemInRange", from + list.Count >= total),
                T("Items", list)));
        }
    }

    private XElement GetItem(XElement op, string id)
    {
        if (Mailbox.Messages.FirstOrDefault(m => m.Id == id) is { } message)
            return Success(op, M("Items", MessageElement(message, withBody: true)));
        if (Mailbox.Events.FirstOrDefault(e => e.Id == id) is { } calendarItem)
            return Success(op, M("Items", EventElement(calendarItem)));
        if (SeriesMaster(id) is { } master)
            return Success(op, M("Items", EventElement(master)));
        if (Mailbox.Contacts.FirstOrDefault(c => c.Id == id) is { } contact)
            return Success(op, M("Items", ContactElement(contact)));
        return Error(op, "ErrorItemNotFound", "The item does not exist.");
    }

    private XElement UpdateItem(XElement op, XElement change)
    {
        var id = Id(change.Element(TNs + "ItemId")!);
        if (Mailbox.Messages.FirstOrDefault(m => m.Id == id) is { } message
            && change.Descendants(TNs + "IsRead").FirstOrDefault() is { } isRead)
            message.IsRead = (bool)isRead;

        var index = Mailbox.Events.FindIndex(e => e.Id == id);
        if (index >= 0) Mailbox.Events[index] = Changed(Mailbox.Events[index], change);
        else if (SeriesMaster(id) is { } master)
        {
            // The series' new start and end move every occurrence by the same time and give it the new duration.
            var updated = Changed(master, change);
            var shift = updated.Start - master.Start;
            for (var i = 0; i < Mailbox.Events.Count; i++)
            {
                var e = Mailbox.Events[i];
                if (e.SeriesId != master.SeriesId) continue;
                Mailbox.Events[i] = e with
                {
                    Subject = updated.Subject, Location = updated.Location,
                    Start = e.Start + shift, End = e.Start + shift + (updated.End - updated.Start),
                };
            }
        }
        return Success(op, M("Items", T("Message", ItemId(id))));
    }

    /// <summary>The event with the subject, location and times an UpdateItem change sets.</summary>
    private static DemoEvent Changed(DemoEvent e, XElement change)
    {
        string? Value(string name) => (string?)change.Descendants(TNs + name).FirstOrDefault();
        DateTimeOffset Time(string name, DateTimeOffset current) =>
            Value(name) is { } v ? DateTimeOffset.Parse(v, CultureInfo.InvariantCulture).ToOffset(current.Offset) : current;
        return e with
        {
            Subject = Value("Subject") ?? e.Subject,
            Location = Value("Location") ?? e.Location,
            Start = Time("Start", e.Start),
            End = Time("End", e.End),
        };
    }

    private const string SeriesPrefix = "series:";

    /// <summary>
    /// The id an element names: an item's own id, or for a RecurringMasterItemId the series of the occurrence, whose
    /// first occurrence stands for the series (as Exchange's recurring master does).
    /// </summary>
    private string ItemKey(XElement element)
    {
        if (element.Name.LocalName != "RecurringMasterItemId") return Id(element);
        var occurrenceId = (string?)element.Attribute("OccurrenceId");
        var series = Mailbox.Events.FirstOrDefault(e => e.Id == occurrenceId)?.SeriesId;
        return series is null ? "" : SeriesPrefix + series;
    }

    private DemoEvent? SeriesMaster(string id) =>
        id.StartsWith(SeriesPrefix, StringComparison.Ordinal)
            ? Mailbox.Events.Where(e => e.SeriesId == id[SeriesPrefix.Length..]).OrderBy(e => e.Start).FirstOrDefault() is { } first
                ? first with { Id = id }
                : null
            : null;

    private XElement DeleteItem(XElement op, string id)
    {
        if (Mailbox.Messages.FirstOrDefault(m => m.Id == id) is { } message)
        {
            if (message.FolderId == "deleteditems") Mailbox.Messages.Remove(message);
            else message.FolderId = "deleteditems";
        }
        Mailbox.Events.RemoveAll(e => e.Id == id || SeriesPrefix + e.SeriesId == id);
        return Success(op);
    }

    private XElement MoveItem(XElement op, string id, string folderId)
    {
        if (Mailbox.Messages.FirstOrDefault(m => m.Id == id) is { } message) message.FolderId = folderId;
        return Success(op, M("Items", T("Message", ItemId(id))));
    }

    private XElement GetAttachment(XElement op, string id)
    {
        var attachment = Mailbox.Messages.SelectMany(m => m.Attachments).FirstOrDefault(a => a.Id == id);
        if (attachment is null) return Error(op, "ErrorAttachmentNotFound", "The attachment does not exist.");
        return Success(op, M("Attachments", T("FileAttachment",
            T("AttachmentId", new XAttribute("Id", attachment.Id)),
            T("Name", attachment.Name),
            T("ContentType", attachment.ContentType),
            T("Content", Convert.ToBase64String(Encoding.UTF8.GetBytes("Postacı demo attachment"))))));
    }

    private XElement MessageElement(DemoMessage m, bool withBody) => T("Message",
        ItemId(m.Id),
        T("ParentFolderId", new XAttribute("Id", m.FolderId)),
        T("ItemClass", m.ItemClass),
        T("Subject", m.Subject),
        withBody ? T("Body", new XAttribute("BodyType", "HTML"), m.BodyHtml) : null,
        withBody ? T("UniqueBody", new XAttribute("BodyType", "HTML"), m.BodyHtml) : null,
        withBody && m.Attachments.Count > 0
            ? T("Attachments", m.Attachments.Select(a => T("FileAttachment",
                T("AttachmentId", new XAttribute("Id", a.Id)), T("Name", a.Name), T("ContentType", a.ContentType),
                T("Size", a.Size), T("IsInline", false))))
            : null,
        T("DateTimeReceived", Utc(m.Received)),
        T("DateTimeSent", Utc(m.Received)),
        T("HasAttachments", m.Attachments.Count > 0),
        T("Importance", m.Importance),
        T("DisplayTo", string.Join("; ", m.To.Select(p => p.Name))),
        T("Preview", m.Preview),
        T("From", MailboxElement(m.From)),
        T("ToRecipients", m.To.Select(MailboxElement)),
        m.Cc.Count > 0 ? T("CcRecipients", m.Cc.Select(MailboxElement)) : null,
        T("IsRead", m.IsRead));

    #endregion

    #region Conversations

    private XElement FindConversation(XElement op)
    {
        var folderId = Id(op.Element(MNs + "ParentFolderId")!.Elements().First());
        var (offset, size) = Paging(op);
        var conversations = Mailbox.Messages.Where(m => m.FolderId == folderId)
            .GroupBy(m => m.ConversationId)
            .Select(g => (Id: g.Key, InFolder: g.OrderByDescending(m => m.Received).ToList(), All: Thread(g.Key)))
            .OrderByDescending(c => c.All.Max(m => m.Received))
            .ToList();

        var page = conversations.Skip(offset).Take(size).Select(c => T("Conversation",
            T("ConversationId", new XAttribute("Id", c.Id)),
            T("ConversationTopic", Topic(c.All[0].Subject)),
            T("UniqueSenders", c.All.Select(m => m.From.Name).Distinct().Select(n => T("String", n))),
            T("UniqueRecipients", c.All.SelectMany(m => m.To).Select(p => p.Name).Distinct().Select(n => T("String", n))),
            T("LastDeliveryTime", Utc(c.All.Max(m => m.Received))),
            T("MessageCount", c.InFolder.Count),
            T("GlobalMessageCount", c.All.Count),
            T("UnreadCount", c.InFolder.Count(m => !m.IsRead)),
            T("HasAttachments", c.All.Any(m => m.Attachments.Count > 0)),
            T("Importance", c.InFolder.Any(m => m.Importance == "High") ? "High" : "Normal"),
            T("ItemIds", c.InFolder.Select(m => ItemId(m.Id)))));

        return M("FindConversationResponse", new XAttribute("ResponseClass", "Success"), M("ResponseCode", "NoError"),
            M("Conversations", page),
            M("TotalConversationsInView", conversations.Count),
            M("IndexedOffset", Math.Min(offset + size, conversations.Count)));
    }

    private XElement GetConversation(XElement op, string conversationId) => Success(op, M("Conversation",
        T("ConversationId", new XAttribute("Id", conversationId)),
        T("ConversationNodes", Thread(conversationId).Select(m => T("ConversationNode",
            T("Items", MessageElement(m, withBody: true)))))));

    /// <summary>The conversation's messages outside Deleted Items and Drafts, newest first.</summary>
    private List<DemoMessage> Thread(string conversationId) =>
        Mailbox.Messages.Where(m => m.ConversationId == conversationId && m.FolderId is not ("deleteditems" or "drafts"))
            .OrderByDescending(m => m.Received).ToList();

    private static string Topic(string subject)
    {
        foreach (var prefix in new[] { "YNT: ", "RE: ", "İLT: ", "FW: " })
            if (subject.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return subject[prefix.Length..];
        return subject;
    }

    #endregion

    #region Calendar

    private XElement EventElement(DemoEvent e) => T("CalendarItem",
        ItemId(e.Id),
        T("Subject", e.Subject),
        T("Body", new XAttribute("BodyType", "HTML"), e.BodyHtml),
        T("ReminderIsSet", !e.IsAllDay),
        T("ReminderMinutesBeforeStart", 15),
        T("Start", Utc(e.Start)),
        T("End", Utc(e.End)),
        T("IsAllDayEvent", e.IsAllDay),
        T("LegacyFreeBusyStatus", e.FreeBusy),
        T("Location", e.Location),
        T("IsMeeting", e.Attendees.Count > 0),
        T("IsCancelled", false),
        T("IsRecurring", e.IsRecurring),
        T("MyResponseType", e.MyResponse),
        T("Organizer", MailboxElement(e.Organizer)),
        e.Attendees.Count > 0
            ? T("RequiredAttendees", e.Attendees.Select((p, i) => T("Attendee", MailboxElement(p), T("ResponseType", i % 3 == 2 ? "Tentative" : "Accept"))))
            : null);

    /// <summary>
    /// Adds a new appointment to the calendar; a recurring one as its occurrences (up to 100, in the next 90 days
    /// when it has no end), which share a series. Returns the id of the first.
    /// </summary>
    private string CreateEvents(XElement item)
    {
        string? Value(string name) => (string?)item.Element(TNs + name);
        DateTimeOffset Time(string name) => DateTimeOffset.Parse(Value(name)!, CultureInfo.InvariantCulture).ToOffset(Mailbox.Now.Offset);
        var start = Time("Start");
        var duration = Time("End") - start;
        var attendees = item.Descendants(TNs + "EmailAddress")
            .Select(a => Mailbox.People.FirstOrDefault(p => p.Address.Equals(a.Value, StringComparison.OrdinalIgnoreCase)))
            .OfType<DemoPerson>().ToList();

        var days = new List<DateTime> { start.Date };
        string? series = null;
        if (item.Element(TNs + "Recurrence") is { } recurrence)
        {
            series = Mailbox.NewId("series");
            var rule = recurrence.Elements().First();
            var until = (DateTime?)recurrence.Descendants(TNs + "EndDate").FirstOrDefault() ?? start.Date.AddDays(90);
            var weekdays = ((string?)rule.Element(TNs + "DaysOfWeek"))?.Split(' ') ?? [];
            var dayOfMonth = (int?)rule.Element(TNs + "DayOfMonth");
            var month = (string?)rule.Element(TNs + "Month");
            bool Repeats(DateTime d) => rule.Name.LocalName switch
            {
                "DailyRecurrence" => true,
                "WeeklyRecurrence" => weekdays.Contains(d.DayOfWeek.ToString()),
                "AbsoluteMonthlyRecurrence" => d.Day == dayOfMonth,
                _ => d.Day == dayOfMonth && CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(d.Month) == month,
            };
            days = [];
            for (var d = start.Date; d <= until && days.Count < 100; d = d.AddDays(1))
                if (Repeats(d)) days.Add(d);
        }

        var ids = days.Select(day =>
        {
            var occurrenceStart = start + (day - start.Date);
            var e = new DemoEvent(Mailbox.NewId("event"), Value("Subject") ?? "", occurrenceStart, occurrenceStart + duration,
                Value("Location") ?? "", Mailbox.Me, attendees, IsAllDay: Value("IsAllDayEvent") == "true",
                FreeBusy: Value("LegacyFreeBusyStatus") ?? "Busy", MyResponse: "Organizer",
                BodyHtml: Value("Body") ?? "", SeriesId: series);
            Mailbox.Events.Add(e);
            return e.Id;
        }).ToList();
        return ids.FirstOrDefault() ?? Mailbox.NewId("event");
    }

    /// <summary>Reminders dismissed, and the new reminder times of snoozed ones, by event id.</summary>
    private readonly HashSet<string> _dismissed = [];
    private readonly Dictionary<string, DateTimeOffset> _snoozed = [];

    /// <summary>Every timed event reminds 15 minutes before it starts (as EventElement says), until dismissed.</summary>
    private XElement GetReminders(XElement op)
    {
        var begin = DateTimeOffset.Parse((string)op.Element(MNs + "BeginTime")!, CultureInfo.InvariantCulture);
        var end = DateTimeOffset.Parse((string)op.Element(MNs + "EndTime")!, CultureInfo.InvariantCulture);
        var reminders = Mailbox.Events
            .Where(e => !e.IsAllDay && !_dismissed.Contains(e.Id))
            .Select(e => (Event: e, Time: _snoozed.TryGetValue(e.Id, out var snoozed) ? snoozed : e.Start.AddMinutes(-15)))
            .Where(r => r.Time >= begin && r.Time <= end)
            .Select(r => T("Reminder",
                T("Subject", r.Event.Subject),
                T("Location", r.Event.Location),
                T("ReminderTime", Utc(r.Time)),
                T("StartDate", Utc(r.Event.Start)),
                T("EndDate", Utc(r.Event.End)),
                ItemId(r.Event.Id),
                T("ReminderGroup", "Calendar"),
                T("UID", r.Event.Id)));
        return M("GetRemindersResponse", new XAttribute("ResponseClass", "Success"), M("ResponseCode", "NoError"), M("Reminders", reminders));
    }

    private XElement PerformReminderAction(XElement op)
    {
        foreach (var action in op.Descendants(TNs + "ReminderItemAction"))
        {
            var id = Id(action.Element(TNs + "ItemId")!);
            if ((string?)action.Element(TNs + "ActionType") == "Dismiss") _dismissed.Add(id);
            else _snoozed[id] = DateTimeOffset.Parse((string)action.Element(TNs + "NewReminderTime")!, CultureInfo.InvariantCulture);
        }
        return M("PerformReminderActionResponse", new XAttribute("ResponseClass", "Success"), M("ResponseCode", "NoError"), M("UpdatedItemIds"));
    }

    private XElement Availability(XElement op)
    {
        var start = DateTimeOffset.Parse((string)op.Descendants(TNs + "StartTime").First(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
        var end = DateTimeOffset.Parse((string)op.Descendants(TNs + "EndTime").First(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
        var responses = op.Descendants(TNs + "MailboxData").Select(data =>
        {
            var address = (string)data.Element(TNs + "Email")!.Element(TNs + "Address")!;
            var person = Mailbox.People.FirstOrDefault(p => p.Address.Equals(address, StringComparison.OrdinalIgnoreCase));
            var busy = Mailbox.Events
                .Where(e => !e.IsAllDay && e.Start < end && e.End > start && person is not null && (e.Organizer == person || e.Attendees.Contains(person)))
                .Select(e => T("CalendarEvent",
                    T("StartTime", e.Start.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture)),
                    T("EndTime", e.End.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture)),
                    T("BusyType", e.FreeBusy)));
            return M("FreeBusyResponse", ResponseMessage("ResponseMessage"), M("FreeBusyView", T("CalendarEventArray", busy)));
        });
        return M("GetUserAvailabilityResponse", M("FreeBusyResponseArray", responses));
    }

    #endregion

    #region Directory

    private XElement ResolveNames(XElement op)
    {
        var entry = ((string)op.Element(MNs + "UnresolvedEntry")!).Trim();
        if (entry.StartsWith("smtp:", StringComparison.OrdinalIgnoreCase)) entry = entry[5..];
        var exact = entry.Contains('@');
        var matches = Mailbox.People
            .Where(p => exact ? p.Address.Equals(entry, StringComparison.OrdinalIgnoreCase) : Matches(entry, p.Name, p.Address))
            .ToList();
        if (matches.Count == 0) return Error(op, "ErrorNameResolutionNoResults", "No results were found.");

        return Success(op, M("ResolutionSet",
            new XAttribute("TotalItemsInView", matches.Count), new XAttribute("IncludesLastItemInRange", true),
            matches.Select(p => T("Resolution",
                MailboxElement(p),
                T("Contact",
                    T("DisplayName", p.Name),
                    T("CompanyName", Mailbox.Company),
                    T("PhoneNumbers", T("Entry", new XAttribute("Key", "BusinessPhone"), p.Phone), T("Entry", new XAttribute("Key", "MobilePhone"), p.Mobile)),
                    T("Department", p.Department),
                    T("JobTitle", p.JobTitle),
                    T("OfficeLocation", p.Office),
                    Mailbox.Managers.TryGetValue(p, out var manager) ? T("ManagerMailbox", MailboxElement(manager)) : null,
                    T("DirectReports", Mailbox.Managers.Where(kv => kv.Value == p).Select(kv => MailboxElement(kv.Key))))))));
    }

    private XElement ContactElement(DemoContact c) => T("Contact",
        ItemId(c.Id),
        T("DisplayName", c.Person.Name),
        T("GivenName", c.Person.Name.Split(' ')[0]),
        T("CompanyName", c.Company),
        T("EmailAddresses", T("Entry", new XAttribute("Key", "EmailAddress1"), c.Person.Address)),
        T("PhoneNumbers", T("Entry", new XAttribute("Key", "BusinessPhone"), c.Person.Phone), T("Entry", new XAttribute("Key", "MobilePhone"), c.Person.Mobile)),
        T("Department", c.Person.Department),
        T("JobTitle", c.Person.JobTitle),
        T("Surname", string.Join(' ', c.Person.Name.Split(' ').Skip(1))));

    #endregion

    #region SOAP helpers

    private static XElement T(string name, params object?[] content) => new(TNs + name, content);
    private static XElement M(string name, params object?[] content) => new(MNs + name, content);

    private static XElement ItemId(string id) => T("ItemId", new XAttribute("Id", id), new XAttribute("ChangeKey", "demo"));

    private static XElement MailboxElement(DemoPerson p) =>
        T("Mailbox", T("Name", p.Name), T("EmailAddress", p.Address), T("RoutingType", "SMTP"), T("MailboxType", "Mailbox"));

    private static string Id(XElement element) => (string)element.Attribute("Id")!;

    private static string Utc(DateTimeOffset value) => value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static (int Offset, int Size) Paging(XElement op)
    {
        var view = op.Element(MNs + "IndexedPageItemView");
        return ((int?)view?.Attribute("Offset") ?? 0, (int?)view?.Attribute("MaxEntriesReturned") ?? 100);
    }

    private static bool Matches(string? query, params string[] fields) =>
        string.IsNullOrEmpty(query) || fields.Any(f => f.Contains(query, StringComparison.CurrentCultureIgnoreCase));

    private static XElement Response(XElement op, IEnumerable<XElement> messages) =>
        M(op.Name.LocalName + "Response", M("ResponseMessages", messages));

    private static XElement Success(XElement op, params object?[] content) => Success(op.Name.LocalName, content);

    private static XElement Success(string operation, params object?[] content) =>
        M(operation + "ResponseMessage", new XAttribute("ResponseClass", "Success"), M("ResponseCode", "NoError"), content);

    private static XElement Error(XElement op, string code, string text) => Error(op.Name.LocalName, code, text);

    private static XElement Error(string operation, string code, string text) =>
        M(operation + "ResponseMessage", new XAttribute("ResponseClass", "Error"), M("MessageText", text), M("ResponseCode", code));

    private static XElement ResponseMessage(string name) =>
        new(MNs + name, new XAttribute("ResponseClass", "Success"), new XElement(MNs + "ResponseCode", "NoError"));

    /// <summary>A response body that never delivers anything until the request is cancelled.</summary>
    private sealed class SilentStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            await Task.Delay(Timeout.Infinite, ct);
            return 0;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
            ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    #endregion
}
