using System.Net;
using System.Xml.Linq;
using ExchangeMail.Core.Ews;
using ExchangeMail.Core.Models;

namespace ExchangeMail.Core.Tests;

public class EwsClientTests
{
    private static readonly XNamespace T = FakeEwsServer.T;
    private static readonly XNamespace M = FakeEwsServer.M;

    private static string Ok(string operation, string inner) =>
        $"""<m:{operation}Response><m:ResponseMessages><m:{operation}ResponseMessage ResponseClass="Success"><m:ResponseCode>NoError</m:ResponseCode>{inner}</m:{operation}ResponseMessage></m:ResponseMessages></m:{operation}Response>""";

    private static string FolderMsg(string id) =>
        $"""<m:GetFolderResponseMessage ResponseClass="Success"><m:ResponseCode>NoError</m:ResponseCode><m:Folders><t:Folder><t:FolderId Id="{id}" ChangeKey="ck" /></t:Folder></m:Folders></m:GetFolderResponseMessage>""";

    private const string WellKnownResponse =
        "<m:GetFolderResponse><m:ResponseMessages>" +
        """<m:GetFolderResponseMessage ResponseClass="Success"><m:ResponseCode>NoError</m:ResponseCode><m:Folders><t:Folder><t:FolderId Id="ROOT" /></t:Folder></m:Folders></m:GetFolderResponseMessage>""" +
        """<m:GetFolderResponseMessage ResponseClass="Success"><m:ResponseCode>NoError</m:ResponseCode><m:Folders><t:Folder><t:FolderId Id="INBOX" /></t:Folder></m:Folders></m:GetFolderResponseMessage>""" +
        """<m:GetFolderResponseMessage ResponseClass="Success"><m:ResponseCode>NoError</m:ResponseCode><m:Folders><t:Folder><t:FolderId Id="DRAFTS" /></t:Folder></m:Folders></m:GetFolderResponseMessage>""" +
        """<m:GetFolderResponseMessage ResponseClass="Success"><m:ResponseCode>NoError</m:ResponseCode><m:Folders><t:Folder><t:FolderId Id="SENT" /></t:Folder></m:Folders></m:GetFolderResponseMessage>""" +
        """<m:GetFolderResponseMessage ResponseClass="Success"><m:ResponseCode>NoError</m:ResponseCode><m:Folders><t:Folder><t:FolderId Id="DELETED" /></t:Folder></m:Folders></m:GetFolderResponseMessage>""" +
        """<m:GetFolderResponseMessage ResponseClass="Error"><m:MessageText>not found</m:MessageText><m:ResponseCode>ErrorFolderNotFound</m:ResponseCode></m:GetFolderResponseMessage>""" +
        """<m:GetFolderResponseMessage ResponseClass="Success"><m:ResponseCode>NoError</m:ResponseCode><m:Folders><t:Folder><t:FolderId Id="OUTBOX" /></t:Folder></m:Folders></m:GetFolderResponseMessage>""" +
        "</m:ResponseMessages></m:GetFolderResponse>";

    private static string Folder(string id, string parent, string name, string? folderClass = "IPF.Note", int unread = 0, bool hidden = false) =>
        $"""
        <t:Folder>
          <t:FolderId Id="{id}" ChangeKey="x" /><t:ParentFolderId Id="{parent}" />
          {(folderClass is null ? "" : $"<t:FolderClass>{folderClass}</t:FolderClass>")}
          <t:DisplayName>{name}</t:DisplayName><t:TotalCount>10</t:TotalCount><t:ChildFolderCount>0</t:ChildFolderCount><t:UnreadCount>{unread}</t:UnreadCount>
          {(hidden ? """<t:ExtendedProperty><t:ExtendedFieldURI PropertyTag="0x10f4" PropertyType="Boolean" /><t:Value>true</t:Value></t:ExtendedProperty>""" : "")}
        </t:Folder>
        """;

    [Fact]
    public async Task GetFolderTree_builds_hierarchy_and_orders_well_known_folders_first()
    {
        var server = new FakeEwsServer()
            .On("GetFolder", WellKnownResponse)
            .On("FindFolder", Ok("FindFolder", "<m:RootFolder TotalItemsInView=\"7\" IncludesLastItemInRange=\"true\"><t:Folders>" +
                Folder("A", "ROOT", "Arşiv") +
                Folder("SENT", "ROOT", "Gönderilmiş Öğeler") +
                Folder("INBOX", "ROOT", "Gelen Kutusu", unread: 3) +
                Folder("SUB", "INBOX", "Projeler") +
                Folder("HID", "ROOT", "Sync Issues", hidden: true) +
                Folder("HIDCHILD", "HID", "Conflicts") +
                Folder("NOCLASS", "ROOT", "Eski") +
                "</t:Folders></m:RootFolder>"));
        // Calendar folders come back as t:CalendarFolder and must be ignored.
        using var client = server.CreateClient();

        var tree = await client.GetFolderTreeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["Gelen Kutusu", "Gönderilmiş Öğeler", "Arşiv", "Eski"], tree.Select(f => f.DisplayName));
        Assert.Equal(WellKnownFolder.Inbox, tree[0].WellKnown);
        Assert.Equal(3, tree[0].UnreadCount);
        Assert.Equal("Projeler", Assert.Single(tree[0].Children).DisplayName);
        Assert.Equal(WellKnownFolder.SentItems, tree[1].WellKnown);

        var findFolder = server.Request("FindFolder");
        Assert.Equal("Deep", (string?)findFolder.Attribute("Traversal"));
        Assert.Equal("ROOT", (string?)findFolder.Element(M + "ParentFolderIds")!.Element(T + "FolderId")!.Attribute("Id"));
    }

    [Fact]
    public async Task FindItems_parses_summaries_and_paging_and_puts_query_after_parent_folder()
    {
        var server = new FakeEwsServer().On("FindItem", Ok("FindItem", """
            <m:RootFolder IndexedPagingOffset="2" TotalItemsInView="120" IncludesLastItemInRange="false"><t:Items>
              <t:Message>
                <t:ItemId Id="I1" ChangeKey="C1" /><t:ItemClass>IPM.Note</t:ItemClass><t:Subject>Rapor</t:Subject>
                <t:DateTimeReceived>2026-10-20T08:30:00Z</t:DateTimeReceived><t:HasAttachments>true</t:HasAttachments>
                <t:Importance>High</t:Importance><t:DisplayTo>Ayşe Kaya</t:DisplayTo><t:Preview>Merhaba,
            ekte</t:Preview>
                <t:IsRead>false</t:IsRead>
                <t:From><t:Mailbox><t:Name>Ali Veli</t:Name><t:EmailAddress>ali@sirket.com</t:EmailAddress></t:Mailbox></t:From>
              </t:Message>
              <t:MeetingRequest><t:ItemId Id="I2" ChangeKey="C2" /><t:ItemClass>IPM.Schedule.Meeting.Request</t:ItemClass><t:Subject>Toplantı</t:Subject>
                <t:DateTimeReceived>2026-10-19T08:30:00Z</t:DateTimeReceived><t:IsRead>true</t:IsRead></t:MeetingRequest>
            </t:Items></m:RootFolder>
            """));
        using var client = server.CreateClient();

        var page = await client.FindItemsAsync("INBOX", 0, 2, "from:ali", TestContext.Current.CancellationToken);

        Assert.Equal(2, page.Items.Count);
        Assert.Equal(120, page.TotalCount);
        Assert.Equal(2, page.NextOffset);
        Assert.False(page.IsLastPage);
        var first = page.Items[0];
        Assert.Equal(new EwsItemId("I1", "C1"), first.ItemId);
        Assert.Equal("Ali Veli", first.From!.Name);
        Assert.False(first.IsRead);
        Assert.True(first.HasAttachments);
        Assert.Equal(Importance.High, first.Importance);
        Assert.Equal(new DateTimeOffset(2026, 10, 20, 8, 30, 0, TimeSpan.Zero), first.Received);
        Assert.Equal("IPM.Schedule.Meeting.Request", page.Items[1].ItemClass);

        var request = server.Request("FindItem");
        Assert.Equal(["ItemShape", "IndexedPageItemView", "SortOrder", "ParentFolderIds", "QueryString"],
            request.Elements().Select(e => e.Name.LocalName));
        Assert.Equal("from:ali", request.Element(M + "QueryString")!.Value);
    }

    [Fact]
    public async Task GetItem_parses_recipients_body_and_attachments()
    {
        var server = new FakeEwsServer().On("GetItem", Ok("GetItem", """
            <m:Items><t:Message>
              <t:ItemId Id="I1" ChangeKey="C9" /><t:Subject>Teklif</t:Subject>
              <t:Body BodyType="HTML">&lt;p&gt;Merhaba &lt;img src="cid:logo@01"&gt;&lt;/p&gt;</t:Body>
              <t:Attachments>
                <t:FileAttachment><t:AttachmentId Id="A1" /><t:Name>teklif.pdf</t:Name><t:ContentType>application/pdf</t:ContentType><t:Size>2048</t:Size><t:IsInline>false</t:IsInline></t:FileAttachment>
                <t:FileAttachment><t:AttachmentId Id="A2" /><t:Name>logo.png</t:Name><t:ContentType>image/png</t:ContentType><t:ContentId>logo@01</t:ContentId><t:Size>100</t:Size><t:IsInline>true</t:IsInline></t:FileAttachment>
                <t:ItemAttachment><t:AttachmentId Id="A3" /><t:Name>Eski ileti</t:Name></t:ItemAttachment>
              </t:Attachments>
              <t:DateTimeSent>2026-10-20T08:29:00Z</t:DateTimeSent><t:DateTimeReceived>2026-10-20T08:30:00Z</t:DateTimeReceived>
              <t:Importance>Normal</t:Importance>
              <t:ToRecipients><t:Mailbox><t:Name>Ben</t:Name><t:EmailAddress>ben@sirket.com</t:EmailAddress></t:Mailbox></t:ToRecipients>
              <t:CcRecipients><t:Mailbox><t:EmailAddress>cc@sirket.com</t:EmailAddress></t:Mailbox></t:CcRecipients>
              <t:IsRead>true</t:IsRead>
              <t:From><t:Mailbox><t:Name>Ali</t:Name><t:EmailAddress>ali@sirket.com</t:EmailAddress></t:Mailbox></t:From>
            </t:Message></m:Items>
            """));
        using var client = server.CreateClient();

        var detail = await client.GetItemAsync(new EwsItemId("I1", "C1"), TestContext.Current.CancellationToken);

        Assert.True(detail.IsHtml);
        Assert.Contains("cid:logo@01", detail.Body);
        Assert.Equal("ben@sirket.com", Assert.Single(detail.To).Address);
        Assert.Equal("cc@sirket.com", Assert.Single(detail.Cc).Display);
        Assert.Equal(3, detail.Attachments.Count);
        Assert.True(detail.Attachments[1].IsInline);
        Assert.True(detail.Attachments[2].IsItemAttachment);

        var shape = server.Request("GetItem").Element(M + "ItemShape")!;
        Assert.Equal(["BaseShape", "BodyType", "FilterHtmlContent", "AdditionalProperties"], shape.Elements().Select(e => e.Name.LocalName));
    }

    [Fact]
    public async Task Send_new_message_without_attachments_is_a_single_CreateItem_with_schema_order()
    {
        var server = new FakeEwsServer().On("CreateItem", Ok("CreateItem", "<m:Items />"));
        using var client = server.CreateClient();

        await client.SendAsync(new OutgoingMessage
        {
            To = ["a@sirket.com"],
            Bcc = ["b@sirket.com"],
            Subject = "Selam",
            HtmlBody = "<p>Merhaba</p>",
            Importance = Importance.High,
        }, TestContext.Current.CancellationToken);

        var request = server.Request("CreateItem");
        Assert.Equal("SendAndSaveCopy", (string?)request.Attribute("MessageDisposition"));
        var message = request.Element(M + "Items")!.Element(T + "Message")!;
        Assert.Equal(["Subject", "Body", "Importance", "ToRecipients", "BccRecipients"], message.Elements().Select(e => e.Name.LocalName));
        Assert.Equal("<p>Merhaba</p>", message.Element(T + "Body")!.Value);
        Assert.Equal("HTML", (string?)message.Element(T + "Body")!.Attribute("BodyType"));
    }

    [Fact]
    public async Task Send_reply_with_attachment_creates_draft_sets_recipients_attaches_and_sends_with_latest_change_key()
    {
        var server = new FakeEwsServer()
            .On("GetItem", Ok("GetItem", """<m:Items><t:Message><t:ItemId Id="ORIG" ChangeKey="FRESH" /></t:Message></m:Items>"""))
            .On("CreateItem", Ok("CreateItem", """<m:Items><t:Message><t:ItemId Id="DRAFT" ChangeKey="K1" /></t:Message></m:Items>"""))
            .On("UpdateItem", Ok("UpdateItem", """<m:Items><t:Message><t:ItemId Id="DRAFT" ChangeKey="K2" /></t:Message></m:Items>"""))
            .On("CreateAttachment", Ok("CreateAttachment", """<m:Attachments><t:FileAttachment><t:AttachmentId Id="ATT" RootItemId="DRAFT" RootItemChangeKey="K3" /></t:FileAttachment></m:Attachments>"""))
            .On("SendItem", Ok("SendItem", ""));
        using var client = server.CreateClient();

        await client.SendAsync(new OutgoingMessage
        {
            Mode = ComposeMode.Reply,
            ReferenceItemId = new EwsItemId("ORIG", "STALE"),
            To = ["ali@sirket.com"],
            Subject = "YNT: Teklif",
            HtmlBody = "<p>Teşekkürler</p>",
            Attachments = [new OutgoingAttachment("not.txt", "text/plain", "abc"u8.ToArray())],
        }, TestContext.Current.CancellationToken);

        Assert.Equal(["GetItem", "CreateItem", "UpdateItem", "CreateAttachment", "SendItem"], server.Operations);

        var reply = server.Request("CreateItem").Element(M + "Items")!.Element(T + "ReplyToItem")!;
        Assert.Equal("FRESH", (string?)reply.Element(T + "ReferenceItemId")!.Attribute("ChangeKey"));
        Assert.Equal(["ReferenceItemId", "NewBodyContent"], reply.Elements().Select(e => e.Name.LocalName));
        Assert.Equal("SaveOnly", (string?)server.Request("CreateItem").Attribute("MessageDisposition"));

        var updates = server.Request("UpdateItem").Descendants(T + "Updates").Single().Elements().ToList();
        Assert.Contains(updates, u => u.Name.LocalName == "SetItemField" && u.Descendants(T + "EmailAddress").Any(e => e.Value == "ali@sirket.com"));
        Assert.Contains(updates, u => u.Name.LocalName == "DeleteItemField" && (string?)u.Element(T + "FieldURI")!.Attribute("FieldURI") == "message:CcRecipients");
        Assert.DoesNotContain(updates, u => u.Descendants().Any(d => (string?)d.Attribute("FieldURI") == "message:BccRecipients"));

        Assert.Equal("K2", (string?)server.Request("CreateAttachment").Element(M + "ParentItemId")!.Attribute("ChangeKey"));
        Assert.Equal("YWJj", server.Request("CreateAttachment").Descendants(T + "Content").Single().Value);
        Assert.Equal("K3", (string?)server.Request("SendItem").Descendants(T + "ItemId").Single().Attribute("ChangeKey"));
    }

    [Fact]
    public async Task Error_response_class_becomes_EwsException_with_code()
    {
        var server = new FakeEwsServer().On("GetItem", """
            <m:GetItemResponse><m:ResponseMessages><m:GetItemResponseMessage ResponseClass="Error">
              <m:MessageText>The specified object was not found in the store.</m:MessageText><m:ResponseCode>ErrorItemNotFound</m:ResponseCode>
            </m:GetItemResponseMessage></m:ResponseMessages></m:GetItemResponse>
            """);
        using var client = server.CreateClient();

        var ex = await Assert.ThrowsAsync<EwsException>(() => client.GetItemAsync(new EwsItemId("X", null), TestContext.Current.CancellationToken));
        Assert.Equal("ErrorItemNotFound", ex.ResponseCode);
        Assert.Contains("not found", ex.Message);
    }

    [Fact]
    public async Task Unauthorized_becomes_EwsAuthenticationException()
    {
        var server = new FakeEwsServer().OnRaw("GetFolder", () => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var client = server.CreateClient();

        await Assert.ThrowsAsync<EwsAuthenticationException>(() => client.GetWellKnownFolderIdsAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Server_busy_fault_is_retried()
    {
        var fault = $"""
            <s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/"><s:Body><s:Fault>
              <faultcode xmlns:a="{FakeEwsServer.T}">a:ErrorServerBusy</faultcode><faultstring>The server cannot service this request right now.</faultstring>
              <detail><e:ResponseCode xmlns:e="http://schemas.microsoft.com/exchange/services/2006/errors">ErrorServerBusy</e:ResponseCode>
                <e:Message xmlns:e="http://schemas.microsoft.com/exchange/services/2006/errors">The server cannot service this request right now. Try again later.</e:Message>
                <t:MessageXml xmlns:t="{FakeEwsServer.T}"><t:Value Name="BackOffMilliseconds">10</t:Value></t:MessageXml></detail>
            </s:Fault></s:Body></s:Envelope>
            """;
        var server = new FakeEwsServer()
            .OnRaw("GetFolder", () => FakeEwsServer.Xml(HttpStatusCode.InternalServerError, fault))
            .On("GetFolder", WellKnownResponse);
        using var client = server.CreateClient();

        var ids = await client.GetWellKnownFolderIdsAsync(TestContext.Current.CancellationToken);

        Assert.Equal("INBOX", ids["inbox"]);
        Assert.False(ids.ContainsKey("junkemail"));
        Assert.Equal(2, server.Requests.Count);
    }

    [Fact]
    public async Task ResolveNames_without_results_returns_empty()
    {
        var server = new FakeEwsServer().On("ResolveNames", """
            <m:ResolveNamesResponse><m:ResponseMessages><m:ResolveNamesResponseMessage ResponseClass="Error">
              <m:MessageText>No results were found.</m:MessageText><m:ResponseCode>ErrorNameResolutionNoResults</m:ResponseCode>
            </m:ResolveNamesResponseMessage></m:ResponseMessages></m:ResolveNamesResponse>
            """);
        using var client = server.CreateClient();

        Assert.Empty(await client.ResolveNamesAsync("zzz", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ResolveNames_returns_multiple_matches_from_warning()
    {
        var server = new FakeEwsServer().On("ResolveNames", """
            <m:ResolveNamesResponse><m:ResponseMessages><m:ResolveNamesResponseMessage ResponseClass="Warning">
              <m:MessageText>Multiple results were found.</m:MessageText><m:ResponseCode>ErrorNameResolutionMultipleResults</m:ResponseCode>
              <m:ResolutionSet TotalItemsInView="2" IncludesLastItemInRange="true">
                <t:Resolution><t:Mailbox><t:Name>Ali Veli</t:Name><t:EmailAddress>ali@sirket.com</t:EmailAddress><t:RoutingType>SMTP</t:RoutingType></t:Mailbox></t:Resolution>
                <t:Resolution><t:Mailbox><t:Name>Ali Can</t:Name><t:EmailAddress>alican@sirket.com</t:EmailAddress><t:RoutingType>SMTP</t:RoutingType></t:Mailbox></t:Resolution>
              </m:ResolutionSet>
            </m:ResolveNamesResponseMessage></m:ResponseMessages></m:ResolveNamesResponse>
            """);
        using var client = server.CreateClient();

        var result = await client.ResolveNamesAsync("ali", TestContext.Current.CancellationToken);

        Assert.Equal(["ali@sirket.com", "alican@sirket.com"], result.Select(r => r.Address));
    }

    [Fact]
    public async Task GetAttachment_returns_item_attachment_as_eml()
    {
        var server = new FakeEwsServer().On("GetAttachment", Ok("GetAttachment", """
            <m:Attachments><t:ItemAttachment><t:AttachmentId Id="A3" /><t:Name>Eski: ileti?</t:Name>
              <t:Message><t:MimeContent CharacterSet="UTF-8">U3ViamVjdDogeA==</t:MimeContent></t:Message></t:ItemAttachment></m:Attachments>
            """));
        using var client = server.CreateClient();

        var content = await client.GetAttachmentAsync(new AttachmentInfo("A3", "Eski: ileti?", null, null, 0, false, true), TestContext.Current.CancellationToken);

        Assert.Equal("Eski_ ileti_.eml", content.FileName);
        Assert.Equal("Subject: x", System.Text.Encoding.UTF8.GetString(content.Content));
        Assert.NotNull(server.Request("GetAttachment").Element(M + "AttachmentShape"));
    }
}
