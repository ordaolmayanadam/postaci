using System.Xml.Linq;
using ExchangeMail.Core.Models;

namespace ExchangeMail.Core.Tests;

public class CalendarContactsTests
{
    private static readonly XNamespace T = FakeEwsServer.T;
    private static readonly XNamespace M = FakeEwsServer.M;

    private static string Ok(string operation, string inner) =>
        $"""<m:{operation}Response><m:ResponseMessages><m:{operation}ResponseMessage ResponseClass="Success"><m:ResponseCode>NoError</m:ResponseCode>{inner}</m:{operation}ResponseMessage></m:ResponseMessages></m:{operation}Response>""";

    [Fact]
    public async Task SetReadState_overwrites_without_change_key_and_suppresses_read_receipts()
    {
        var server = new FakeEwsServer().On("UpdateItem", Ok("UpdateItem", """<m:Items><t:Message><t:ItemId Id="I1" ChangeKey="NEW" /></t:Message></m:Items>"""));
        using var client = server.CreateClient();

        var ids = await client.SetReadStateAsync([new EwsItemId("I1", "OLD")], true, TestContext.Current.CancellationToken);

        var request = server.Request("UpdateItem");
        Assert.Equal("AlwaysOverwrite", (string?)request.Attribute("ConflictResolution"));
        Assert.Equal("true", (string?)request.Attribute("SuppressReadReceipts"));
        Assert.Equal("true", request.Descendants(T + "IsRead").Single().Value);
        Assert.Equal(new EwsItemId("I1", "NEW"), Assert.Single(ids));
    }

    [Fact]
    public async Task FindAppointments_uses_calendar_view_with_time_zone_and_parses_events()
    {
        var server = new FakeEwsServer().On("FindItem", Ok("FindItem", """
            <m:RootFolder TotalItemsInView="2" IncludesLastItemInRange="true"><t:Items>
              <t:CalendarItem><t:ItemId Id="B" ChangeKey="c" /><t:Subject>Sprint</t:Subject>
                <t:Start>2026-10-26T09:00:00Z</t:Start><t:End>2026-10-26T10:00:00Z</t:End><t:IsAllDayEvent>false</t:IsAllDayEvent>
                <t:LegacyFreeBusyStatus>Tentative</t:LegacyFreeBusyStatus><t:Location>Toplantı 3</t:Location>
                <t:IsMeeting>true</t:IsMeeting><t:IsCancelled>false</t:IsCancelled><t:IsRecurring>true</t:IsRecurring>
                <t:MyResponseType>NoResponseReceived</t:MyResponseType>
                <t:Organizer><t:Mailbox><t:Name>Ali</t:Name><t:EmailAddress>ali@sirket.com</t:EmailAddress></t:Mailbox></t:Organizer>
              </t:CalendarItem>
              <t:CalendarItem><t:ItemId Id="A" ChangeKey="c" /><t:Subject>Bayram</t:Subject>
                <t:Start>2026-10-25T21:00:00Z</t:Start><t:End>2026-10-26T21:00:00Z</t:End><t:IsAllDayEvent>true</t:IsAllDayEvent></t:CalendarItem>
            </t:Items></m:RootFolder>
            """));
        using var client = server.CreateClient();
        client.TimeZoneId = "Turkey Standard Time";

        var events = await client.FindAppointmentsAsync(
            new DateTimeOffset(2026, 10, 26, 0, 0, 0, TimeSpan.FromHours(3)),
            new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.FromHours(3)),
            TestContext.Current.CancellationToken);

        Assert.Equal(["A", "B"], events.Select(e => e.ItemId.Id)); // sorted by start
        var sprint = events[1];
        Assert.Equal(ResponseType.NoResponseReceived, sprint.MyResponse);
        Assert.Equal(FreeBusyStatus.Tentative, sprint.FreeBusy);
        Assert.Equal("ali@sirket.com", sprint.Organizer!.Address);
        Assert.True(sprint.IsMeeting && sprint.IsRecurring);
        Assert.True(events[0].IsAllDay);

        var view = server.Request("FindItem").Element(M + "CalendarView")!;
        Assert.Equal("2026-10-25T21:00:00Z", (string?)view.Attribute("StartDate"));
        Assert.Equal("2026-11-01T21:00:00Z", (string?)view.Attribute("EndDate"));
        Assert.Equal("calendar", (string?)server.Request("FindItem").Descendants(T + "DistinguishedFolderId").Single().Attribute("Id"));
        Assert.Equal("Turkey Standard Time", (string?)server.Headers[0]!.Descendants(T + "TimeZoneDefinition").Single().Attribute("Id"));
    }

    [Fact]
    public async Task Mail_requests_do_not_send_time_zone_header()
    {
        var server = new FakeEwsServer().On("DeleteItem", Ok("DeleteItem", ""));
        using var client = server.CreateClient();
        client.TimeZoneId = "Turkey Standard Time";

        await client.DeleteItemsAsync([new EwsItemId("X", null)], ct: TestContext.Current.CancellationToken);

        Assert.Empty(server.Headers[0]!.Descendants(T + "TimeZoneContext"));
    }

    [Fact]
    public async Task CreateAppointment_with_attendees_sends_invitations_in_schema_order()
    {
        var server = new FakeEwsServer().On("CreateItem", Ok("CreateItem", """<m:Items><t:CalendarItem><t:ItemId Id="N" ChangeKey="c" /></t:CalendarItem></m:Items>"""));
        using var client = server.CreateClient();

        await client.CreateAppointmentAsync(new NewAppointment
        {
            Subject = "Planlama",
            Location = "Salon",
            Start = new DateTimeOffset(2026, 10, 27, 14, 0, 0, TimeSpan.FromHours(3)),
            End = new DateTimeOffset(2026, 10, 27, 15, 0, 0, TimeSpan.FromHours(3)),
            RequiredAttendees = ["ayse@sirket.com"],
            ReminderMinutes = 10,
        }, TestContext.Current.CancellationToken);

        var request = server.Request("CreateItem");
        Assert.Equal("SendToAllAndSaveCopy", (string?)request.Attribute("SendMeetingInvitations"));
        var item = request.Element(M + "Items")!.Element(T + "CalendarItem")!;
        Assert.Equal(
            ["Subject", "Body", "ReminderIsSet", "ReminderMinutesBeforeStart", "Start", "End", "IsAllDayEvent", "LegacyFreeBusyStatus", "Location", "RequiredAttendees"],
            item.Elements().Select(e => e.Name.LocalName));
        Assert.Equal("2026-10-27T11:00:00Z", item.Element(T + "Start")!.Value);
        Assert.Equal("ayse@sirket.com", item.Descendants(T + "EmailAddress").Single().Value);
    }

    [Fact]
    public async Task CreateAppointment_rejects_end_before_start()
    {
        using var client = new FakeEwsServer().CreateClient();
        var now = DateTimeOffset.Now;

        await Assert.ThrowsAsync<ArgumentException>(() => client.CreateAppointmentAsync(new NewAppointment { Start = now, End = now }));
    }

    [Fact]
    public async Task RespondToMeeting_accepts_with_fresh_change_key()
    {
        var server = new FakeEwsServer()
            .On("GetItem", Ok("GetItem", """<m:Items><t:MeetingRequest><t:ItemId Id="MR" ChangeKey="FRESH" /></t:MeetingRequest></m:Items>"""))
            .On("CreateItem", Ok("CreateItem", "<m:Items />"));
        using var client = server.CreateClient();

        await client.RespondToMeetingAsync(new EwsItemId("MR", "OLD"), MeetingResponse.Tentative, ct: TestContext.Current.CancellationToken);

        var create = server.Request("CreateItem");
        Assert.Equal("SendAndSaveCopy", (string?)create.Attribute("MessageDisposition"));
        var reference = create.Element(M + "Items")!.Element(T + "TentativelyAcceptItem")!.Element(T + "ReferenceItemId")!;
        Assert.Equal("FRESH", (string?)reference.Attribute("ChangeKey"));
    }

    [Fact]
    public async Task FindContacts_requests_indexed_fields_and_parses_entries()
    {
        var server = new FakeEwsServer().On("FindItem", Ok("FindItem", """
            <m:RootFolder IndexedPagingOffset="2" TotalItemsInView="2" IncludesLastItemInRange="true"><t:Items>
              <t:Contact><t:ItemId Id="C1" ChangeKey="c" /><t:DisplayName>Ayşe Kaya</t:DisplayName><t:CompanyName>Şirket</t:CompanyName>
                <t:EmailAddresses><t:Entry Key="EmailAddress1">ayse@sirket.com</t:Entry></t:EmailAddresses>
                <t:PhoneNumbers><t:Entry Key="MobilePhone">+90 555 000 00 00</t:Entry></t:PhoneNumbers>
                <t:JobTitle>Mühendis</t:JobTitle></t:Contact>
              <t:DistributionList><t:ItemId Id="D1" ChangeKey="c" /><t:DisplayName>Ekip</t:DisplayName></t:DistributionList>
            </t:Items></m:RootFolder>
            """));
        using var client = server.CreateClient();

        var page = await client.FindContactsAsync(ct: TestContext.Current.CancellationToken);

        var ayse = page.Items[0];
        Assert.Equal("ayse@sirket.com", ayse.Email);
        Assert.Equal("+90 555 000 00 00", ayse.MobilePhone);
        Assert.Null(ayse.BusinessPhone);
        Assert.False(ayse.IsDirectoryEntry);
        Assert.True(page.Items[1].IsDistributionList);

        var indexed = server.Request("FindItem").Descendants(T + "IndexedFieldURI").ToList();
        Assert.Contains(indexed, e => (string?)e.Attribute("FieldIndex") == "EmailAddress1");
    }

    [Fact]
    public async Task SearchDirectory_parses_full_contact_data()
    {
        var server = new FakeEwsServer().On("ResolveNames", """
            <m:ResolveNamesResponse><m:ResponseMessages><m:ResolveNamesResponseMessage ResponseClass="Warning">
              <m:MessageText>Multiple results were found.</m:MessageText><m:ResponseCode>ErrorNameResolutionMultipleResults</m:ResponseCode>
              <m:ResolutionSet TotalItemsInView="1" IncludesLastItemInRange="true"><t:Resolution>
                <t:Mailbox><t:Name>Mehmet Can</t:Name><t:EmailAddress>mehmet@sirket.com</t:EmailAddress><t:RoutingType>SMTP</t:RoutingType><t:MailboxType>Mailbox</t:MailboxType></t:Mailbox>
                <t:Contact><t:DisplayName>Mehmet Can</t:DisplayName><t:EmailAddresses><t:Entry Key="EmailAddress1">SMTP:mehmet@sirket.com</t:Entry></t:EmailAddresses>
                  <t:PhoneNumbers><t:Entry Key="BusinessPhone">1234</t:Entry></t:PhoneNumbers><t:Department>BT</t:Department><t:JobTitle>Uzman</t:JobTitle></t:Contact>
              </t:Resolution></m:ResolutionSet>
            </m:ResolveNamesResponseMessage></m:ResponseMessages></m:ResolveNamesResponse>
            """);
        using var client = server.CreateClient();

        var result = Assert.Single(await client.SearchDirectoryAsync("mehmet", TestContext.Current.CancellationToken));

        Assert.True(result.IsDirectoryEntry);
        Assert.Equal("mehmet@sirket.com", result.Email);
        Assert.Equal("1234", result.BusinessPhone);
        Assert.Equal("BT", result.Department);
        Assert.Equal("true", (string?)server.Request("ResolveNames").Attribute("ReturnFullContactData"));
    }

    [Fact]
    public async Task CreateContact_writes_fields_in_schema_order()
    {
        var server = new FakeEwsServer().On("CreateItem", Ok("CreateItem", """<m:Items><t:Contact><t:ItemId Id="N" ChangeKey="c" /></t:Contact></m:Items>"""));
        using var client = server.CreateClient();

        await client.CreateContactAsync(new NewContact
        {
            GivenName = "Zeynep", Surname = "Ak", Email = "zeynep@x.com", Company = "X", JobTitle = "Avukat",
            MobilePhone = "555", BusinessPhone = "212",
        }, TestContext.Current.CancellationToken);

        var contact = server.Request("CreateItem").Descendants(T + "Contact").Single();
        Assert.Equal(["DisplayName", "GivenName", "CompanyName", "EmailAddresses", "PhoneNumbers", "JobTitle", "Surname"],
            contact.Elements().Select(e => e.Name.LocalName));
        Assert.Equal("Zeynep Ak", contact.Element(T + "DisplayName")!.Value);
        Assert.Equal(["BusinessPhone", "MobilePhone"], contact.Element(T + "PhoneNumbers")!.Elements().Select(e => (string)e.Attribute("Key")!));
    }
}
