using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using ExchangeMail.Core.Models;
using static ExchangeMail.Core.Ews.Xml;

namespace ExchangeMail.Core.Ews;

public sealed partial class EwsClient
{
    private static readonly string[] StreamingEventTypes =
        ["NewMailEvent", "CreatedEvent", "DeletedEvent", "ModifiedEvent", "MovedEvent", "CopiedEvent"];

    private HttpClient? _streamingHttp;

    /// <summary>Streaming connections stay open for minutes, beyond the normal request timeout.</summary>
    private HttpClient StreamingHttp => _streamingHttp ??= CreateStreamingClient();

    private HttpClient CreateStreamingClient()
    {
        var client = new HttpClient(_handler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Postaci/1.0");
        return client;
    }

    /// <summary>Creates a streaming subscription to item and folder changes in every folder of the mailbox.</summary>
    public async Task<string> SubscribeToStreamingAsync(CancellationToken ct = default)
    {
        // SubscribeToAllFolders takes no FolderIds; the subscription expires 30 minutes after its last connection.
        var request = M("Subscribe",
            M("StreamingSubscriptionRequest", new XAttribute("SubscribeToAllFolders", "true"),
                T("EventTypes", StreamingEventTypes.Select(e => T("EventType", e)))));

        var message = Single(await ExecuteAsync(request, ct));
        return (string?)message.Element(MNs + "SubscriptionId") ?? throw new EwsException(Strings.Ews_SubscriptionIdMissing);
    }

    /// <summary>
    /// Opens a streaming connection and reports each batch of events as it arrives. Returns when the server closes the
    /// connection after <paramref name="connectionTimeoutMinutes"/> (the caller reconnects with the same subscription);
    /// throws <see cref="EwsException"/> when the subscription is gone or the server fails.
    /// </summary>
    public async Task ReceiveStreamingEventsAsync(string subscriptionId, int connectionTimeoutMinutes,
        Action<IReadOnlyList<MailboxEvent>> onEvents, CancellationToken ct = default)
    {
        var operation = M("GetStreamingEvents",
            M("SubscriptionIds", T("SubscriptionId", subscriptionId)),
            M("ConnectionTimeout", Math.Clamp(connectionTimeoutMinutes, 1, 30)));

        using var request = BuildRequest(operation, withTimeZone: false);
        HttpResponseMessage response;
        try
        {
            response = await StreamingHttp.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new EwsException(Loc.Format(Strings.Ews_ServerUnreachable, ex.Message), null, ex);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new EwsAuthenticationException(Strings.Ews_AuthFailed);

            // The server keeps the response open and writes one complete SOAP envelope per batch (and per heartbeat).
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var buffer = new StringBuilder();
            var chunk = new char[8192];
            int read;
            try
            {
                while ((read = await reader.ReadAsync(chunk, ct)) > 0)
                {
                    buffer.Append(chunk, 0, read);
                    while (TakeEnvelope(buffer) is { } xml)
                    {
                        if (HandleStreamingEnvelope(xml, onEvents)) return;
                    }
                }
            }
            catch (IOException ex) when (!ct.IsCancellationRequested)
            {
                throw new EwsException(Loc.Format(Strings.Ews_ServerUnreachable, ex.Message), null, ex);
            }

            if (!response.IsSuccessStatusCode)
                throw new EwsException($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
        }
    }

    /// <summary>Handles one streamed envelope; true when the server closed the connection.</summary>
    private static bool HandleStreamingEnvelope(string xml, Action<IReadOnlyList<MailboxEvent>> onEvents)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Parse(xml.TrimStart());
        }
        catch (XmlException ex)
        {
            throw new EwsException(Strings.Ews_StreamUnreadable, null, ex);
        }
        ThrowIfFault(doc);

        var closed = false;
        foreach (var message in ResponseMessages(BodyOf(doc)))
        {
            Ok(message); // ErrorSubscriptionNotFound etc.: the caller subscribes again
            var events = message.Element(MNs + "Notifications")?.Elements()
                .SelectMany(n => n.Elements().Where(e => e.Name.LocalName.EndsWith("Event", StringComparison.Ordinal)))
                .Select(ParseMailboxEvent)
                .ToList() ?? [];
            if (events.Count > 0) onEvents(events);
            closed |= (string?)message.Element(MNs + "ConnectionStatus") == "Closed";
        }
        return closed;
    }

    /// <summary>Removes and returns the first complete envelope in <paramref name="buffer"/>, or null if none is complete yet.</summary>
    internal static string? TakeEnvelope(StringBuilder buffer)
    {
        var text = buffer.ToString();
        var end = EnvelopeEnd().Match(text);
        if (!end.Success) return null;
        var length = end.Index + end.Length;
        buffer.Remove(0, length);
        return text[..length];
    }

    private static MailboxEvent ParseMailboxEvent(XElement e) => new(
        e.Name.LocalName switch
        {
            "NewMailEvent" => MailboxEventKind.NewMail,
            "CreatedEvent" => MailboxEventKind.Created,
            "DeletedEvent" => MailboxEventKind.Deleted,
            "ModifiedEvent" => MailboxEventKind.Modified,
            "MovedEvent" => MailboxEventKind.Moved,
            "CopiedEvent" => MailboxEventKind.Copied,
            _ => MailboxEventKind.Other,
        },
        (string?)e.Element(TNs + "FolderId")?.Attribute("Id"),
        (string?)e.Element(TNs + "ParentFolderId")?.Attribute("Id"),
        (string?)e.Element(TNs + "OldParentFolderId")?.Attribute("Id"));

    [GeneratedRegex(@"</(?:[A-Za-z0-9_]+:)?Envelope\s*>")]
    private static partial Regex EnvelopeEnd();
}
