using System.Xml.Linq;
using ExchangeMail.Core.Models;
using ExchangeMail.ViewModels;

namespace ExchangeMail.Core.Tests;

/// <summary>Drafts, automatic replies, availability and editing of calendar items and contacts.</summary>
public class FeatureTests
{
    private static readonly XNamespace T = FakeEwsServer.T;
    private static readonly XNamespace M = FakeEwsServer.M;

    private static string Ok(string operation, string inner) =>
        $"""<m:{operation}Response><m:ResponseMessages><m:{operation}ResponseMessage ResponseClass="Success"><m:ResponseCode>NoError</m:ResponseCode>{inner}</m:{operation}ResponseMessage></m:ResponseMessages></m:{operation}Response>""";

    private static string ItemIdResponse(string operation, string id, string changeKey) =>
        Ok(operation, $"""<m:Items><t:Message><t:ItemId Id="{id}" ChangeKey="{changeKey}" /></t:Message></m:Items>""");

    private static string DraftDetail(string id, string body, string attachments = "") => Ok("GetItem", $"""
        <m:Items><t:Message><t:ItemId Id="{id}" ChangeKey="ck9" /><t:Subject>Konu</t:Subject>
          <t:Body BodyType="HTML">{System.Security.SecurityElement.Escape(body)}</t:Body>
          <t:Attachments>{attachments}</t:Attachments><t:IsRead>true</t:IsRead></t:Message></m:Items>
        """);

    private static MailSession Session(FakeEwsServer server) =>
        new(server.CreateClient(), new AccountSettings { Email = "ben@sirket.com" });

    // ---- Drafts ----

    [Fact]
    public async Task Saving_an_existing_draft_overwrites_it_and_removes_attachments()
    {
        var server = new FakeEwsServer()
            .On("UpdateItem", ItemIdResponse("UpdateItem", "D1", "k2"))
            .On("DeleteAttachment", Ok("DeleteAttachment", """<m:RootItemId RootItemId="D1" RootItemChangeKey="k3" />"""))
            .On("GetItem", ItemIdResponse("GetItem", "D1", "k4"));
        using var client = server.CreateClient();

        var id = await client.SaveDraftAsync(new OutgoingMessage
        {
            ExistingDraftId = new EwsItemId("D1", "k1"),
            To = ["a@sirket.com"],
            Subject = "Güncel",
            HtmlBody = "<p>yeni</p>",
            RemovedAttachmentIds = ["ATT1"],
        }, TestContext.Current.CancellationToken);

        Assert.Equal(["UpdateItem", "DeleteAttachment", "GetItem"], server.Operations);
        Assert.Equal(new EwsItemId("D1", "k4"), id);
        var updates = server.Request("UpdateItem").Descendants(T + "Updates").Single().Elements().ToList();
        Assert.Contains(updates, u => u.Descendants(T + "Body").Any(b => b.Value == "<p>yeni</p>"));
        // Empty Cc/Bcc are cleared so recipients removed in the editor disappear from the draft.
        Assert.Equal(2, updates.Count(u => u.Name.LocalName == "DeleteItemField"));
    }

    [Fact]
    public async Task Sending_an_existing_draft_does_not_create_a_new_item()
    {
        var server = new FakeEwsServer()
            .On("UpdateItem", ItemIdResponse("UpdateItem", "D1", "k2"))
            .On("SendItem", Ok("SendItem", ""));
        using var client = server.CreateClient();

        await client.SendAsync(new OutgoingMessage { ExistingDraftId = new EwsItemId("D1", "k1"), To = ["a@sirket.com"] },
            TestContext.Current.CancellationToken);

        Assert.Equal(["UpdateItem", "SendItem"], server.Operations);
        Assert.Equal("k2", (string?)server.Request("SendItem").Descendants(T + "ItemId").Single().Attribute("ChangeKey"));
    }

    [Fact]
    public async Task Compose_saves_once_then_updates_the_same_draft()
    {
        var server = new FakeEwsServer()
            .On("CreateItem", ItemIdResponse("CreateItem", "D1", "k1"))
            .On("GetItem", DraftDetail("D1", "<p>ilk</p>"))
            .On("UpdateItem", ItemIdResponse("UpdateItem", "D1", "k2"))
            .On("GetItem", DraftDetail("D1", "<p>ikinci</p>"));
        var vm = new ComposeViewModel(Session(server), ComposeMode.New, null) { To = "a@sirket.com" };
        var saves = 0;
        vm.DraftSaved += (_, _) => saves++;
        var body = "<p>ilk</p>";
        vm.ReadBodyHtmlAsync = () => Task.FromResult(body);

        await vm.SaveDraftCommand.ExecuteAsync(null);
        body = "<p>ikinci</p>";
        await vm.SaveDraftCommand.ExecuteAsync(null);

        Assert.Null(vm.ErrorMessage);
        Assert.Equal(2, saves);
        Assert.Equal(["CreateItem", "GetItem", "UpdateItem", "GetItem"], server.Operations);
        Assert.Contains("<p>ikinci</p>", server.Request("UpdateItem").Descendants(T + "Body").Single().Value);
    }

    [Fact]
    public async Task Saving_a_reply_loads_the_quoted_original_into_the_editor()
    {
        var original = new MailDetail(new EwsItemId("O", "c"), "Teklif", new MailboxAddress("Ali", "ali@sirket.com"), [], [], [],
            null, DateTimeOffset.Now, "", true, true, Importance.Normal, []);
        var attachment = """<t:FileAttachment><t:AttachmentId Id="A1" /><t:Name>ek.pdf</t:Name><t:Size>10</t:Size><t:IsInline>false</t:IsInline></t:FileAttachment>""";
        var server = new FakeEwsServer()
            .On("GetItem", ItemIdResponse("GetItem", "O", "c2"))
            .On("CreateItem", ItemIdResponse("CreateItem", "D1", "k1"))
            .On("UpdateItem", ItemIdResponse("UpdateItem", "D1", "k2"))
            .On("CreateAttachment", Ok("CreateAttachment", """<m:Attachments><t:FileAttachment><t:AttachmentId Id="A1" RootItemId="D1" RootItemChangeKey="k3" /></t:FileAttachment></m:Attachments>"""))
            .On("GetItem", DraftDetail("D1", "<html><body><p>Teşekkürler</p><hr><p>Özgün ileti</p></body></html>", attachment));
        var vm = new ComposeViewModel(Session(server), ComposeMode.Reply, original);
        vm.AddAttachment("ek.pdf", [1, 2, 3]);
        string? replaced = null;
        vm.BodyReplaced += (_, html) => replaced = html;
        Assert.True(vm.IsResponse);

        await vm.SaveDraftCommand.ExecuteAsync(null);

        Assert.Null(vm.ErrorMessage);
        Assert.Equal("<p>Teşekkürler</p><hr><p>Özgün ileti</p>", replaced);
        Assert.False(vm.IsResponse);
        Assert.Empty(vm.Attachments);
        Assert.Equal("ek.pdf", Assert.Single(vm.ExistingAttachments).Name);
    }

    [Fact]
    public void Editing_a_draft_prefills_fields_and_existing_attachments()
    {
        using var server = new FakeEwsServer();
        var draft = new MailDetail(new EwsItemId("D", "c"), "Taslak konu", null,
            [new MailboxAddress("Ali", "ali@sirket.com")], [], [new MailboxAddress(null, "gizli@sirket.com")],
            null, null, "<html><body><p>Merhaba</p><script>x()</script></body></html>", true, true, Importance.High,
            [new AttachmentInfo("A1", "rapor.xlsx", null, null, 2048, false, false), new AttachmentInfo("A2", "logo.png", null, "logo", 10, true, false)]);

        var vm = ComposeViewModel.ForDraft(Session(server), draft);

        Assert.Equal("Ali <ali@sirket.com>; ", vm.To);
        Assert.True(vm.ShowBcc);
        Assert.True(vm.IsHighImportance);
        Assert.Equal("<p>Merhaba</p>", vm.InitialHtml);
        Assert.Equal("rapor.xlsx", Assert.Single(vm.ExistingAttachments).Name);
        Assert.StartsWith("Taslak", vm.WindowTitle);

        vm.RemoveExistingAttachmentCommand.Execute(vm.ExistingAttachments[0]);
        Assert.True(vm.HasAttachmentChanges);
    }

    // ---- Automatic replies ----

    [Fact]
    public async Task Auto_reply_settings_are_read_and_written()
    {
        var server = new FakeEwsServer().On("GetUserOofSettingsRequest", """
            <GetUserOofSettingsResponse xmlns="http://schemas.microsoft.com/exchange/services/2006/messages">
              <ResponseMessage ResponseClass="Success"><ResponseCode>NoError</ResponseCode></ResponseMessage>
              <OofSettings xmlns="http://schemas.microsoft.com/exchange/services/2006/types">
                <OofState>Scheduled</OofState><ExternalAudience>Known</ExternalAudience>
                <Duration><StartTime>2026-11-01T06:00:00Z</StartTime><EndTime>2026-11-05T15:00:00Z</EndTime></Duration>
                <InternalReply><Message>&lt;html&gt;&lt;body&gt;İzindeyim.&lt;br&gt;Acil: 1234&lt;/body&gt;&lt;/html&gt;</Message></InternalReply>
                <ExternalReply><Message>Out of office.</Message></ExternalReply>
              </OofSettings>
              <AllowExternalOof>All</AllowExternalOof>
            </GetUserOofSettingsResponse>
            """)
            .On("SetUserOofSettingsRequest", """
            <SetUserOofSettingsResponse xmlns="http://schemas.microsoft.com/exchange/services/2006/messages">
              <ResponseMessage ResponseClass="Success"><ResponseCode>NoError</ResponseCode></ResponseMessage>
            </SetUserOofSettingsResponse>
            """);
        var vm = new AutoReplyViewModel(Session(server));

        await vm.LoadAsync();

        Assert.Null(vm.ErrorMessage);
        Assert.True(vm.IsEnabled && vm.IsScheduled);
        Assert.Equal("İzindeyim.\nAcil: 1234", vm.InternalMessage);
        Assert.True(vm.ReplyToExternal);
        Assert.Equal(0, vm.ExternalAudienceIndex);
        Assert.Equal("ben@sirket.com", server.Request("GetUserOofSettingsRequest").Descendants(T + "Address").Single().Value);

        await vm.TurnOffCommand.ExecuteAsync(null);

        Assert.False(vm.IsActive);
        var set = server.Request("SetUserOofSettingsRequest").Element(T + "UserOofSettings")!;
        Assert.Equal(["OofState", "ExternalAudience", "Duration", "InternalReply", "ExternalReply"], set.Elements().Select(e => e.Name.LocalName));
        Assert.Equal("Disabled", set.Element(T + "OofState")!.Value);
    }

    [Fact]
    public void Scheduled_auto_reply_is_active_only_inside_its_window()
    {
        var s = new AutoReplySettings(AutoReplyState.Scheduled,
            new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero), "", "", ExternalAudience.None);

        Assert.False(s.IsActive(new DateTimeOffset(2026, 10, 31, 9, 0, 0, TimeSpan.Zero)));
        Assert.True(s.IsActive(new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero)));
        Assert.False(s.IsActive(new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero)));
    }

    // ---- Calendar editing and availability ----

    [Fact]
    public async Task Updating_a_meeting_notifies_attendees_and_keeps_body_unless_changed()
    {
        var server = new FakeEwsServer().On("UpdateItem", Ok("UpdateItem", "<m:Items />"));
        using var client = server.CreateClient();

        await client.UpdateAppointmentAsync(new EwsItemId("E1", "c"), new NewAppointment
        {
            Subject = "Yeni konu",
            Start = new DateTimeOffset(2026, 10, 28, 10, 0, 0, TimeSpan.FromHours(3)),
            End = new DateTimeOffset(2026, 10, 28, 11, 0, 0, TimeSpan.FromHours(3)),
            ReminderMinutes = null,
        }, updateBody: false, updateAttendees: false, notifyAttendees: true, TestContext.Current.CancellationToken);

        var request = server.Request("UpdateItem");
        Assert.Equal("SendToAllAndSaveCopy", (string?)request.Attribute("SendMeetingInvitationsOrCancellations"));
        var uris = request.Descendants(T + "FieldURI").Select(f => (string)f.Attribute("FieldURI")!).ToList();
        Assert.Contains("calendar:Start", uris);
        Assert.DoesNotContain("item:Body", uris);
        Assert.DoesNotContain("calendar:RequiredAttendees", uris);
        Assert.Equal("false", request.Descendants(T + "ReminderIsSet").Single().Value);
        Assert.Equal("2026-10-28T07:00:00Z", request.Descendants(T + "Start").Single().Value);
    }

    [Fact]
    public async Task Moving_an_appointment_only_changes_start_and_end()
    {
        var server = new FakeEwsServer().On("UpdateItem", Ok("UpdateItem", "<m:Items />"));
        using var client = server.CreateClient();
        var start = new DateTimeOffset(2026, 10, 29, 14, 0, 0, TimeSpan.Zero);

        await client.MoveAppointmentAsync(new EwsItemId("E1", "c"), start, start.AddMinutes(45), notifyAttendees: false, TestContext.Current.CancellationToken);

        var request = server.Request("UpdateItem");
        Assert.Equal("SendToNone", (string?)request.Attribute("SendMeetingInvitationsOrCancellations"));
        Assert.Equal(["calendar:Start", "calendar:End"], request.Descendants(T + "FieldURI").Select(f => (string)f.Attribute("FieldURI")!));
        Assert.Equal("2026-10-29T14:45:00Z", request.Descendants(T + "End").Single().Value);
    }

    [Fact]
    public async Task Availability_is_requested_in_utc_and_parsed_per_attendee()
    {
        var server = new FakeEwsServer().On("GetUserAvailabilityRequest", """
            <GetUserAvailabilityResponse xmlns="http://schemas.microsoft.com/exchange/services/2006/messages">
              <FreeBusyResponseArray>
                <FreeBusyResponse>
                  <ResponseMessage ResponseClass="Success"><ResponseCode>NoError</ResponseCode></ResponseMessage>
                  <FreeBusyView>
                    <FreeBusyViewType xmlns="http://schemas.microsoft.com/exchange/services/2006/types">FreeBusy</FreeBusyViewType>
                    <CalendarEventArray xmlns="http://schemas.microsoft.com/exchange/services/2006/types">
                      <CalendarEvent><StartTime>2026-10-28T07:00:00</StartTime><EndTime>2026-10-28T08:00:00</EndTime><BusyType>Busy</BusyType></CalendarEvent>
                      <CalendarEvent><StartTime>2026-10-28T09:00:00</StartTime><EndTime>2026-10-28T09:30:00</EndTime><BusyType>Free</BusyType></CalendarEvent>
                      <CalendarEvent><StartTime>2026-10-28T11:00:00</StartTime><EndTime>2026-10-28T12:00:00</EndTime><BusyType>Tentative</BusyType></CalendarEvent>
                    </CalendarEventArray>
                  </FreeBusyView>
                </FreeBusyResponse>
                <FreeBusyResponse>
                  <ResponseMessage ResponseClass="Error"><MessageText>Mailbox not found.</MessageText><ResponseCode>ErrorMailRecipientNotFound</ResponseCode></ResponseMessage>
                </FreeBusyResponse>
              </FreeBusyResponseArray>
            </GetUserAvailabilityResponse>
            """);
        using var client = server.CreateClient();
        var start = new DateTimeOffset(2026, 10, 28, 0, 0, 0, TimeSpan.FromHours(3));

        var result = await client.GetAvailabilityAsync(["ali@sirket.com", "yok@sirket.com"], start, start.AddDays(1), TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Count);
        var ali = result[0];
        Assert.Null(ali.Error);
        Assert.Equal(2, ali.Busy.Count); // "Free" blocks are dropped
        Assert.Equal(new DateTimeOffset(2026, 10, 28, 7, 0, 0, TimeSpan.Zero), ali.Busy[0].Start);
        Assert.Equal(FreeBusyStatus.Tentative, ali.Busy[1].Status);
        Assert.Equal("Mailbox not found.", result[1].Error);

        var request = server.Request("GetUserAvailabilityRequest");
        Assert.Equal("2026-10-27T21:00:00", request.Descendants(T + "StartTime").Single().Value);
        Assert.Equal(["TimeZone", "MailboxDataArray", "FreeBusyViewOptions"], request.Elements().Select(e => e.Name.LocalName));
    }

    [Fact]
    public async Task Updating_a_contact_sets_and_clears_indexed_fields()
    {
        var server = new FakeEwsServer().On("UpdateItem", Ok("UpdateItem", "<m:Items />"));
        using var client = server.CreateClient();

        await client.UpdateContactAsync(new EwsItemId("C1", "c"), new NewContact
        {
            GivenName = "Ayşe", Surname = "Kaya", Email = "ayse@yeni.com", MobilePhone = "555",
        }, TestContext.Current.CancellationToken);

        var updates = server.Request("UpdateItem").Descendants(T + "Updates").Single().Elements().ToList();
        var setEmail = updates.Single(u => u.Name.LocalName == "SetItemField" && u.Element(T + "IndexedFieldURI") is { } f && (string?)f.Attribute("FieldIndex") == "EmailAddress1");
        Assert.Equal("ayse@yeni.com", setEmail.Descendants(T + "Entry").Single().Value);
        Assert.Contains(updates, u => u.Name.LocalName == "DeleteItemField" && (string?)u.Element(T + "IndexedFieldURI")?.Attribute("FieldIndex") == "BusinessPhone");
        Assert.Contains(updates, u => u.Name.LocalName == "DeleteItemField" && (string?)u.Element(T + "FieldURI")?.Attribute("FieldURI") == "contacts:CompanyName");
        Assert.Equal("Ayşe Kaya", updates[0].Descendants(T + "DisplayName").Single().Value);
    }

    // ---- HTML helpers ----

    [Fact]
    public void Html_to_plain_text_keeps_line_structure()
    {
        Assert.Equal("Merhaba,\nYarın izindeyim.\n\nAli & Co", MailFormat.HtmlToPlainText(
            "<html><head><style>p{}</style></head><body><p>Merhaba,<br>Yarın izindeyim.</p><p></p><div>Ali &amp; Co</div></body></html>"));
    }

    [Fact]
    public void Extract_body_html_drops_document_wrapper_and_scripts()
    {
        Assert.Equal("<p>a</p>", MailFormat.ExtractBodyHtml("<html><head><title>t</title></head><body class=\"x\"><p>a</p><script>alert(1)</script></body></html>"));
        Assert.Equal("<b>frag</b>", MailFormat.ExtractBodyHtml("<b>frag</b>"));
    }
}
