using ExchangeMail.Core.Ews;
using ExchangeMail.Core.Models;
using ExchangeMail.Demo;
using ExchangeMail.ViewModels;

namespace ExchangeMail.Core.Tests;

/// <summary>Editing and deleting recurring series and resizing events, against the demo mailbox.</summary>
public class CalendarEditingTests
{
    /// <summary>The calendar of a fresh demo mailbox; the series question is answered with <paramref name="scope"/>.</summary>
    private static (CalendarViewModel Calendar, DemoMailbox Mailbox) Calendar(RecurrenceScope? scope, List<string>? questions = null)
    {
        var mailbox = new DemoMailbox("tr");
        var session = new MailSession(new EwsClient(DemoEwsHandler.EwsUrl, new DemoEwsHandler(mailbox)), new AccountSettings { Email = mailbox.Me.Address });
        var vm = new CalendarViewModel(session)
        {
            AskScopeAsync = (title, _) =>
            {
                questions?.Add(title);
                return Task.FromResult(scope);
            },
        };
        return (vm, mailbox);
    }

    /// <summary>Shows the event's details, as selecting it in the calendar does.</summary>
    private static async Task OpenAsync(CalendarViewModel vm, DemoMailbox mailbox, CalendarEntryViewModel entry)
    {
        using var client = new EwsClient(DemoEwsHandler.EwsUrl, new DemoEwsHandler(mailbox));
        vm.Detail = new CalendarDetailViewModel(await client.GetAppointmentAsync(entry.Event.ItemId, TestContext.Current.CancellationToken), allowRemoteContent: false);
    }

    [Fact]
    public async Task Editing_the_series_of_an_occurrence_changes_every_occurrence()
    {
        var (vm, mailbox) = Calendar(RecurrenceScope.Series);
        await vm.LoadAsync();
        var occurrence = vm.Events.Last(e => e.Event.Subject == "Haftalık özet");
        Assert.True(occurrence.Event.IsRecurring);
        await OpenAsync(vm, mailbox, occurrence);
        CalendarDetailViewModel? edited = null;
        vm.EditRequested += (_, detail) => edited = detail;

        await vm.EditCommand.ExecuteAsync(null);

        // The editor gets the series, which starts with its first occurrence.
        Assert.NotNull(edited);
        Assert.True(edited.IsSeries);
        var first = mailbox.Events.Where(e => e.SeriesId == "wrap-up").Min(e => e.Start);
        Assert.Equal(first, edited.Event.Start);

        var later = edited.Event.Start.AddHours(1);
        Assert.True(await vm.UpdateAsync(edited, new NewAppointment
        {
            Subject = "Haftalık değerlendirme",
            Location = edited.Event.Location,
            Start = later,
            End = later + (edited.Event.End - edited.Event.Start),
        }, bodyChanged: false, attendeesChanged: false));

        var series = mailbox.Events.Where(e => e.SeriesId == "wrap-up").ToList();
        Assert.Equal(2, series.Count);
        Assert.All(series, e => Assert.Equal("Haftalık değerlendirme", e.Subject));
        Assert.All(series, e => Assert.Equal(16, e.Start.Hour));
        Assert.All(series, e => Assert.Equal(TimeSpan.FromHours(1), e.End - e.Start));
    }

    [Fact]
    public async Task Choosing_the_occurrence_edits_only_that_occurrence()
    {
        var (vm, mailbox) = Calendar(RecurrenceScope.Occurrence);
        await vm.LoadAsync();
        var occurrence = vm.Events.First(e => e.Event.Subject == "Haftalık özet");
        await OpenAsync(vm, mailbox, occurrence);
        CalendarDetailViewModel? edited = null;
        vm.EditRequested += (_, detail) => edited = detail;

        await vm.EditCommand.ExecuteAsync(null);

        Assert.NotNull(edited);
        Assert.False(edited.IsSeries);
        Assert.Equal(occurrence.Event.ItemId, edited.Event.ItemId);
    }

    [Fact]
    public async Task Cancelling_the_choice_opens_no_editor_and_deletes_nothing()
    {
        var (vm, mailbox) = Calendar(scope: null);
        await vm.LoadAsync();
        await OpenAsync(vm, mailbox, vm.Events.First(e => e.Event.Subject == "Haftalık özet"));
        var opened = false;
        vm.EditRequested += (_, _) => opened = true;
        var count = mailbox.Events.Count;

        await vm.EditCommand.ExecuteAsync(null);
        await vm.DeleteCommand.ExecuteAsync(null);

        Assert.False(opened);
        Assert.Equal(count, mailbox.Events.Count);
    }

    [Fact]
    public async Task Cancelling_a_recurring_meeting_series_removes_every_occurrence()
    {
        var questions = new List<string>();
        var (vm, mailbox) = Calendar(RecurrenceScope.Series, questions);
        await vm.LoadAsync();
        await OpenAsync(vm, mailbox, vm.Events.First(e => e.Event.Subject == "Haftalık özet"));

        await vm.DeleteCommand.ExecuteAsync(null);

        // You organize this meeting, so the question is about cancelling it, and it replaces the usual confirmation.
        Assert.Equal([Strings.Calendar_RecurringCancelTitle], questions);
        Assert.DoesNotContain(mailbox.Events, e => e.SeriesId == "wrap-up");
        Assert.Contains(mailbox.Events, e => e.SeriesId == "operations");
    }

    [Fact]
    public async Task Resizing_an_event_keeps_its_start_and_sets_the_new_end()
    {
        var (vm, mailbox) = Calendar(scope: null);
        await vm.LoadAsync();
        var entry = vm.Events.First(e => e.Event.Subject == "Kampanya planlaması");
        var newEnd = entry.Event.End.LocalDateTime.AddMinutes(30);

        await vm.ResizeAsync(entry, newEnd);

        Assert.Null(vm.ErrorMessage);
        Assert.Equal(Strings.Calendar_EventResized, vm.StatusText);
        var changed = mailbox.Events.Single(e => e.Id == entry.Event.ItemId.Id);
        Assert.Equal(entry.Event.Start, changed.Start);
        Assert.Equal(newEnd, changed.End.LocalDateTime);
    }

    [Theory]
    [InlineData(RecurrenceKind.Daily, "DailyRecurrence", null, null)]
    [InlineData(RecurrenceKind.Weekdays, "WeeklyRecurrence", "Monday Tuesday Wednesday Thursday Friday", null)]
    [InlineData(RecurrenceKind.Weekly, "WeeklyRecurrence", "Wednesday", null)]
    [InlineData(RecurrenceKind.Monthly, "AbsoluteMonthlyRecurrence", null, "28")]
    [InlineData(RecurrenceKind.Yearly, "AbsoluteYearlyRecurrence", null, "28")]
    public async Task A_new_series_sends_its_recurrence_from_the_start_date(RecurrenceKind kind, string rule, string? days, string? dayOfMonth)
    {
        var server = new FakeEwsServer().On("CreateItem", """
            <m:CreateItemResponse><m:ResponseMessages><m:CreateItemResponseMessage ResponseClass="Success"><m:ResponseCode>NoError</m:ResponseCode>
              <m:Items><t:CalendarItem><t:ItemId Id="S1" ChangeKey="c" /></t:CalendarItem></m:Items>
            </m:CreateItemResponseMessage></m:ResponseMessages></m:CreateItemResponse>
            """);
        using var client = server.CreateClient();
        var start = new DateTimeOffset(new DateTime(2026, 10, 28, 9, 0, 0, DateTimeKind.Local)); // a Wednesday

        await client.CreateAppointmentAsync(new NewAppointment
        {
            Subject = "Planlama",
            Start = start,
            End = start.AddMinutes(30),
            Recurrence = new RecurrencePattern(kind, kind == RecurrenceKind.Daily ? new DateOnly(2026, 11, 6) : null),
        }, TestContext.Current.CancellationToken);

        var recurrence = server.Request("CreateItem").Descendants(XNames.T + "Recurrence").Single();
        // The schema puts Recurrence after the attendees, as the last field written here.
        Assert.Equal("Recurrence", recurrence.Parent!.Elements().Last().Name.LocalName);
        var pattern = recurrence.Elements().First();
        Assert.Equal(rule, pattern.Name.LocalName);
        Assert.Equal(days, (string?)pattern.Element(XNames.T + "DaysOfWeek"));
        Assert.Equal(dayOfMonth, (string?)pattern.Element(XNames.T + "DayOfMonth"));
        if (kind == RecurrenceKind.Yearly) Assert.Equal("October", (string?)pattern.Element(XNames.T + "Month"));
        var range = recurrence.Elements().Last();
        Assert.Equal(kind == RecurrenceKind.Daily ? "EndDateRecurrence" : "NoEndRecurrence", range.Name.LocalName);
        Assert.Equal("2026-10-28", (string?)range.Element(XNames.T + "StartDate"));
        if (kind == RecurrenceKind.Daily) Assert.Equal("2026-11-06", (string?)range.Element(XNames.T + "EndDate"));
    }

    [Fact]
    public async Task A_new_weekday_series_shows_its_occurrences_as_one_series()
    {
        var (vm, mailbox) = Calendar(RecurrenceScope.Series);
        var monday = DateTime.Today.AddDays(-(((int)DateTime.Today.DayOfWeek + 6) % 7)).AddDays(7);
        var start = new DateTimeOffset(monday.AddHours(8));

        Assert.True(await vm.CreateAsync(new NewAppointment
        {
            Subject = "Günlük kısa toplantı",
            Start = start,
            End = start.AddMinutes(15),
            Recurrence = new RecurrencePattern(RecurrenceKind.Weekdays, DateOnly.FromDateTime(monday.AddDays(10))),
        }));

        // Two working weeks: Monday to Friday, then Monday to Thursday (the last day).
        var series = mailbox.Events.Where(e => e.Subject == "Günlük kısa toplantı").ToList();
        Assert.Equal(9, series.Count);
        Assert.Single(series.Select(e => e.SeriesId).Distinct());
        Assert.DoesNotContain(series, e => e.Start.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday);
        Assert.Equal(5, vm.Events.Count(e => e.Event.Subject == "Günlük kısa toplantı" && e.Event.IsRecurring));
    }

    [Fact]
    public async Task Series_master_is_requested_by_occurrence_id()
    {
        var server = new FakeEwsServer().On("GetItem", """
            <m:GetItemResponse><m:ResponseMessages><m:GetItemResponseMessage ResponseClass="Success"><m:ResponseCode>NoError</m:ResponseCode>
              <m:Items><t:CalendarItem><t:ItemId Id="MASTER" ChangeKey="c" /><t:Subject>Seri</t:Subject><t:IsRecurring>true</t:IsRecurring></t:CalendarItem></m:Items>
            </m:GetItemResponseMessage></m:ResponseMessages></m:GetItemResponse>
            """);
        using var client = server.CreateClient();

        var master = await client.GetRecurringMasterAsync(new EwsItemId("OCC", "c"), TestContext.Current.CancellationToken);

        Assert.Equal("MASTER", master.Event.ItemId.Id);
        var id = server.Request("GetItem").Descendants(XNames.T + "RecurringMasterItemId").Single();
        Assert.Equal("OCC", (string?)id.Attribute("OccurrenceId"));
    }

    [Fact]
    public async Task Unchanged_times_are_not_written()
    {
        var server = new FakeEwsServer().On("UpdateItem", """
            <m:UpdateItemResponse><m:ResponseMessages><m:UpdateItemResponseMessage ResponseClass="Success"><m:ResponseCode>NoError</m:ResponseCode><m:Items /></m:UpdateItemResponseMessage></m:ResponseMessages></m:UpdateItemResponse>
            """);
        using var client = server.CreateClient();
        var start = new DateTimeOffset(2026, 10, 30, 15, 0, 0, TimeSpan.FromHours(3));

        await client.UpdateAppointmentAsync(new EwsItemId("MASTER", "c"), new NewAppointment { Subject = "Yeni ad", Start = start, End = start.AddHours(1) },
            updateBody: false, updateAttendees: false, updateTimes: false, notifyAttendees: true, TestContext.Current.CancellationToken);

        var uris = server.Request("UpdateItem").Descendants(XNames.T + "FieldURI").Select(f => (string)f.Attribute("FieldURI")!).ToList();
        Assert.Contains("item:Subject", uris);
        Assert.DoesNotContain("calendar:Start", uris);
        Assert.DoesNotContain("calendar:End", uris);
        Assert.DoesNotContain("calendar:IsAllDayEvent", uris);
    }

    private static class XNames
    {
        public static readonly System.Xml.Linq.XNamespace T = FakeEwsServer.T;
    }
}
