namespace ExchangeMail.Core.Models;

public enum AutoReplyState { Disabled, Enabled, Scheduled }

/// <summary>Who outside the organization receives the external reply.</summary>
public enum ExternalAudience { None, Known, All }

/// <summary>Out of Office ("Otomatik yanıtlar") settings of a mailbox.</summary>
public sealed record AutoReplySettings(
    AutoReplyState State,
    DateTimeOffset? Start,
    DateTimeOffset? End,
    string InternalReplyHtml,
    string ExternalReplyHtml,
    ExternalAudience ExternalAudience)
{
    /// <summary>True when replies are being sent right now.</summary>
    public bool IsActive(DateTimeOffset now) => State switch
    {
        AutoReplyState.Enabled => true,
        AutoReplyState.Scheduled => Start <= now && now < End,
        _ => false,
    };
}
