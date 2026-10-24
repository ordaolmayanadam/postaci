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

public sealed partial class MessageItemViewModel(MailSummary summary, bool showRecipients) : ObservableObject
{
    public MailSummary Summary { get; } = summary;

    /// <summary>Current id; the change key is refreshed after updates.</summary>
    public EwsItemId Id { get; set; } = summary.ItemId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUnread))]
    private bool _isRead = summary.IsRead;

    public bool IsUnread => !IsRead;

    public string Sender => showRecipients
        ? (Summary.DisplayTo.Length > 0 ? Summary.DisplayTo : Strings.Mail_NoRecipient)
        : Summary.From?.Display ?? Strings.Mail_NoSender;

    public string Subject => string.IsNullOrWhiteSpace(Summary.Subject) ? Strings.Common_NoSubject : Summary.Subject;
    public string Preview => Summary.Preview.ReplaceLineEndings(" ");
    public string DateText => MailFormat.ListDate(Summary.Received);
    public bool HasAttachments => Summary.HasAttachments;
    public bool IsHighImportance => Summary.Importance == Importance.High;
    public bool IsMeeting => Summary.ItemClass.StartsWith("IPM.Schedule.Meeting", StringComparison.OrdinalIgnoreCase);
}

public sealed class AttachmentViewModel(AttachmentInfo info)
{
    public AttachmentInfo Info { get; } = info;
    public string Name => Info.Name;
    public string SizeText => Info.Size > 0 ? MailFormat.FileSize(Info.Size) : "";

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

    public MessageDetailViewModel(MailDetail detail, bool allowRemoteContent = false)
    {
        Detail = detail;
        _allowRemoteContent = allowRemoteContent;
        _body = detail.Body;
        Attachments = detail.Attachments
            .Where(a => !(a.IsInline && MailFormat.ReferencesContentId(detail.Body, a.ContentId)))
            .Select(a => new AttachmentViewModel(a))
            .ToList();
        HasBlockedContent = detail.IsHtml && MailFormat.HasRemoteContent(detail.Body);
    }

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

    public string DisplayHtml => MailFormat.PrepareForDisplay(_body, Detail.IsHtml, AllowRemoteContent);

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
                _body = MailFormat.ReplaceContentId(_body, attachment.ContentId!, $"data:{type};base64,{Convert.ToBase64String(content.Content)}");
            }
            catch (EwsException)
            {
                // A missing inline image should not prevent the message from showing.
            }
        }
        OnPropertyChanged(nameof(DisplayHtml));
    }
}
