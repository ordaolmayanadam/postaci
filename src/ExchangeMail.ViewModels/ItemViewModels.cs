using CommunityToolkit.Mvvm.ComponentModel;
using ExchangeMail.Core.Ews;
using ExchangeMail.Core.Models;

namespace ExchangeMail.ViewModels;

public sealed partial class FolderViewModel(MailFolder folder, int depth) : ObservableObject
{
    public string Id => folder.Id;
    /// <summary>
    /// Standard folders use the interface language; the server names them in the mailbox's language, which can
    /// differ from the language chosen in Postacı.
    /// </summary>
    public string DisplayName => WellKnown switch
    {
        WellKnownFolder.Inbox => Strings.Folder_Inbox,
        WellKnownFolder.Drafts => Strings.Folder_Drafts,
        WellKnownFolder.SentItems => Strings.Folder_SentItems,
        WellKnownFolder.DeletedItems => Strings.Folder_DeletedItems,
        WellKnownFolder.JunkEmail => Strings.Folder_JunkEmail,
        WellKnownFolder.Outbox => Strings.Folder_Outbox,
        _ => folder.DisplayName,
    };
    public int Depth => depth;
    public WellKnownFolder WellKnown => folder.WellKnown;

    /// <summary>Folders whose list should show recipients rather than the sender.</summary>
    public bool ShowsRecipients => WellKnown is WellKnownFolder.SentItems or WellKnownFolder.Drafts or WellKnownFolder.Outbox;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UnreadText), nameof(HasUnread))]
    private int _unreadCount = folder.UnreadCount;

    [ObservableProperty]
    private int _totalCount = folder.TotalCount;

    public bool HasUnread => UnreadCount > 0 && WellKnown is not (WellKnownFolder.SentItems or WellKnownFolder.Outbox);
    public string UnreadText => HasUnread ? UnreadCount.ToString() : "";

    public string AccessibleName => AccessibleText.Join(DisplayName, HasUnread ? Loc.Format(Strings.A11y_UnreadCount, UnreadCount) : null);
    public override string ToString() => AccessibleName;

    /// <summary>Segoe Fluent Icons glyph.</summary>
    public string Glyph => WellKnown switch
    {
        WellKnownFolder.Inbox => "",
        WellKnownFolder.Drafts => "",
        WellKnownFolder.SentItems => "",
        WellKnownFolder.DeletedItems => "",
        WellKnownFolder.JunkEmail => "",
        WellKnownFolder.Outbox => "",
        _ => "",
    };

    public static IEnumerable<FolderViewModel> Flatten(IEnumerable<MailFolder> folders, int depth = 0)
    {
        foreach (var f in folders)
        {
            yield return new FolderViewModel(f, depth);
            foreach (var child in Flatten(f.Children, depth + 1)) yield return child;
        }
    }
}

/// <summary>A row of the message list: a single message, or a conversation when <see cref="Conversation"/> is set.</summary>
public sealed partial class MessageItemViewModel(MailSummary summary, bool showRecipients) : ObservableObject
{
    public MailSummary Summary { get; } = summary;

    /// <summary>Current id; the change key is refreshed after updates. For a conversation, its newest item here.</summary>
    public EwsItemId Id { get; set; } = summary.ItemId;

    /// <summary>The conversation this row stands for; null for a plain message.</summary>
    public ConversationSummary? Conversation { get; private init; }

    /// <summary>Every item the row covers in the current folder; actions (delete, move, read) apply to all of them.</summary>
    public IReadOnlyList<EwsItemId> ItemIds => Conversation?.ItemIds ?? [Id];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUnread))]
    private bool _isRead = summary.IsRead;

    public bool IsUnread => !IsRead;

    private int _conversationUnread;

    /// <summary>Unread items behind the row (0 or 1 for a message).</summary>
    public int UnreadItems => IsRead ? 0 : Conversation is null ? 1 : Math.Max(1, _conversationUnread);

    /// <summary>Marks the row read or unread; a conversation marked unread counts all of its items as unread.</summary>
    public void SetRead(bool isRead)
    {
        if (!isRead && Conversation is not null) _conversationUnread = ItemIds.Count;
        IsRead = isRead;
    }

    /// <summary>
    /// Messages in the whole conversation (including sent replies, each message once however many folders hold a
    /// copy); for a plain message 1.
    /// </summary>
    public int MessageCount { get; private init; } = 1;

    /// <summary>The conversation's message count, shown when more than one.</summary>
    public string CountText => MessageCount > 1 ? MessageCount.ToString() : "";
    public bool HasCount => CountText.Length > 0;

    /// <summary>Conversation rows keep a column for the chevron so their text lines up, opened or not.</summary>
    public bool HasExpanderColumn => Conversation is not null;

    /// <summary>A conversation with several messages can be opened up in the list.</summary>
    public bool CanExpand => Conversation is not null && MessageCount > 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExpandGlyph))]
    private bool _isExpanded;

    /// <summary>Segoe Fluent chevron: down when open, right when closed.</summary>
    public string ExpandGlyph => IsExpanded ? "\uE70D" : "\uE76C";

    /// <summary>For a message shown under its conversation: that conversation's row.</summary>
    public MessageItemViewModel? Parent { get; private init; }

    public bool IsChild => Parent is not null;

    /// <summary>Messages under a conversation are indented and repeat neither the subject nor the chevron space.</summary>
    public double IndentWidth => IsChild ? 28 : 0;
    public bool ShowSubject => !IsChild;

    public bool HasPreview => Preview.Length > 0;

    /// <summary>
    /// A conversation row. <paramref name="newest"/> (the conversation's newest message in this folder, when known)
    /// supplies the preview, the id replies and actions start from, and the sender of a one-person conversation.
    /// <paramref name="messageCount"/> is the deduplicated count; without it the server's count (which counts each
    /// copy) is used.
    /// </summary>
    public static MessageItemViewModel ForConversation(ConversationSummary c, bool showRecipients, MailSummary? newest = null, int? messageCount = null)
    {
        var people = showRecipients ? c.Recipients : c.Senders;
        var from = people.Count > 1 || newest?.From is null
            ? (people.Count > 0 ? new MailboxAddress(string.Join(", ", people), "") : newest?.From)
            : newest.From;
        var summary = new MailSummary(
            newest?.ItemId ?? c.ItemIds.FirstOrDefault() ?? new EwsItemId("", null),
            c.Topic,
            from,
            showRecipients ? string.Join(", ", people) : "",
            c.LastDelivery,
            c.UnreadCount == 0,
            c.HasAttachments,
            c.Importance,
            newest?.Preview ?? "",
            newest?.ItemClass ?? "IPM.Conversation");
        return new MessageItemViewModel(summary, showRecipients)
        {
            Conversation = c,
            MessageCount = messageCount ?? c.GlobalMessageCount,
            _conversationUnread = c.UnreadCount,
        };
    }

    /// <summary>One message of an opened conversation, shown under <paramref name="parent"/>.</summary>
    public static MessageItemViewModel ForThreadMessage(ThreadMessage m, MessageItemViewModel parent) =>
        new(new MailSummary(m.ItemId, m.Subject, m.From, string.Join("; ", m.To.Select(t => t.Display)),
                m.Received ?? DateTimeOffset.MinValue, m.IsRead, false, Importance.Normal, m.Preview, m.ItemClass),
            showRecipients: false)
        { Parent = parent };

    public string Sender => showRecipients
        ? (Summary.DisplayTo.Length > 0 ? Summary.DisplayTo : Strings.Mail_NoRecipient)
        : Summary.From?.Display ?? Strings.Mail_NoSender;

    public string Subject => string.IsNullOrWhiteSpace(Summary.Subject) ? Strings.Common_NoSubject : Summary.Subject;
    public string Preview => Summary.Preview.ReplaceLineEndings(" ");
    public string DateText => MailFormat.ListDate(Summary.Received);
    public bool HasAttachments => Summary.HasAttachments;
    public bool IsHighImportance => Summary.Importance == Importance.High;
    public bool IsMeeting => Summary.ItemClass.StartsWith("IPM.Schedule.Meeting", StringComparison.OrdinalIgnoreCase);

    /// <summary>What the row shows, in reading order, with the state its icons and accent bar carry.</summary>
    public string AccessibleName => AccessibleText.Join(
        IsUnread ? Strings.A11y_Unread : null,
        Sender,
        ShowSubject ? Subject : null,
        DateText,
        HasCount ? Loc.Count(MessageCount, Strings.A11y_MessageCount_One, Strings.A11y_MessageCount) : null,
        IsMeeting ? Strings.A11y_MeetingInvitation : null,
        HasAttachments ? Strings.A11y_HasAttachments : null,
        IsHighImportance ? Strings.A11y_HighImportance : null);

    public override string ToString() => AccessibleName;
}

public sealed class AttachmentViewModel(AttachmentInfo info)
{
    public AttachmentInfo Info { get; } = info;
    public string Name => Info.Name;
    public string SizeText => Info.Size > 0 ? MailFormat.FileSize(Info.Size) : "";
    public override string ToString() => AccessibleText.Join(Name, SizeText);

    public string Glyph => Info.IsItemAttachment ? "" : Path.GetExtension(Info.Name).ToLowerInvariant() switch
    {
        ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" => "",
        ".zip" or ".7z" or ".rar" => "",
        _ => "",
    };
}

/// <summary>The message shown in the reading pane.</summary>
public sealed partial class MessageDetailViewModel : ObservableObject
{
    private const long MaxInlineImageBytes = 15 * 1024 * 1024;
    private string _body;

    /// <summary>The whole conversation as one document, or null for a single message.</summary>
    private string? _threadHtml;

    /// <param name="thread">The conversation's messages (newest first); with more than one, they are shown instead of
    /// <paramref name="detail"/>'s body. <paramref name="detail"/> stays the message that replies and attachments use.</param>
    public MessageDetailViewModel(MailDetail detail, bool allowRemoteContent = false, IReadOnlyList<ThreadMessage>? thread = null)
    {
        Detail = detail;
        _allowRemoteContent = allowRemoteContent;
        _body = detail.Body;
        ThreadCount = thread?.Count ?? 1;
        if (ThreadCount > 1) _threadHtml = MailFormat.BuildThreadHtml(thread!);
        Attachments = detail.Attachments
            .Where(a => !(a.IsInline && MailFormat.ReferencesContentId(detail.Body, a.ContentId)))
            .Select(a => new AttachmentViewModel(a))
            .ToList();
        HasBlockedContent = _threadHtml is not null
            ? MailFormat.HasRemoteContent(_threadHtml)
            : detail.IsHtml && MailFormat.HasRemoteContent(detail.Body);
    }

    public int ThreadCount { get; }
    public bool IsThread => ThreadCount > 1;
    public string ThreadText => IsThread ? Loc.Count(ThreadCount, Strings.Mail_ThreadCount_One, Strings.Mail_ThreadCount) : "";

    public MailDetail Detail { get; }
    public string Subject => string.IsNullOrWhiteSpace(Detail.Subject) ? Strings.Common_NoSubject : Detail.Subject;
    public string FromName => Detail.From?.Display ?? "";
    public string FromAddress => Detail.From?.Address ?? "";
    public string ToText => string.Join("; ", Detail.To.Select(m => m.Display));
    public string CcText => string.Join("; ", Detail.Cc.Select(m => m.Display));
    public bool HasCc => Detail.Cc.Count > 0;
    public string DateText => MailFormat.LongDate(Detail.Received ?? Detail.Sent);
    public bool IsHighImportance => Detail.Importance == Importance.High;
    public IReadOnlyList<AttachmentViewModel> Attachments { get; }
    public bool HasAttachments => Attachments.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayHtml), nameof(ShowRemoteContentBanner))]
    private bool _allowRemoteContent;

    public bool HasBlockedContent { get; }
    public bool ShowRemoteContentBanner => HasBlockedContent && !AllowRemoteContent;

    public string DisplayHtml => _threadHtml is { } thread
        ? MailFormat.PrepareForDisplay(thread, true, AllowRemoteContent)
        : MailFormat.PrepareForDisplay(_body, Detail.IsHtml, AllowRemoteContent);

    /// <summary>Replaces cid: references with data: URIs so embedded images render.</summary>
    public async Task LoadInlineImagesAsync(EwsClient client, CancellationToken ct)
    {
        if (!Detail.IsHtml) return;
        long loaded = 0;
        foreach (var attachment in Detail.Attachments.Where(a => !a.IsItemAttachment && MailFormat.ReferencesContentId(_body, a.ContentId)))
        {
            if (loaded + attachment.Size > MaxInlineImageBytes) break;
            try
            {
                var content = await client.GetAttachmentAsync(attachment, ct);
                loaded += content.Content.Length;
                var type = content.ContentType ?? attachment.ContentType ?? "image/png";
                var data = $"data:{type};base64,{Convert.ToBase64String(content.Content)}";
                _body = MailFormat.ReplaceContentId(_body, attachment.ContentId!, data);
                if (_threadHtml is not null) _threadHtml = MailFormat.ReplaceContentId(_threadHtml, attachment.ContentId!, data);
            }
            catch (EwsException)
            {
                // A missing inline image should not prevent the message from showing.
            }
        }
        OnPropertyChanged(nameof(DisplayHtml));
    }
}
