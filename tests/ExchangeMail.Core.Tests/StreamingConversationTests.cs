using System.Net;
using System.Text;
using System.Xml.Linq;
using ExchangeMail.Core.Ews;
using ExchangeMail.Core.Models;
using ExchangeMail.ViewModels;

namespace ExchangeMail.Core.Tests;

/// <summary>Conversations (FindConversation, GetConversationItems) and streaming notifications.</summary>
public class StreamingConversationTests
{
    private static readonly XNamespace T = FakeEwsServer.T;
    private static readonly XNamespace M = FakeEwsServer.M;

    private const string Conversations = """
        <m:FindConversationResponse ResponseClass="Success"><m:ResponseCode>NoError</m:ResponseCode>
          <m:Conversations>
            <t:Conversation>
              <t:ConversationId Id="C1" />
              <t:ConversationTopic>Bütçe</t:ConversationTopic>
              <t:UniqueRecipients><t:String>Ben</t:String></t:UniqueRecipients>
              <t:UniqueSenders><t:String>Ali Veli</t:String><t:String>Ayşe Kaya</t:String></t:UniqueSenders>
              <t:LastDeliveryTime>2026-10-20T09:00:00Z</t:LastDeliveryTime>
              <t:HasAttachments>true</t:HasAttachments>
              <t:MessageCount>2</t:MessageCount>
              <t:GlobalMessageCount>3</t:GlobalMessageCount>
              <t:UnreadCount>1</t:UnreadCount>
              <t:Importance>High</t:Importance>
              <t:ItemIds><t:ItemId Id="I1" ChangeKey="k1" /><t:ItemId Id="I2" ChangeKey="k2" /></t:ItemIds>
            </t:Conversation>
            <t:Conversation>
              <t:ConversationId Id="C2" />
              <t:ConversationTopic>Tek ileti</t:ConversationTopic>
              <t:UniqueSenders><t:String>Mehmet</t:String></t:UniqueSenders>
              <t:LastDeliveryTime>2026-10-19T09:00:00Z</t:LastDeliveryTime>
              <t:MessageCount>1</t:MessageCount>
              <t:GlobalMessageCount>1</t:GlobalMessageCount>
              <t:UnreadCount>0</t:UnreadCount>
              <t:ItemIds><t:ItemId Id="I3" ChangeKey="k3" /></t:ItemIds>
            </t:Conversation>
          </m:Conversations>
          <m:TotalConversationsInView>2</m:TotalConversationsInView>
          <m:IndexedOffset>2</m:IndexedOffset>
        </m:FindConversationResponse>
        """;

    private const string Thread = """
        <m:GetConversationItemsResponse><m:ResponseMessages>
          <m:GetConversationItemsResponseMessage ResponseClass="Success"><m:ResponseCode>NoError</m:ResponseCode>
            <m:Conversation><t:ConversationId Id="C1" /><t:ConversationNodes>
              <t:ConversationNode><t:Items><t:Message>
                <t:ItemId Id="I1" ChangeKey="k1" /><t:Subject>Bütçe</t:Subject>
                <t:From><t:Mailbox><t:Name>Ali Veli</t:Name><t:EmailAddress>ali@sirket.com</t:EmailAddress></t:Mailbox></t:From>
                <t:DateTimeReceived>2026-10-20T07:00:00Z</t:DateTimeReceived>
                <t:UniqueBody BodyType="HTML">&lt;html&gt;&lt;body&gt;&lt;p&gt;İlk taslak ekte.&lt;/p&gt;&lt;/body&gt;&lt;/html&gt;</t:UniqueBody>
              </t:Message></t:Items></t:ConversationNode>
              <t:ConversationNode><t:Items><t:Message>
                <t:ItemId Id="I2" ChangeKey="k2" /><t:Subject>YNT: Bütçe</t:Subject>
                <t:From><t:Mailbox><t:Name>Ayşe Kaya</t:Name><t:EmailAddress>ayse@sirket.com</t:EmailAddress></t:Mailbox></t:From>
                <t:DateTimeReceived>2026-10-20T08:00:00Z</t:DateTimeReceived>
                <t:UniqueBody BodyType="HTML">&lt;p&gt;Rakamlar &lt;b&gt;uygun&lt;/b&gt;.&lt;/p&gt;</t:UniqueBody>
              </t:Message></t:Items></t:ConversationNode>
              <t:ConversationNode><t:Items><t:Message>
                <t:ItemId Id="S1" ChangeKey="s1" /><t:Subject>YNT: Bütçe</t:Subject>
                <t:From><t:Mailbox><t:Name>Ben</t:Name><t:EmailAddress>ben@sirket.com</t:EmailAddress></t:Mailbox></t:From>
                <t:DateTimeReceived>2026-10-20T09:00:00Z</t:DateTimeReceived>
                <t:UniqueBody BodyType="Text">Teşekkürler &lt;script&gt;</t:UniqueBody>
              </t:Message></t:Items></t:ConversationNode>
            </t:ConversationNodes></m:Conversation>
          </m:GetConversationItemsResponseMessage>
        </m:ResponseMessages></m:GetConversationItemsResponse>
        """;

    private static string Ok(string operation, string inner) =>
        $"""<m:{operation}Response><m:ResponseMessages><m:{operation}ResponseMessage ResponseClass="Success"><m:ResponseCode>NoError</m:ResponseCode>{inner}</m:{operation}ResponseMessage></m:ResponseMessages></m:{operation}Response>""";

    private static string StreamBatch(string notifications, string status) =>
        Ok("GetStreamingEvents", $"<m:Notifications>{notifications}</m:Notifications><m:ConnectionStatus>{status}</m:ConnectionStatus>");

    private static HttpResponseMessage Stream(params string[] bodies) =>
        // Each streamed envelope may carry its own XML declaration.
        FakeEwsServer.Xml(HttpStatusCode.OK, string.Concat(bodies.Select(b => "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" + FakeEwsServer.Envelope(b))));

    // ---- Conversations ----

    [Fact]
    public async Task Find_conversations_sorts_by_activity_and_reads_counts()
    {
        var server = new FakeEwsServer().On("FindConversation", Conversations);
        using var client = server.CreateClient();

        var page = await client.FindConversationsAsync("INBOX", 0, 50, TestContext.Current.CancellationToken);

        var request = server.Request("FindConversation");
        Assert.Equal(["IndexedPageItemView", "SortOrder", "ParentFolderId"], request.Elements().Select(e => e.Name.LocalName));
        Assert.Equal("conversation:LastDeliveryTime", (string?)request.Descendants(T + "FieldURI").Single().Attribute("FieldURI"));

        Assert.True(page.IsLastPage);
        var c = page.Items[0];
        Assert.Equal(("C1", "Bütçe", 2, 3, 1), (c.ConversationId, c.Topic, c.MessageCount, c.GlobalMessageCount, c.UnreadCount));
        Assert.Equal(["Ali Veli", "Ayşe Kaya"], c.Senders);
        Assert.Equal([new EwsItemId("I1", "k1"), new EwsItemId("I2", "k2")], c.ItemIds);
        Assert.Equal(Importance.High, c.Importance);
    }

    [Fact]
    public async Task Conversation_items_follow_schema_order_and_come_newest_first()
    {
        var server = new FakeEwsServer().On("GetConversationItems", Thread);
        using var client = server.CreateClient();

        var thread = await client.GetConversationItemsAsync("C1", TestContext.Current.CancellationToken);

        var request = server.Request("GetConversationItems");
        Assert.Equal(["ItemShape", "FoldersToIgnore", "MaxItemsToReturn", "SortOrder", "Conversations"], request.Elements().Select(e => e.Name.LocalName));
        Assert.Equal(["deleteditems", "drafts"], request.Element(M + "FoldersToIgnore")!.Elements().Select(e => (string?)e.Attribute("Id")));
        Assert.Contains(request.Descendants(T + "FieldURI"), f => (string?)f.Attribute("FieldURI") == "item:UniqueBody");

        Assert.Equal(["S1", "I2", "I1"], thread.Select(m => m.ItemId.Id));
        Assert.False(thread[0].IsHtml);
        Assert.Equal("<p>Rakamlar <b>uygun</b>.</p>", thread[1].Body);
    }

    [Fact]
    public async Task Conversation_view_lists_threads_and_opens_the_whole_conversation()
    {
        var server = new FakeEwsServer()
            .On("GetFolder", WellKnownFolders)
            .On("FindFolder", InboxOnly)
            .On("FindConversation", Conversations)
            .On("UpdateItem", Ok("UpdateItem", """<m:Items><t:Message><t:ItemId Id="I1" ChangeKey="k9" /></t:Message></m:Items>""") )
            .On("GetConversationItems", Thread)
            .On("GetItem", Ok("GetItem", """
                <m:Items><t:Message><t:ItemId Id="I2" ChangeKey="k2" /><t:Subject>YNT: Bütçe</t:Subject>
                  <t:Body BodyType="HTML">&lt;p&gt;tam metin&lt;/p&gt;</t:Body><t:IsRead>true</t:IsRead></t:Message></m:Items>
                """));
        var session = new MailSession(server.CreateClient(), new AccountSettings { Email = "ben@sirket.com" });
        session.Preferences.ConversationView = true;
        var vm = new MainViewModel(session);

        await vm.InitializeAsync();
        await WaitUntil(() => vm.Messages.Count == 2 && !vm.IsLoadingList);
        Assert.Equal("Ali Veli, Ayşe Kaya", vm.Messages[0].Sender);
        Assert.Equal("3", vm.Messages[0].CountText);
        Assert.False(vm.Messages[1].HasCount);
        Assert.Equal(1, vm.Messages[0].UnreadItems);

        vm.SetSelection([vm.Messages[0]]);
        await WaitUntil(() => vm.Current is not null);

        // Marking read covers the conversation's items in this folder.
        Assert.Equal(["I1", "I2"], server.Request("UpdateItem").Descendants(T + "ItemId").Select(i => (string?)i.Attribute("Id")));
        Assert.True(vm.Messages[0].IsRead);
        // Replies go to the newest message from someone else, not to our own reply.
        Assert.Equal("I2", (string?)server.Request("GetItem").Descendants(T + "ItemId").Single().Attribute("Id"));
        Assert.True(vm.Current!.IsThread);
        Assert.Equal("Konuşmada 3 ileti", vm.Current.ThreadText);
        var html = vm.Current.DisplayHtml;
        Assert.Contains("İlk taslak ekte.", html);
        Assert.Contains("Rakamlar <b>uygun</b>", html);
        Assert.Contains("&lt;script&gt;", html); // a text body is encoded
        Assert.DoesNotContain("<script>", html);
        Assert.True(html.IndexOf("&lt;script&gt;", StringComparison.Ordinal) < html.IndexOf("İlk taslak", StringComparison.Ordinal)); // newest first
        // Every request is routed to the signed-in mailbox.
        Assert.All(server.AnchorMailboxes, a => Assert.Equal("ben@sirket.com", a));
    }

    // ---- Streaming ----

    [Fact]
    public async Task Subscribes_to_all_folders()
    {
        var server = new FakeEwsServer().On("Subscribe", Ok("Subscribe", "<m:SubscriptionId>SUB1</m:SubscriptionId><m:Watermark>W</m:Watermark>"));
        using var client = server.CreateClient();

        Assert.Equal("SUB1", await client.SubscribeToStreamingAsync(TestContext.Current.CancellationToken));

        var request = server.Request("Subscribe").Element(M + "StreamingSubscriptionRequest")!;
        Assert.Equal("true", (string?)request.Attribute("SubscribeToAllFolders"));
        Assert.Null(request.Element(T + "FolderIds"));
        Assert.Contains("NewMailEvent", request.Descendants(T + "EventType").Select(e => e.Value));
    }

    [Fact]
    public async Task Streamed_envelopes_are_reported_until_the_server_closes()
    {
        var server = new FakeEwsServer().OnRaw("GetStreamingEvents", () => Stream(
            StreamBatch("""
                <m:Notification><t:SubscriptionId>SUB1</t:SubscriptionId>
                  <t:NewMailEvent><t:TimeStamp>2026-10-20T09:00:00Z</t:TimeStamp><t:ItemId Id="N1" /><t:ParentFolderId Id="INBOX" /></t:NewMailEvent>
                </m:Notification>
                """, "OK"),
            StreamBatch("", "OK"), // heartbeat
            StreamBatch("""
                <m:Notification><t:SubscriptionId>SUB1</t:SubscriptionId>
                  <t:MovedEvent><t:TimeStamp>2026-10-20T09:01:00Z</t:TimeStamp><t:ItemId Id="N2" /><t:ParentFolderId Id="ARCHIVE" /><t:OldItemId Id="N1" /><t:OldParentFolderId Id="INBOX" /></t:MovedEvent>
                </m:Notification>
                """, "Closed")));
        using var client = server.CreateClient();
        var batches = new List<IReadOnlyList<MailboxEvent>>();

        await client.ReceiveStreamingEventsAsync("SUB1", 10, batches.Add, TestContext.Current.CancellationToken);

        var request = server.Request("GetStreamingEvents");
        Assert.Equal("SUB1", request.Descendants(T + "SubscriptionId").Single().Value);
        Assert.Equal("10", request.Element(M + "ConnectionTimeout")!.Value);
        Assert.Equal(2, batches.Count);
        Assert.Equal(new MailboxEvent(MailboxEventKind.NewMail, null, "INBOX", null), batches[0].Single());
        Assert.Equal(new MailboxEvent(MailboxEventKind.Moved, null, "ARCHIVE", "INBOX"), batches[1].Single());
    }

    [Fact]
    public async Task A_lost_subscription_is_reported()
    {
        var server = new FakeEwsServer().OnRaw("GetStreamingEvents", () => Stream(
            """<m:GetStreamingEventsResponse><m:ResponseMessages><m:GetStreamingEventsResponseMessage ResponseClass="Error"><m:MessageText>gone</m:MessageText><m:ResponseCode>ErrorSubscriptionNotFound</m:ResponseCode></m:GetStreamingEventsResponseMessage></m:ResponseMessages></m:GetStreamingEventsResponse>"""));
        using var client = server.CreateClient();

        var ex = await Assert.ThrowsAsync<EwsException>(() =>
            client.ReceiveStreamingEventsAsync("SUB1", 10, _ => { }, TestContext.Current.CancellationToken));
        Assert.Equal("ErrorSubscriptionNotFound", ex.ResponseCode);
    }

    [Fact]
    public void Envelopes_are_taken_only_when_complete()
    {
        var buffer = new StringBuilder("<s:Envelope><s:Body>");
        Assert.Null(EwsClient.TakeEnvelope(buffer));
        buffer.Append("</s:Body></s:Envelope><s:Envelope>");
        Assert.Equal("<s:Envelope><s:Body></s:Body></s:Envelope>", EwsClient.TakeEnvelope(buffer));
        Assert.Equal("<s:Envelope>", buffer.ToString());
    }

    [Fact]
    public async Task Watcher_reports_events_and_refreshes_after_an_outage()
    {
        var server = new FakeEwsServer()
            .On("Subscribe", """<m:SubscribeResponse><m:ResponseMessages><m:SubscribeResponseMessage ResponseClass="Error"><m:MessageText>busy</m:MessageText><m:ResponseCode>ErrorInternalServerError</m:ResponseCode></m:SubscribeResponseMessage></m:ResponseMessages></m:SubscribeResponse>""")
            .On("Subscribe", Ok("Subscribe", "<m:SubscriptionId>SUB2</m:SubscriptionId>"))
            .OnRaw("GetStreamingEvents", () => Stream(StreamBatch("""
                <m:Notification><t:SubscriptionId>SUB2</t:SubscriptionId>
                  <t:ModifiedEvent><t:TimeStamp>2026-10-20T09:00:00Z</t:TimeStamp><t:ItemId Id="N1" /><t:ParentFolderId Id="INBOX" /></t:ModifiedEvent>
                </m:Notification>
                """, "Closed")));
        using var client = server.CreateClient();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var watcher = new MailboxWatcher(client) { FirstRetryDelay = TimeSpan.FromMilliseconds(10) };
        var batches = new List<IReadOnlyList<MailboxEvent>>();
        watcher.Changed += (_, events) =>
        {
            batches.Add(events);
            if (events.Count > 0) cts.Cancel();
        };

        await watcher.RunAsync(cts.Token);

        // First an empty batch (changes may have been missed while the subscription failed), then the real event.
        Assert.Equal(2, batches.Count);
        Assert.Empty(batches[0]);
        Assert.Equal(MailboxEventKind.Modified, batches[1].Single().Kind);
        Assert.False(watcher.IsConnected);
    }

    [Fact]
    public async Task Only_changes_in_mail_folders_trigger_a_refresh()
    {
        var server = new FakeEwsServer().On("GetFolder", WellKnownFolders).On("FindFolder", InboxOnly).On("FindItem", NoItems);
        var vm = new MainViewModel(new MailSession(server.CreateClient(), new AccountSettings { Email = "ben@sirket.com" }));
        await vm.InitializeAsync();

        Assert.True(vm.ConcernsMail([]));
        Assert.True(vm.ConcernsMail([new MailboxEvent(MailboxEventKind.Modified, null, "INBOX", null)]));
        Assert.True(vm.ConcernsMail([new MailboxEvent(MailboxEventKind.NewMail, null, "ELSEWHERE", null)]));
        Assert.False(vm.ConcernsMail([new MailboxEvent(MailboxEventKind.Modified, null, "CALENDAR", null)]));
    }

    private const string WellKnownFolders = """
        <m:GetFolderResponse><m:ResponseMessages>
          <m:GetFolderResponseMessage ResponseClass="Success"><m:ResponseCode>NoError</m:ResponseCode><m:Folders><t:Folder><t:FolderId Id="ROOT" /></t:Folder></m:Folders></m:GetFolderResponseMessage>
          <m:GetFolderResponseMessage ResponseClass="Success"><m:ResponseCode>NoError</m:ResponseCode><m:Folders><t:Folder><t:FolderId Id="INBOX" /></t:Folder></m:Folders></m:GetFolderResponseMessage>
        </m:ResponseMessages></m:GetFolderResponse>
        """;

    private const string InboxOnly = """
        <m:FindFolderResponse><m:ResponseMessages><m:FindFolderResponseMessage ResponseClass="Success"><m:ResponseCode>NoError</m:ResponseCode>
          <m:RootFolder><t:Folders>
            <t:Folder><t:FolderId Id="INBOX" /><t:ParentFolderId Id="ROOT" /><t:FolderClass>IPF.Note</t:FolderClass><t:DisplayName>Gelen Kutusu</t:DisplayName><t:TotalCount>2</t:TotalCount><t:UnreadCount>1</t:UnreadCount></t:Folder>
          </t:Folders></m:RootFolder>
        </m:FindFolderResponseMessage></m:ResponseMessages></m:FindFolderResponse>
        """;

    private const string NoItems = """
        <m:FindItemResponse><m:ResponseMessages><m:FindItemResponseMessage ResponseClass="Success"><m:ResponseCode>NoError</m:ResponseCode>
          <m:RootFolder IndexedPagingOffset="0" TotalItemsInView="0" IncludesLastItemInRange="true"><t:Items /></m:RootFolder>
        </m:FindItemResponseMessage></m:ResponseMessages></m:FindItemResponse>
        """;

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++) await Task.Delay(20);
        Assert.True(condition());
    }
}
