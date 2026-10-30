using System.Xml.Linq;
using ExchangeMail.Core.Ews;
using ExchangeMail.Core.Models;
using ExchangeMail.Demo;
using ExchangeMail.ViewModels;

namespace ExchangeMail.Core.Tests;

/// <summary>Event reminders and finding the online meeting an event links to.</summary>
public class RemindersTests
{
    private static readonly XNamespace T = FakeEwsServer.T;
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 10, 0, 0, TimeSpan.FromHours(3)); // a Wednesday

    [Theory]
    [InlineData("https://teams.microsoft.com/l/meetup-join/19%3ameeting_abc%40thread.v2/0?context=x")]
    [InlineData("https://company.zoom.us/j/123456789?pwd=abc")]
    [InlineData("https://meet.google.com/abc-defg-hij")]
    [InlineData("https://company.webex.com/meet/someone")]
    [InlineData("https://meet.jit.si/PlanningRoom")]
    [InlineData("https://jitsi.company.example/planning")]
    [InlineData("https://meet-jitsi.company.example/planning")]
    public void Known_meeting_services_and_jitsi_are_recognized(string address)
    {
        var body = $"""<p>Agenda</p><p><a href="https://intranet.company.example/agenda">Agenda</a></p><p><a href="{System.Net.WebUtility.HtmlEncode(address)}">Join</a></p>""";

        Assert.Equal(new Uri(address).AbsoluteUri, MeetingLink.Find(null, "", body));
    }

    [Fact]
    public void Only_meeting_services_count_and_the_organizations_own_hosts_can_be_added()
    {
        const string body = "<p>Slides: https://intranet.company.example/slides. Room: https://toplanti.company.example/r/42.</p>";

        Assert.Null(MeetingLink.Find(null, "Room 3", body));
        var hosts = MeetingLink.ParseHosts("toplanti.company.example; https://video.company.example/ ");
        Assert.Equal(["toplanti.company.example", "video.company.example"], hosts);
        Assert.Equal("https://toplanti.company.example/r/42", MeetingLink.Find(null, "", body, hosts));
    }

    [Theory]
    // Typed without a scheme, in the location or the text: opened over https.
    [InlineData("tvk.company.example/weekly-meeting", null, "https://tvk.company.example/weekly-meeting")]
    [InlineData(null, "<p>Bağlantı: tvk.company.example/weekly-meeting.</p>", "https://tvk.company.example/weekly-meeting")]
    // Outlook links a typed address with http://, which is kept.
    [InlineData(null, """<p><a href="http://tvk.company.example/weekly-meeting">tvk.company.example/weekly-meeting</a></p>""", "http://tvk.company.example/weekly-meeting")]
    [InlineData("meet.jit.si/Planning", null, "https://meet.jit.si/Planning")]
    // Not a meeting: the server alone (as in a signature), or a mail address on the server.
    [InlineData("tvk.company.example", "<p>Destek: destek@tvk.company.example</p>", null)]
    public void Meeting_addresses_without_a_scheme_are_recognized(string? location, string? body, string? expected)
    {
        Assert.Equal(expected, MeetingLink.Find(null, location, body, ["tvk.company.example"]));
    }

    [Fact]
    public void Exchanges_join_address_comes_first_then_the_location()
    {
        Assert.Equal("https://teams.microsoft.com/l/meetup-join/1",
            MeetingLink.Find("https://teams.microsoft.com/l/meetup-join/1", "https://meet.jit.si/a", "https://zoom.us/j/2"));
        Assert.Equal("https://meet.jit.si/a", MeetingLink.Find(null, "https://meet.jit.si/a", "https://zoom.us/j/2"));
    }

    [Fact]
    public async Task Reminders_are_read_and_snoozed_in_exchange()
    {
        var server = new FakeEwsServer()
            .On("GetReminders", """
                <m:GetRemindersResponse ResponseClass="Success"><m:ResponseCode>NoError</m:ResponseCode><m:Reminders>
                  <t:Reminder><t:Subject>Planlama</t:Subject><t:Location>Oda 3</t:Location><t:ReminderTime>2026-10-28T06:45:00Z</t:ReminderTime>
                    <t:StartDate>2026-10-28T07:00:00Z</t:StartDate><t:EndDate>2026-10-28T08:00:00Z</t:EndDate>
                    <t:ItemId Id="E1" ChangeKey="c" /><t:ReminderGroup>Calendar</t:ReminderGroup><t:UID>u</t:UID></t:Reminder>
                  <t:Reminder><t:Subject>Görev</t:Subject><t:ReminderTime>2026-10-28T06:00:00Z</t:ReminderTime>
                    <t:ItemId Id="T1" ChangeKey="c" /><t:ReminderGroup>Task</t:ReminderGroup></t:Reminder>
                </m:Reminders></m:GetRemindersResponse>
                """)
            .On("PerformReminderAction", """<m:PerformReminderActionResponse ResponseClass="Success"><m:ResponseCode>NoError</m:ResponseCode><m:UpdatedItemIds /></m:PerformReminderActionResponse>""");
        using var client = server.CreateClient();
        var ct = TestContext.Current.CancellationToken;

        var reminders = await client.GetRemindersAsync(Now.AddDays(-1), Now.AddDays(1), ct);
        await client.SnoozeReminderAsync(reminders[0].ItemId, Now.AddMinutes(10), ct);

        var first = reminders[0];
        Assert.Equal(("Planlama", "Oda 3", true), (first.Subject, first.Location, first.IsCalendar));
        Assert.Equal(Now.AddMinutes(-15), first.ReminderTime);
        Assert.False(reminders[1].IsCalendar);
        // Only the reminders that are due and not dismissed, as Outlook's reminder window shows them.
        Assert.Equal("CurrentReminders", server.Request("GetReminders").Element(XName.Get("ReminderType", FakeEwsServer.M))?.Value);
        var action = server.Request("PerformReminderAction").Descendants(T + "ReminderItemAction").Single();
        Assert.Equal("Snooze", action.Element(T + "ActionType")?.Value);
        Assert.Equal("2026-10-28T07:10:00Z", action.Element(T + "NewReminderTime")?.Value);
        Assert.Null(action.Element(T + "ItemId")?.Attribute("ChangeKey")); // a notification may be older than the event's last change
    }

    [Fact]
    public async Task Due_reminders_are_reported_once_with_their_meeting_and_come_back_after_snoozing()
    {
        var mailbox = new DemoMailbox("tr", Now);
        using var client = new EwsClient(DemoEwsHandler.EwsUrl, new DemoEwsHandler(mailbox));
        var now = Now;
        var watcher = new ReminderWatcher(client, () => []) { Now = () => now };
        var due = new List<DueReminder>();
        var gone = new List<string>();
        watcher.Due += (_, r) => due.Add(r);
        watcher.Gone += (_, key) => gone.Add(key);

        await watcher.CheckAsync(TestContext.Current.CancellationToken);
        await watcher.CheckAsync(TestContext.Current.CancellationToken);

        // Within its reminder time and not yet over: the inspection (10:00–12:00) and the quick sync (10:15).
        Assert.Equal(["Depo denetimi", "Hızlı görüşme"], due.Select(d => d.Reminder.Subject).Order());
        var sync = due.Single(d => d.Reminder.Subject == "Hızlı görüşme");
        Assert.Equal("https://jitsi.kuzeylojistik.example/rota-planlama", sync.JoinUrl);
        Assert.Null(due.Single(d => d.Reminder.Subject == "Depo denetimi").JoinUrl);

        await watcher.SnoozeAsync(sync.Reminder.ItemId, TimeSpan.FromMinutes(5));
        await watcher.CheckAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, due.Count);
        now = Now.AddMinutes(5);
        await watcher.CheckAsync(TestContext.Current.CancellationToken);
        Assert.Equal(3, due.Count);
        Assert.Equal("Hızlı görüşme", due[2].Reminder.Subject);

        // Dismissed elsewhere (Outlook, a phone): its notification is taken away.
        var inspection = due.Single(d => d.Reminder.Subject == "Depo denetimi");
        await client.DismissReminderAsync(inspection.Reminder.ItemId, TestContext.Current.CancellationToken);
        await watcher.CheckAsync(TestContext.Current.CancellationToken);
        Assert.Equal([inspection.Key], gone);
    }

    [Fact]
    public async Task A_reminder_is_shown_once_even_when_exchange_moves_its_time_and_not_again_after_dismissing()
    {
        static string Reminders(string reminderTime) => $"""
            <m:GetRemindersResponse ResponseClass="Success"><m:ResponseCode>NoError</m:ResponseCode><m:Reminders>
              <t:Reminder><t:Subject>Toplantı</t:Subject><t:ReminderTime>{reminderTime}</t:ReminderTime>
                <t:StartDate>2026-10-28T11:00:00Z</t:StartDate><t:EndDate>2026-10-28T13:00:00Z</t:EndDate>
                <t:ItemId Id="E1" ChangeKey="c" /><t:ReminderGroup>Calendar</t:ReminderGroup></t:Reminder>
            </m:Reminders></m:GetRemindersResponse>
            """;
        // The meeting is 14:00–16:00 (11:00–13:00 UTC); it is 14:05 and each check reports another reminder time.
        var server = new FakeEwsServer()
            .On("GetReminders", Reminders("2026-10-28T10:45:00Z"))
            .On("GetItem", """
                <m:GetItemResponse><m:ResponseMessages><m:GetItemResponseMessage ResponseClass="Success"><m:ResponseCode>NoError</m:ResponseCode>
                  <m:Items><t:CalendarItem><t:ItemId Id="E1" ChangeKey="c" /><t:Subject>Toplantı</t:Subject></t:CalendarItem></m:Items>
                </m:GetItemResponseMessage></m:ResponseMessages></m:GetItemResponse>
                """)
            .On("GetReminders", Reminders("2026-10-28T11:05:00Z"))
            .On("PerformReminderAction", """<m:PerformReminderActionResponse ResponseClass="Success"><m:ResponseCode>NoError</m:ResponseCode><m:UpdatedItemIds /></m:PerformReminderActionResponse>""")
            .On("GetReminders", Reminders("2026-10-28T11:06:00Z"));
        using var client = server.CreateClient();
        var now = new DateTimeOffset(2026, 10, 28, 11, 5, 0, TimeSpan.Zero);
        var watcher = new ReminderWatcher(client, () => []) { Now = () => now };
        var due = new List<DueReminder>();
        watcher.Due += (_, r) => due.Add(r);

        await watcher.CheckAsync(TestContext.Current.CancellationToken);
        now = now.AddMinutes(1);
        await watcher.CheckAsync(TestContext.Current.CancellationToken);
        Assert.Single(due);

        // Exchange still lists it after Dismiss (here: it reported no change): it stays dismissed here.
        await watcher.DismissAsync(due[0].Reminder.ItemId);
        await watcher.CheckAsync(TestContext.Current.CancellationToken);
        Assert.Single(due);
    }

    [Fact]
    public async Task The_calendar_offers_to_join_the_meeting_of_the_selected_event()
    {
        var mailbox = new DemoMailbox("tr");
        var session = new MailSession(new EwsClient(DemoEwsHandler.EwsUrl, new DemoEwsHandler(mailbox)), new AccountSettings { Email = mailbox.Me.Address });
        // The quick sync starts soon after now, which near midnight on Sunday is next week.
        var vm = new CalendarViewModel(session) { AnchorDate = mailbox.Events.Single(e => e.Subject == "Hızlı görüşme").Start.Date };
        await vm.LoadAsync();

        vm.SelectedEvent = vm.Events.Single(e => e.Event.Subject == "Hızlı görüşme");
        for (var i = 0; i < 50 && vm.Detail is null; i++) await Task.Delay(20, TestContext.Current.CancellationToken);

        Assert.True(vm.Detail?.HasJoinUrl);
        Assert.Equal("https://jitsi.kuzeylojistik.example/rota-planlama", vm.Detail!.JoinUrl);
    }
}
