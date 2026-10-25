using ExchangeMail.Core.Models;
using ExchangeMail.ViewModels;

namespace ExchangeMail.Core.Tests;

public class MainViewModelTests
{
    private static readonly System.Xml.Linq.XNamespace Tns = FakeEwsServer.T;

    private const string WellKnown = """
        <m:GetFolderResponse><m:ResponseMessages>
          <m:GetFolderResponseMessage ResponseClass="Success"><m:ResponseCode>NoError</m:ResponseCode><m:Folders><t:Folder><t:FolderId Id="ROOT" /></t:Folder></m:Folders></m:GetFolderResponseMessage>
          <m:GetFolderResponseMessage ResponseClass="Success"><m:ResponseCode>NoError</m:ResponseCode><m:Folders><t:Folder><t:FolderId Id="INBOX" /></t:Folder></m:Folders></m:GetFolderResponseMessage>
        </m:ResponseMessages></m:GetFolderResponse>
        """;

    private const string Folders = """
        <m:FindFolderResponse><m:ResponseMessages><m:FindFolderResponseMessage ResponseClass="Success"><m:ResponseCode>NoError</m:ResponseCode>
          <m:RootFolder><t:Folders>
            <t:Folder><t:FolderId Id="INBOX" /><t:ParentFolderId Id="ROOT" /><t:FolderClass>IPF.Note</t:FolderClass><t:DisplayName>Gelen Kutusu</t:DisplayName><t:TotalCount>2</t:TotalCount><t:UnreadCount>1</t:UnreadCount></t:Folder>
          </t:Folders></m:RootFolder>
        </m:FindFolderResponseMessage></m:ResponseMessages></m:FindFolderResponse>
        """;

    private static string Items(params (string Id, bool Read)[] items) =>
        $"""
        <m:FindItemResponse><m:ResponseMessages><m:FindItemResponseMessage ResponseClass="Success"><m:ResponseCode>NoError</m:ResponseCode>
          <m:RootFolder IndexedPagingOffset="{items.Length}" TotalItemsInView="{items.Length}" IncludesLastItemInRange="true"><t:Items>
            {string.Concat(items.Select(i => $"<t:Message><t:ItemId Id=\"{i.Id}\" ChangeKey=\"ck\" /><t:Subject>{i.Id}</t:Subject><t:DateTimeReceived>2026-10-20T08:00:00Z</t:DateTimeReceived><t:IsRead>{(i.Read ? "true" : "false")}</t:IsRead></t:Message>"))}
          </t:Items></m:RootFolder>
        </m:FindItemResponseMessage></m:ResponseMessages></m:FindItemResponse>
        """;

    private const string Counts = """
        <m:GetFolderResponse><m:ResponseMessages>
          <m:GetFolderResponseMessage ResponseClass="Success"><m:ResponseCode>NoError</m:ResponseCode><m:Folders><t:Folder><t:FolderId Id="INBOX" /><t:TotalCount>3</t:TotalCount><t:UnreadCount>2</t:UnreadCount></t:Folder></m:Folders></m:GetFolderResponseMessage>
        </m:ResponseMessages></m:GetFolderResponse>
        """;

    [Fact]
    public async Task Background_refresh_prepends_new_mail_and_syncs_read_state()
    {
        var server = new FakeEwsServer()
            .On("GetFolder", WellKnown)
            .On("FindFolder", Folders)
            .On("FindItem", Items(("B", false), ("A", true)))
            .On("GetFolder", Counts)
            .On("FindItem", Items(("C", false), ("B", true), ("A", true)));
        var vm = new MainViewModel(new MailSession(server.CreateClient(), new AccountSettings { Email = "ben@sirket.com" }));

        await vm.InitializeAsync();
        await WaitUntil(() => vm.Messages.Count == 2 && !vm.IsLoadingList);

        Assert.Equal(WellKnownFolder.Inbox, vm.SelectedFolder!.WellKnown);
        Assert.False(vm.IsListEmpty);
        IReadOnlyList<MailSummary>? notified = null;
        vm.NewMailArrived += (_, items) => notified = items;

        var added = await vm.BackgroundRefreshAsync();

        Assert.Equal(1, added);
        Assert.Equal(["C", "B", "A"], vm.Messages.Select(m => m.Id.Id));
        Assert.True(vm.Messages[1].IsRead); // B was read in another client
        Assert.Equal(2, vm.SelectedFolder.UnreadCount);
        Assert.Equal(2, vm.InboxUnreadCount);
        Assert.Equal("C", Assert.Single(notified!).ItemId.Id); // B and A were already in the inbox
    }

    private static string Detail(string id) => $"""
        <m:GetItemResponse><m:ResponseMessages><m:GetItemResponseMessage ResponseClass="Success"><m:ResponseCode>NoError</m:ResponseCode>
          <m:Items><t:Message><t:ItemId Id="{id}" ChangeKey="ck2" /><t:Subject>{id}</t:Subject><t:Body BodyType="HTML">&lt;p&gt;x&lt;/p&gt;</t:Body><t:IsRead>true</t:IsRead></t:Message></m:Items>
        </m:GetItemResponseMessage></m:ResponseMessages></m:GetItemResponse>
        """;

    private const string MarkedRead = """
        <m:UpdateItemResponse><m:ResponseMessages><m:UpdateItemResponseMessage ResponseClass="Success"><m:ResponseCode>NoError</m:ResponseCode>
          <m:Items><t:Message><t:ItemId Id="B" ChangeKey="ck3" /></t:Message></m:Items>
        </m:UpdateItemResponseMessage></m:ResponseMessages></m:UpdateItemResponse>
        """;

    private static async Task<(MainViewModel Vm, FakeEwsServer Server)> OpenInboxAsync(MarkAsReadMode mode)
    {
        var server = new FakeEwsServer()
            .On("GetFolder", WellKnown)
            .On("FindFolder", Folders)
            .On("FindItem", Items(("B", false), ("A", true)));
        var preferences = new UserPreferences { MarkAsRead = mode, MarkAsReadDelaySeconds = 1 };
        var vm = new MainViewModel(new MailSession(server.CreateClient(), new AccountSettings { Email = "ben@sirket.com" }, preferences));
        await vm.InitializeAsync();
        await WaitUntil(() => vm.Messages.Count == 2 && !vm.IsLoadingList);
        return (vm, server);
    }

    [Fact]
    public async Task Started_from_a_notification_the_message_opens_once_the_inbox_is_loaded()
    {
        var server = new FakeEwsServer()
            .On("GetFolder", WellKnown)
            .On("FindFolder", Folders)
            .On("FindItem", Items(("B", true), ("A", true)))
            .On("GetItem", Detail("A"));
        var vm = new MainViewModel(new MailSession(server.CreateClient(), new AccountSettings { Email = "ben@sirket.com" }))
        {
            OpenWhenLoaded = "A",
        };

        await vm.InitializeAsync();

        Assert.Equal("A", vm.SelectedMessage?.Id.Id);
        Assert.Null(vm.OpenWhenLoaded); // only once
        await WaitUntil(() => vm.Current is not null);
    }

    [Fact]
    public async Task A_poll_that_finds_the_list_loading_runs_again_once_it_has_loaded()
    {
        var loaded = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = new FakeEwsServer()
            .On("GetFolder", WellKnown)
            .On("FindFolder", Folders)
            .OnAsync("FindItem", () => loaded.Task) // the list is still loading while the poll runs
            .On("GetFolder", Counts)
            .On("FindItem", Items(("A", true))) // the poll's inbox check
            .On("GetFolder", Counts)
            .On("FindItem", Items(("C", false), ("A", true)));
        var vm = new MainViewModel(new MailSession(server.CreateClient(), new AccountSettings { Email = "ben@sirket.com" }));
        await vm.InitializeAsync();
        Assert.True(vm.IsLoadingList);

        Assert.Equal(0, await vm.BackgroundRefreshAsync());
        loaded.SetResult(FakeEwsServer.Xml(System.Net.HttpStatusCode.OK, FakeEwsServer.Envelope(Items(("A", true)))));

        await WaitUntil(() => vm.Messages.Count == 2);
        Assert.Equal("C", vm.Messages[0].Id.Id);
    }

    [Fact]
    public async Task Selecting_unread_mail_marks_it_read_when_mode_is_WhenSelected()
    {
        var (vm, server) = await OpenInboxAsync(MarkAsReadMode.WhenSelected);
        server.On("UpdateItem", MarkedRead).On("GetItem", Detail("B"));

        vm.SelectedMessage = vm.Messages[0];
        await WaitUntil(() => vm.Current is not null);

        Assert.True(vm.Messages[0].IsRead);
        Assert.Equal(0, vm.SelectedFolder!.UnreadCount);
        Assert.Equal(["UpdateItem", "GetItem"], server.Operations.Skip(3));
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task Selecting_unread_mail_leaves_it_unread_when_mode_is_Never()
    {
        var (vm, server) = await OpenInboxAsync(MarkAsReadMode.Never);
        server.On("GetItem", Detail("B"));

        vm.SelectedMessage = vm.Messages[0];
        await WaitUntil(() => vm.Current is not null);

        Assert.False(vm.Messages[0].IsRead);
        Assert.DoesNotContain("UpdateItem", server.Operations);
    }

    [Fact]
    public async Task Delayed_mode_marks_read_only_after_the_delay()
    {
        var (vm, server) = await OpenInboxAsync(MarkAsReadMode.AfterDelay);
        server.On("GetItem", Detail("B")).On("UpdateItem", MarkedRead);

        vm.SelectedMessage = vm.Messages[0];
        await WaitUntil(() => vm.Current is not null);
        Assert.False(vm.Messages[0].IsRead);

        await WaitUntil(() => vm.Messages[0].IsRead);
        Assert.Equal("UpdateItem", server.Operations.Last());
    }

    [Fact]
    public async Task Toggle_read_button_marks_unread_and_reports_no_error()
    {
        var (vm, server) = await OpenInboxAsync(MarkAsReadMode.Never);
        server.On("GetItem", Detail("A")).On("UpdateItem", MarkedRead);
        vm.SelectedMessage = vm.Messages[1];
        await WaitUntil(() => vm.Current is not null);

        await vm.ToggleReadCommand.ExecuteAsync(null);

        Assert.False(vm.Messages[1].IsRead);
        Assert.Equal(2, vm.SelectedFolder!.UnreadCount);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task Send_receive_checks_for_new_mail_and_reports_failures()
    {
        var server = new FakeEwsServer()
            .On("GetFolder", WellKnown)
            .On("FindFolder", Folders)
            .On("FindItem", Items(("A", true)))
            .On("GetFolder", Counts)
            .On("FindItem", Items(("B", false), ("A", true)))
            .OnRaw("GetFolder", () => new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable));
        var vm = new MainViewModel(new MailSession(server.CreateClient(), new AccountSettings { Email = "ben@sirket.com" }));
        await vm.InitializeAsync();
        await WaitUntil(() => vm.Messages.Count == 1 && !vm.IsLoadingList);

        await vm.SendReceiveCommand.ExecuteAsync(null);

        Assert.Equal(["B", "A"], vm.Messages.Select(m => m.Id.Id));
        Assert.Equal(2, vm.SelectedFolder!.UnreadCount);
        Assert.Null(vm.ErrorMessage);

        // Unlike the silent background poll, a manual check says why it failed.
        await vm.SendReceiveCommand.ExecuteAsync(null);
        Assert.StartsWith(Loc.Format(Strings.Mail_SendReceiveFailed, ""), vm.ErrorMessage);
    }

    private static string Ok(string operation, string inner = "") =>
        $"""<m:{operation}Response><m:ResponseMessages><m:{operation}ResponseMessage ResponseClass="Success"><m:ResponseCode>NoError</m:ResponseCode>{inner}</m:{operation}ResponseMessage></m:ResponseMessages></m:{operation}Response>""";

    private static async Task<(FakeEwsServer Server, MainViewModel Vm)> LoadThreeAsync(params (string Operation, string Body)[] then)
    {
        var server = new FakeEwsServer()
            .On("GetFolder", WellKnown)
            .On("FindFolder", Folders)
            .On("FindItem", Items(("C", false), ("B", false), ("A", true)));
        foreach (var (operation, body) in then) server.On(operation, body);
        var vm = new MainViewModel(new MailSession(server.CreateClient(), new AccountSettings { Email = "ben@sirket.com" }));
        await vm.InitializeAsync();
        await WaitUntil(() => vm.Messages.Count == 3 && !vm.IsLoadingList);
        return (server, vm);
    }

    [Fact]
    public async Task Several_selected_messages_are_deleted_with_one_request()
    {
        var (server, vm) = await LoadThreeAsync(("DeleteItem", Ok("DeleteItem")));

        vm.SetSelection([vm.Messages[0], vm.Messages[1]]);
        Assert.Null(vm.SelectedMessage);
        Assert.Null(vm.Current);
        Assert.Equal("2 öğe seçildi", vm.PlaceholderText);
        Assert.True(vm.DeleteCommand.CanExecute(null));

        await vm.DeleteCommand.ExecuteAsync(null);

        Assert.Equal(["C", "B"], server.Request("DeleteItem").Descendants(Tns + "ItemId").Select(i => (string?)i.Attribute("Id")));
        Assert.Equal(["A"], vm.Messages.Select(m => m.Id.Id));
        Assert.Equal(0, vm.SelectedFolder!.UnreadCount); // both deleted messages were unread
        Assert.Same(vm.Messages[0], vm.SelectedMessage); // the next row takes the first removed row's place
        Assert.Equal([vm.Messages[0]], vm.SelectedItems);
    }

    [Fact]
    public async Task Dropped_messages_move_to_the_folder()
    {
        var (server, vm) = await LoadThreeAsync(("MoveItem", Ok("MoveItem")));
        var archive = new FolderViewModel(new MailFolder { Id = "ARCHIVE", DisplayName = "Arşiv" }, 0);

        await vm.MoveAsync([vm.Messages[1], vm.Messages[2]], archive);

        var request = server.Request("MoveItem");
        Assert.Equal("ARCHIVE", (string?)request.Descendants(Tns + "FolderId").Single().Attribute("Id"));
        Assert.Equal(["B", "A"], request.Descendants(Tns + "ItemId").Select(i => (string?)i.Attribute("Id")));
        Assert.Equal(["C"], vm.Messages.Select(m => m.Id.Id));
        Assert.Equal(1, archive.UnreadCount);
        Assert.Equal(2, archive.TotalCount);
    }

    [Fact]
    public async Task Toggling_a_mixed_selection_marks_everything_read()
    {
        var (server, vm) = await LoadThreeAsync(("UpdateItem", Ok("UpdateItem",
            """<m:Items><t:Message><t:ItemId Id="B" ChangeKey="b2" /></t:Message></m:Items></m:UpdateItemResponseMessage><m:UpdateItemResponseMessage ResponseClass="Success"><m:ResponseCode>NoError</m:ResponseCode><m:Items><t:Message><t:ItemId Id="A" ChangeKey="a2" /></t:Message></m:Items>""")));

        vm.SetSelection([vm.Messages[1], vm.Messages[2]]);
        await vm.ToggleReadCommand.ExecuteAsync(null);

        Assert.Equal(2, server.Request("UpdateItem").Descendants(Tns + "ItemChange").Count());
        Assert.True(vm.Messages[1].IsRead);
        Assert.True(vm.Messages[2].IsRead);
        Assert.Equal("b2", vm.Messages[1].Id.ChangeKey);
        Assert.Equal(0, vm.SelectedFolder!.UnreadCount);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++) await Task.Delay(20);
        Assert.True(condition());
    }
}
