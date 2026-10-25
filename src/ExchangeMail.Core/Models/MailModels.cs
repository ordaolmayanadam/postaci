namespace ExchangeMail.Core.Models;

public sealed record EwsItemId(string Id, string? ChangeKey);

public sealed record MailboxAddress(string? Name, string Address)
{
    public string Display => string.IsNullOrWhiteSpace(Name) ? Address : Name!;

    public override string ToString() =>
        string.IsNullOrWhiteSpace(Name) || Name == Address ? Address : $"{Name} <{Address}>";
}

public enum Importance { Low, Normal, High }

public enum WellKnownFolder { None, Inbox, Drafts, SentItems, DeletedItems, JunkEmail, Outbox }

public enum DeleteMode { MoveToDeletedItems, SoftDelete, HardDelete }

public enum ComposeMode { New, Reply, ReplyAll, Forward }

public sealed class MailFolder
{
    public required string Id { get; init; }
    public string? ParentId { get; init; }
    public required string DisplayName { get; init; }
    public int TotalCount { get; set; }
    public int UnreadCount { get; set; }
    public WellKnownFolder WellKnown { get; set; }
    public List<MailFolder> Children { get; } = [];
}

public sealed record MailSummary(
    EwsItemId ItemId,
    string Subject,
    MailboxAddress? From,
    string DisplayTo,
    DateTimeOffset Received,
    bool IsRead,
    bool HasAttachments,
    Importance Importance,
    string Preview,
    string ItemClass);

public sealed record MailPage(IReadOnlyList<MailSummary> Items, int TotalCount, int NextOffset, bool IsLastPage);

public sealed record AttachmentInfo(
    string Id,
    string Name,
    string? ContentType,
    string? ContentId,
    long Size,
    bool IsInline,
    bool IsItemAttachment);

public sealed record AttachmentContent(string FileName, string? ContentType, byte[] Content);

public sealed record MailDetail(
    EwsItemId ItemId,
    string Subject,
    MailboxAddress? From,
    IReadOnlyList<MailboxAddress> To,
    IReadOnlyList<MailboxAddress> Cc,
    IReadOnlyList<MailboxAddress> Bcc,
    DateTimeOffset? Sent,
    DateTimeOffset? Received,
    string Body,
    bool IsHtml,
    bool IsRead,
    Importance Importance,
    IReadOnlyList<AttachmentInfo> Attachments,
    string? ItemClass = null);

/// <summary>A conversation (thread) as listed in one folder.</summary>
public sealed record ConversationSummary(
    string ConversationId,
    string Topic,
    IReadOnlyList<string> Senders,
    IReadOnlyList<string> Recipients,
    DateTimeOffset LastDelivery,
    int MessageCount,
    int GlobalMessageCount,
    int UnreadCount,
    bool HasAttachments,
    Importance Importance,
    IReadOnlyList<EwsItemId> ItemIds);

public sealed record ConversationPage(IReadOnlyList<ConversationSummary> Items, int TotalCount, int NextOffset, bool IsLastPage);

/// <summary>One message of a conversation, with only the text it added (not the quoted history).</summary>
public sealed record ThreadMessage(
    EwsItemId ItemId,
    string Subject,
    MailboxAddress? From,
    IReadOnlyList<MailboxAddress> To,
    DateTimeOffset? Received,
    string Body,
    bool IsHtml,
    string Preview,
    string ItemClass,
    bool IsRead = true);

public sealed record OutgoingAttachment(string Name, string ContentType, byte[] Content);

public sealed class OutgoingMessage
{
    public ComposeMode Mode { get; init; } = ComposeMode.New;
    /// <summary>Item being replied to or forwarded; required unless <see cref="Mode"/> is New.</summary>
    public EwsItemId? ReferenceItemId { get; init; }
    public IList<string> To { get; init; } = [];
    public IList<string> Cc { get; init; } = [];
    public IList<string> Bcc { get; init; } = [];
    public string Subject { get; init; } = "";
    public string HtmlBody { get; init; } = "";
    public Importance Importance { get; init; } = Importance.Normal;
    public IList<OutgoingAttachment> Attachments { get; init; } = [];
    /// <summary>When set, this existing draft is overwritten (and sent) instead of creating a new item.</summary>
    public EwsItemId? ExistingDraftId { get; init; }
    /// <summary>Attachments of <see cref="ExistingDraftId"/> the user removed.</summary>
    public IList<string> RemovedAttachmentIds { get; init; } = [];
}
