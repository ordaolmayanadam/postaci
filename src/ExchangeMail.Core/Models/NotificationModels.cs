namespace ExchangeMail.Core.Models;

public enum MailboxEventKind { NewMail, Created, Deleted, Modified, Moved, Copied, Other }

/// <summary>
/// A change reported by a streaming subscription. <see cref="FolderId"/> is set for folder events,
/// <see cref="ParentFolderId"/> (and for moves/copies <see cref="OldParentFolderId"/>) for item and folder events.
/// </summary>
public sealed record MailboxEvent(MailboxEventKind Kind, string? FolderId, string? ParentFolderId, string? OldParentFolderId);
