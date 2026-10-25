using System.Xml.Linq;
using ExchangeMail.Core.Models;
using static ExchangeMail.Core.Ews.Xml;

namespace ExchangeMail.Core.Ews;

public sealed partial class EwsClient
{
    /// <summary>Lists the conversations that have messages in a folder, most recently active first.</summary>
    public async Task<ConversationPage> FindConversationsAsync(string folderId, int offset = 0, int pageSize = 50, CancellationToken ct = default)
    {
        var request = M("FindConversation",
            M("IndexedPageItemView",
                new XAttribute("MaxEntriesReturned", pageSize),
                new XAttribute("Offset", offset),
                new XAttribute("BasePoint", "Beginning")),
            M("SortOrder", T("FieldOrder", new XAttribute("Order", "Descending"), Field("conversation:LastDeliveryTime"))),
            M("ParentFolderId", FolderIdElement(folderId)));

        // Unlike most operations, FindConversation answers with a single response message, not a list.
        var response = Ok(await ExecuteAsync(request, ct));
        var items = response.Element(MNs + "Conversations")?.Elements(TNs + "Conversation").Select(ParseConversation).ToList() ?? [];
        var total = (int?)response.Element(MNs + "TotalConversationsInView") ?? offset + items.Count;
        var next = (int?)response.Element(MNs + "IndexedOffset") ?? offset + items.Count;
        return new ConversationPage(items, total, next, next >= total || items.Count < pageSize);
    }

    /// <summary>
    /// Returns the messages of a conversation from every folder except Deleted Items and Drafts, newest first.
    /// Each message carries only the text it added (UniqueBody), so the thread reads without repeated quotes.
    /// </summary>
    public async Task<IReadOnlyList<ThreadMessage>> GetConversationItemsAsync(string conversationId, CancellationToken ct = default)
    {
        // Schema order: ItemShape, FoldersToIgnore, MaxItemsToReturn, SortOrder, MailboxScope, Conversations.
        var request = M("GetConversationItems",
            M("ItemShape",
                T("BaseShape", "IdOnly"),
                T("BodyType", "HTML"),
                T("FilterHtmlContent", "true"),
                T("AdditionalProperties",
                    Field("item:Subject"),
                    Field("message:From"),
                    Field("message:ToRecipients"),
                    Field("item:DateTimeReceived"),
                    Field("item:UniqueBody"),
                    Field("item:Preview"),
                    Field("item:ItemClass"),
                    Field("message:IsRead"))),
            M("FoldersToIgnore", DistinguishedFolder("deleteditems"), DistinguishedFolder("drafts")),
            M("MaxItemsToReturn", 100),
            M("SortOrder", "DateOrderDescending"),
            M("Conversations", T("Conversation", T("ConversationId", new XAttribute("Id", conversationId)))));

        var message = Single(await ExecuteAsync(request, ct));
        return message.Descendants(TNs + "ConversationNode")
            .SelectMany(node => node.Element(TNs + "Items")?.Elements() ?? [])
            .Select(ParseThreadMessage)
            .OrderByDescending(m => m.Received)
            .ToList();
    }

    private static ConversationSummary ParseConversation(XElement c) => new(
        (string?)c.Element(TNs + "ConversationId")?.Attribute("Id") ?? "",
        (string?)c.Element(TNs + "ConversationTopic") ?? "",
        StringList(c.Element(TNs + "UniqueSenders")),
        StringList(c.Element(TNs + "UniqueRecipients")),
        (DateTimeOffset?)c.Element(TNs + "LastDeliveryTime") ?? DateTimeOffset.MinValue,
        (int?)c.Element(TNs + "MessageCount") ?? 1,
        (int?)c.Element(TNs + "GlobalMessageCount") ?? (int?)c.Element(TNs + "MessageCount") ?? 1,
        (int?)c.Element(TNs + "UnreadCount") ?? 0,
        (bool?)c.Element(TNs + "HasAttachments") ?? false,
        ParseImportance(c.Element(TNs + "Importance")),
        c.Element(TNs + "ItemIds")?.Elements(TNs + "ItemId").Select(ParseItemId).ToList() ?? []);

    private static IReadOnlyList<string> StringList(XElement? list) =>
        list?.Elements(TNs + "String").Select(s => s.Value).Where(s => s.Length > 0).ToList() ?? [];

    private static ThreadMessage ParseThreadMessage(XElement item)
    {
        var body = item.Element(TNs + "UniqueBody");
        return new ThreadMessage(
            ParseItemId(item.Element(TNs + "ItemId")),
            (string?)item.Element(TNs + "Subject") ?? "",
            ParseMailbox(item.Element(TNs + "From")?.Element(TNs + "Mailbox")),
            ParseMailboxList(item.Element(TNs + "ToRecipients")),
            (DateTimeOffset?)item.Element(TNs + "DateTimeReceived"),
            body?.Value ?? "",
            !string.Equals((string?)body?.Attribute("BodyType"), "Text", StringComparison.OrdinalIgnoreCase),
            ((string?)item.Element(TNs + "Preview") ?? "").Trim(),
            (string?)item.Element(TNs + "ItemClass") ?? "IPM.Note",
            (bool?)item.Element(TNs + "IsRead") ?? true);
    }

    /// <summary>
    /// List-row details (sender, date, preview, read state) of many items in one request, e.g. the newest message of
    /// each conversation on a page. Items that no longer exist are skipped.
    /// </summary>
    public async Task<IReadOnlyList<MailSummary>> GetItemSummariesAsync(IReadOnlyCollection<EwsItemId> ids, CancellationToken ct = default)
    {
        var result = new List<MailSummary>();
        foreach (var chunk in ids.Chunk(200))
        {
            var request = M("GetItem",
                M("ItemShape", T("BaseShape", "IdOnly"), T("AdditionalProperties", SummaryFields())),
                M("ItemIds", chunk.Select(id => ItemIdElement(id with { ChangeKey = null }))));
            foreach (var message in ResponseMessages(await ExecuteAsync(request, ct)))
            {
                if ((string?)message.Attribute("ResponseClass") == "Error") continue; // deleted meanwhile
                if (message.Element(MNs + "Items")?.Elements().FirstOrDefault() is { } item) result.Add(ParseSummary(item));
            }
        }
        return result;
    }
}
