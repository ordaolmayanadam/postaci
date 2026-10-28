using ExchangeMail.Core.Ews;
using ExchangeMail.Core.Models;
using ExchangeMail.Demo;

namespace ExchangeMail.Core.Tests;

/// <summary>The demo mailbox answers the real client the way Exchange would.</summary>
public class DemoMailboxTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 10, 0, 0, TimeSpan.FromHours(3)); // a Wednesday

    private static EwsClient Client(string language = "tr") => new(DemoEwsHandler.EwsUrl, new DemoEwsHandler(new DemoMailbox(language, Now)));

    [Fact]
    public async Task Folder_tree_has_the_well_known_folders_first()
    {
        using var client = Client();
        var tree = await client.GetFolderTreeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(WellKnownFolder.Inbox, tree[0].WellKnown);
        Assert.Equal("Gelen Kutusu", tree[0].DisplayName);
        Assert.True(tree[0].UnreadCount > 0);
        Assert.Contains(tree, f => f.Children.Count > 0);
    }

    [Fact]
    public async Task Conversations_bring_replies_from_other_folders()
    {
        using var client = Client("en");
        var page = await client.FindConversationsAsync("inbox", ct: TestContext.Current.CancellationToken);
        var agenda = page.Items.First();

        Assert.Equal("Weekly operations meeting agenda", agenda.Topic);
        var thread = await client.GetConversationItemsAsync(agenda.ConversationId, "inbox", TestContext.Current.CancellationToken);
        Assert.Equal(3, thread.Count);
        Assert.Equal(new Dictionary<string, int> { [agenda.ConversationId] = 3 },
            await client.CountConversationMessagesAsync([agenda.ConversationId], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Messages_can_be_read_and_marked()
    {
        using var client = Client();
        var page = await client.FindItemsAsync("inbox", ct: TestContext.Current.CancellationToken);
        var unread = page.Items.First(m => !m.IsRead);

        var detail = await client.GetItemAsync(unread.ItemId, TestContext.Current.CancellationToken);
        Assert.Contains("<p>", detail.Body);
        await client.SetReadStateAsync([unread.ItemId], true, TestContext.Current.CancellationToken);

        var after = await client.FindItemsAsync("inbox", ct: TestContext.Current.CancellationToken);
        Assert.True(after.Items.Single(m => m.ItemId.Id == unread.ItemId.Id).IsRead);
    }

    [Fact]
    public async Task Calendar_week_contact_list_and_directory_are_filled()
    {
        using var client = Client();
        var ct = TestContext.Current.CancellationToken;
        var monday = new DateTimeOffset(2026, 10, 26, 0, 0, 0, Now.Offset);

        var events = await client.FindAppointmentsAsync(monday, monday.AddDays(7), ct);
        Assert.True(events.Count > 10);
        Assert.Contains(events, e => e.IsAllDay);
        var detail = await client.GetAppointmentAsync(events.First(e => e.IsMeeting).ItemId, ct);
        Assert.NotEmpty(detail.RequiredAttendees);

        var contacts = await client.FindContactsAsync(ct: ct);
        Assert.True(contacts.Items.Count >= 8);

        var person = await client.GetDirectoryPersonAsync("burak.sahin@kuzeylojistik.example", ct);
        Assert.Equal("Depo Sorumlusu", person?.JobTitle);
        Assert.Equal("Elif Yıldız", person?.ManagerName);
        Assert.NotEmpty(await client.ResolveNamesAsync("Elif", ct));

        var availability = await client.GetAvailabilityAsync([person!.Email!], monday, monday.AddDays(5), ct);
        Assert.Null(availability[0].Error);
        Assert.Equal(AutoReplyState.Disabled, (await client.GetAutoReplyAsync("deniz.kaya@kuzeylojistik.example", ct)).State);
    }
}
