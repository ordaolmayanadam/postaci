using System.Xml.Linq;
using ExchangeMail.Core.Models;
using ExchangeMail.ViewModels;

namespace ExchangeMail.Core.Tests;

/// <summary>Person cards from the directory, and replies/forwards that show the original in the editor.</summary>
public class PersonAndForwardTests
{
    private static readonly XNamespace T = FakeEwsServer.T;
    private static readonly XNamespace M = FakeEwsServer.M;

    private static string Ok(string operation, string inner = "") =>
        $"""<m:{operation}Response><m:ResponseMessages><m:{operation}ResponseMessage ResponseClass="Success"><m:ResponseCode>NoError</m:ResponseCode>{inner}</m:{operation}ResponseMessage></m:ResponseMessages></m:{operation}Response>""";

    private static string Person(string name, string email, string? title, string? managerName, string? managerEmail, string reports = "", string photo = "") => Ok("ResolveNames", $"""
        <m:ResolutionSet TotalItemsInView="1" IncludesLastItemInRange="true"><t:Resolution>
          <t:Mailbox><t:Name>{name}</t:Name><t:EmailAddress>{email}</t:EmailAddress><t:RoutingType>SMTP</t:RoutingType><t:MailboxType>Mailbox</t:MailboxType></t:Mailbox>
          <t:Contact>
            <t:DisplayName>{name}</t:DisplayName>
            <t:CompanyName>Şirket</t:CompanyName>
            <t:PhoneNumbers><t:Entry Key="BusinessPhone">0312 000 00 00</t:Entry></t:PhoneNumbers>
            <t:Department>Bilgi Teknolojileri</t:Department>
            {(title is null ? "" : $"<t:JobTitle>{title}</t:JobTitle>")}
            {(managerName is null ? "" : $"<t:Manager>{managerName}</t:Manager>")}
            <t:OfficeLocation>B Blok</t:OfficeLocation>
            {(managerEmail is null ? "" : $"<t:ManagerMailbox><t:Mailbox><t:Name>{managerName}</t:Name><t:EmailAddress>{managerEmail}</t:EmailAddress></t:Mailbox></t:ManagerMailbox>")}
            {(reports.Length == 0 ? "" : $"<t:DirectReports>{reports}</t:DirectReports>")}
            {(photo.Length == 0 ? "" : $"<t:Photo>{photo}</t:Photo>")}
          </t:Contact>
        </t:Resolution></m:ResolutionSet>
        """);

    [Fact]
    public async Task Directory_lookup_asks_for_all_properties_by_exact_address()
    {
        var server = new FakeEwsServer().On("ResolveNames", Person("Ayşe Kaya", "ayse@sirket.com", "Uzman", "Ali Veli", "ali@sirket.com",
            reports: "<t:Mailbox><t:Name>Can Er</t:Name><t:EmailAddress>can@sirket.com</t:EmailAddress></t:Mailbox>", photo: "AQID"));
        using var client = server.CreateClient();

        var person = await client.GetDirectoryPersonAsync("ayse@sirket.com", TestContext.Current.CancellationToken);

        var request = server.Request("ResolveNames");
        Assert.Equal("AllProperties", (string?)request.Attribute("ContactDataShape"));
        Assert.Equal("ActiveDirectory", (string?)request.Attribute("SearchScope"));
        Assert.Equal("smtp:ayse@sirket.com", request.Element(M + "UnresolvedEntry")!.Value);

        Assert.NotNull(person);
        Assert.Equal(("Ayşe Kaya", "Uzman", "Bilgi Teknolojileri", "B Blok", "0312 000 00 00"),
            (person.DisplayName, person.JobTitle, person.Department, person.Office, person.BusinessPhone));
        Assert.Equal(("Ali Veli", "ali@sirket.com"), (person.ManagerName, person.ManagerEmail));
        Assert.Equal(["can@sirket.com"], person.DirectReports.Select(r => r.Address));
        Assert.Equal([1, 2, 3], person.Photo);
    }

    [Fact]
    public async Task External_addresses_have_no_directory_entry()
    {
        var server = new FakeEwsServer().On("ResolveNames", """
            <m:ResolveNamesResponse><m:ResponseMessages><m:ResolveNamesResponseMessage ResponseClass="Error"><m:MessageText>No results were found.</m:MessageText><m:ResponseCode>ErrorNameResolutionNoResults</m:ResponseCode></m:ResolveNamesResponseMessage></m:ResponseMessages></m:ResolveNamesResponse>
            """);
        using var client = server.CreateClient();

        Assert.Null(await client.GetDirectoryPersonAsync("biri@disari.com", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Card_walks_the_manager_chain_to_the_top()
    {
        var server = new FakeEwsServer()
            .On("ResolveNames", Person("Ayşe Kaya", "ayse@sirket.com", "Uzman", "Ali Veli", "ali@sirket.com"))
            .On("ResolveNames", Person("Ali Veli", "ali@sirket.com", "Müdür", "Genel Müdür", null))
            .On("ResolveNames", Person("Genel Müdür", "gm@sirket.com", "Genel Müdür", null, null));
        var card = new PersonCardViewModel(server.CreateClient());

        await card.LoadAsync(new MailboxAddress("Ayşe Kaya", "ayse@sirket.com"));

        Assert.Equal("Uzman · Bilgi Teknolojileri", card.Title);
        Assert.Equal(["Genel Müdür", "Ali Veli"], card.Managers.Select(m => m.DisplayName));
        // The manager without a mailbox in the entry is looked up by name.
        Assert.Equal(["smtp:ayse@sirket.com", "smtp:ali@sirket.com", "Genel Müdür"],
            server.Requests.Select(r => r.Element(M + "UnresolvedEntry")!.Value));
        Assert.False(card.IsLoading || card.IsLoadingManagers);
    }

    [Fact]
    public async Task Card_stops_when_the_directory_loops()
    {
        var server = new FakeEwsServer()
            .On("ResolveNames", Person("A", "a@sirket.com", null, "B", "b@sirket.com"))
            .On("ResolveNames", Person("B", "b@sirket.com", null, "A", "a@sirket.com"));
        var card = new PersonCardViewModel(server.CreateClient());

        await card.LoadAsync(new MailboxAddress("A", "a@sirket.com"));

        Assert.Equal(["B"], card.Managers.Select(m => m.DisplayName));
        Assert.Equal(2, server.Requests.Count);
    }

    // ---- Forward with the original in the editor ----

    private const string OriginalHtml =
        "<html><head><style>p.MsoNormal{margin:0}</style></head><body><p class=\"MsoNormal\">Yeni metin</p><hr><p>Özgün ileti <img src=\"cid:logo@x\"></p></body></html>";

    private static string DraftDetail() => Ok("GetItem", $"""
        <m:Items><t:Message><t:ItemId Id="D1" ChangeKey="k3" /><t:Subject>İLT: Teklif</t:Subject>
          <t:Body BodyType="HTML">{System.Security.SecurityElement.Escape(OriginalHtml)}</t:Body>
          <t:Attachments>
            <t:FileAttachment><t:AttachmentId Id="LOGO" /><t:Name>logo.png</t:Name><t:ContentType>image/png</t:ContentType><t:ContentId>logo@x</t:ContentId><t:Size>3</t:Size><t:IsInline>true</t:IsInline></t:FileAttachment>
            <t:FileAttachment><t:AttachmentId Id="PDF" /><t:Name>teklif.pdf</t:Name><t:Size>1000</t:Size><t:IsInline>false</t:IsInline></t:FileAttachment>
          </t:Attachments><t:IsRead>true</t:IsRead></t:Message></m:Items>
        """);

    private static (FakeEwsServer Server, ComposeViewModel Vm, List<string> Bodies) Forward(params (string Operation, string Body)[] after)
    {
        var server = new FakeEwsServer()
            .On("GetItem", Ok("GetItem", """<m:Items><t:Message><t:ItemId Id="O" ChangeKey="o2" /></t:Message></m:Items>"""))
            .On("CreateItem", Ok("CreateItem", """<m:Items><t:Message><t:ItemId Id="D1" ChangeKey="k1" /></t:Message></m:Items>"""))
            .On("UpdateItem", Ok("UpdateItem", """<m:Items><t:Message><t:ItemId Id="D1" ChangeKey="k2" /></t:Message></m:Items>"""))
            .On("GetItem", DraftDetail())
            .On("GetAttachment", Ok("GetAttachment", """<m:Attachments><t:FileAttachment><t:AttachmentId Id="LOGO" /><t:Name>logo.png</t:Name><t:ContentType>image/png</t:ContentType><t:Content>AQID</t:Content></t:FileAttachment></m:Attachments>"""));
        foreach (var (operation, body) in after) server.On(operation, body);

        var original = new MailDetail(new EwsItemId("O", "o1"), "Teklif", new MailboxAddress("Ali", "ali@sirket.com"), [], [], [],
            null, DateTimeOffset.Now, "<p>Özgün ileti</p>", true, true, Importance.Normal, []);
        var vm = new ComposeViewModel(new MailSession(server.CreateClient(), new AccountSettings { Email = "ben@sirket.com" }), ComposeMode.Forward, original);
        var bodies = new List<string>();
        vm.BodyReplaced += (_, html) => bodies.Add(html);
        string current = vm.InitialHtml;
        vm.BodyReplaced += (_, html) => current = html;
        vm.ReadBodyHtmlAsync = () => Task.FromResult(current);
        return (server, vm, bodies);
    }

    [Fact]
    public async Task Forward_loads_the_original_with_images_and_attachments_into_the_editor()
    {
        var (server, vm, bodies) = Forward();
        Assert.True(vm.IsResponse);

        await vm.PrepareAsync();

        Assert.Equal(["GetItem", "CreateItem", "UpdateItem", "GetItem", "GetAttachment"], server.Operations);
        Assert.Contains(server.Request("CreateItem").Descendants(T + "ForwardItem"), _ => true);
        Assert.False(vm.IsResponse); // the quote is in the editor now
        var html = Assert.Single(bodies);
        Assert.Contains("Özgün ileti", html);
        Assert.Contains("src=\"data:image/png;base64,AQID\"", html);
        Assert.DoesNotContain("<style>", html); // the head is kept aside
        Assert.Equal(["teklif.pdf"], vm.ExistingAttachments.Select(a => a.Name));
        Assert.True(vm.HasAnyAttachments);
        Assert.StartsWith("Ekler · toplam", vm.AttachmentsHeader);
        Assert.False(vm.IsLoadingOriginal);
        Assert.Null(vm.ErrorMessage);
        Assert.Null(vm.InfoMessage); // no leftover banner
    }

    [Fact]
    public async Task Sender_photos_are_fetched_once_per_address()
    {
        var server = new FakeEwsServer().On("ResolveNames", Person("Ayşe Kaya", "ayse@sirket.com", null, null, null, photo: "AQID"));
        var vm = new MainViewModel(new MailSession(server.CreateClient(), new AccountSettings { Email = "ben@sirket.com" }));

        Assert.Equal([1, 2, 3], await vm.GetPhotoAsync("ayse@sirket.com"));
        Assert.Equal([1, 2, 3], await vm.GetPhotoAsync("AYSE@sirket.com"));
        Assert.Null(await vm.GetPhotoAsync("Ayşe Kaya")); // not an address: no lookup
        Assert.Single(server.Requests);
    }

    [Fact]
    public void Folder_list_density_is_a_preference()
    {
        var session = new MailSession(new FakeEwsServer().CreateClient(), new AccountSettings { Email = "ben@sirket.com" });
        var store = new SettingsStore(Path.Combine(Path.GetTempPath(), "postaci-tests-" + Guid.NewGuid().ToString("N")));
        var settings = new SettingsViewModel(session, store, new NoCredentials());

        Assert.Equal(0, settings.FolderDensityIndex); // compact by default
        settings.FolderDensityIndex = 1;
        Assert.False(session.Preferences.CompactFolderList);
        Assert.False(store.LoadPreferences().CompactFolderList);
    }

    [Fact]
    public async Task A_second_send_while_the_first_is_preparing_is_ignored()
    {
        var server = new FakeEwsServer().On("CreateItem", Ok("CreateItem"));
        var vm = new ComposeViewModel(new MailSession(server.CreateClient(), new AccountSettings { Email = "ben@sirket.com" }), ComposeMode.New, null)
        {
            To = "ayse@sirket.com; ",
            Subject = "Merhaba",
        };
        var body = new TaskCompletionSource<string>();
        vm.ReadBodyHtmlAsync = () => body.Task;
        var sent = 0;
        vm.Sent += (_, _) => sent++;

        var first = vm.SendCommand.ExecuteAsync(null);
        var second = vm.SendCommand.ExecuteAsync(null); // e.g. Ctrl+Enter right after the click
        body.SetResult("<p>Merhaba</p>");
        await Task.WhenAll(first, second);

        Assert.Single(server.Operations, "CreateItem");
        Assert.Equal(1, sent);
    }

    [Fact]
    public async Task A_reply_can_take_the_original_attachments_along()
    {
        var server = new FakeEwsServer()
            .On("GetAttachment", Ok("GetAttachment", """<m:Attachments><t:FileAttachment><t:AttachmentId Id="A1" /><t:Name>teklif.pdf</t:Name><t:ContentType>application/pdf</t:ContentType><t:Content>AQID</t:Content></t:FileAttachment></m:Attachments>"""))
            .On("GetAttachment", Ok("GetAttachment", """<m:Attachments><t:FileAttachment><t:AttachmentId Id="A2" /><t:Name>liste.xlsx</t:Name><t:Content>BAUG</t:Content></t:FileAttachment></m:Attachments>"""));
        var original = new MailDetail(new EwsItemId("O", "o1"), "Teklif", new MailboxAddress("Ali", "ali@sirket.com"), [], [], [],
            null, DateTimeOffset.Now, "<p>Ekte</p>", true, true, Importance.Normal,
            [
                new AttachmentInfo("A1", "teklif.pdf", "application/pdf", null, 3, false, false),
                new AttachmentInfo("A2", "liste.xlsx", null, null, 3, false, false),
                new AttachmentInfo("LOGO", "logo.png", "image/png", "logo@x", 3, true, false),
            ]);
        var session = new MailSession(server.CreateClient(), new AccountSettings { Email = "ben@sirket.com" });

        var reply = new ComposeViewModel(session, ComposeMode.Reply, original);
        Assert.True(reply.CanAddOriginalAttachments);
        Assert.Equal("Özgün iletinin eklerini ekle (2)", reply.AddOriginalAttachmentsText); // inline images don't count

        await reply.AddOriginalAttachmentsCommand.ExecuteAsync(null);

        Assert.Equal(["teklif.pdf", "liste.xlsx"], reply.Attachments.Select(a => a.Name));
        Assert.Equal([1, 2, 3], reply.Attachments[0].Content);
        Assert.False(reply.CanAddOriginalAttachments);
        // Forwards already carry them.
        Assert.False(new ComposeViewModel(session, ComposeMode.Forward, original).CanAddOriginalAttachments);
    }

    private sealed class NoCredentials : ICredentialStore
    {
        public string? GetPassword(string userName) => null;
        public void SavePassword(string userName, string password) { }
        public void RemovePassword(string userName) { }
    }

    [Fact]
    public async Task Sending_restores_cid_images_and_the_original_styles()
    {
        var (server, vm, _) = Forward(
            ("UpdateItem", Ok("UpdateItem", """<m:Items><t:Message><t:ItemId Id="D1" ChangeKey="k4" /></t:Message></m:Items>""")),
            ("SendItem", Ok("SendItem")));
        await vm.PrepareAsync();
        vm.To = "ayse@sirket.com; ";

        await vm.SendCommand.ExecuteAsync(null);

        var body = server.Requests.Where(r => r.Name.LocalName == "UpdateItem").Last().Descendants(T + "Body").Single().Value;
        Assert.Contains("src=\"cid:logo@x\"", body);
        Assert.DoesNotContain("data:image", body);
        Assert.StartsWith("<html><head><style>p.MsoNormal{margin:0}</style></head><body>", body);
        Assert.Equal("SendItem", server.Operations.Last());
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task Discarding_removes_the_automatic_draft_but_a_saved_one_stays()
    {
        var (server, vm, _) = Forward(("DeleteItem", Ok("DeleteItem")));
        await vm.PrepareAsync();

        await vm.DiscardAsync();

        var delete = server.Request("DeleteItem");
        Assert.Equal("SoftDelete", (string?)delete.Attribute("DeleteType"));
        Assert.Equal("D1", (string?)delete.Descendants(T + "ItemId").Single().Attribute("Id"));

        var (server2, vm2, _) = Forward(
            ("UpdateItem", Ok("UpdateItem", """<m:Items><t:Message><t:ItemId Id="D1" ChangeKey="k5" /></t:Message></m:Items>""")),
            ("GetItem", DraftDetail()),
            ("GetAttachment", Ok("GetAttachment", """<m:Attachments><t:FileAttachment><t:AttachmentId Id="LOGO" /><t:Content>AQID</t:Content></t:FileAttachment></m:Attachments>""")));
        await vm2.PrepareAsync();
        await vm2.SaveDraftCommand.ExecuteAsync(null);
        await vm2.DiscardAsync();
        Assert.DoesNotContain("DeleteItem", server2.Operations);
    }
}
