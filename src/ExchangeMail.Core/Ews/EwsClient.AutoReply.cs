using System.Globalization;
using System.Xml.Linq;
using ExchangeMail.Core.Models;
using static ExchangeMail.Core.Ews.Xml;

namespace ExchangeMail.Core.Ews;

public sealed partial class EwsClient
{
    /// <summary>Reads the Out of Office (automatic replies) settings of <paramref name="mailbox"/>.</summary>
    public async Task<AutoReplySettings> GetAutoReplyAsync(string mailbox, CancellationToken ct = default)
    {
        var request = M("GetUserOofSettingsRequest", T("Mailbox", T("Address", mailbox)));
        var response = await ExecuteAsync(request, ct);
        EnsureOofSuccess(response);

        var settings = response.Element(TNs + "OofSettings") ?? throw new EwsException("Yanıtta otomatik yanıt ayarları yok.");
        var duration = settings.Element(TNs + "Duration");
        return new AutoReplySettings(
            Enum.TryParse<AutoReplyState>((string?)settings.Element(TNs + "OofState"), out var state) ? state : AutoReplyState.Disabled,
            ParseOofTime((string?)duration?.Element(TNs + "StartTime")),
            ParseOofTime((string?)duration?.Element(TNs + "EndTime")),
            (string?)settings.Element(TNs + "InternalReply")?.Element(TNs + "Message") ?? "",
            (string?)settings.Element(TNs + "ExternalReply")?.Element(TNs + "Message") ?? "",
            Enum.TryParse<ExternalAudience>((string?)settings.Element(TNs + "ExternalAudience"), out var audience) ? audience : ExternalAudience.None);
    }

    public async Task SetAutoReplyAsync(string mailbox, AutoReplySettings settings, CancellationToken ct = default)
    {
        if (settings.State == AutoReplyState.Scheduled && !(settings.Start < settings.End))
            throw new ArgumentException("Bitiş zamanı başlangıçtan sonra olmalı.", nameof(settings));

        // Exchange always expects a valid window, even when replies are not scheduled.
        var start = settings.Start ?? DateTimeOffset.UtcNow;
        var end = settings.End is { } e && e > start ? e : start.AddDays(1);

        var request = M("SetUserOofSettingsRequest",
            T("Mailbox", T("Address", mailbox)),
            T("UserOofSettings",
                T("OofState", settings.State.ToString()),
                T("ExternalAudience", settings.ExternalAudience.ToString()),
                T("Duration", T("StartTime", EwsDate(start)), T("EndTime", EwsDate(end))),
                T("InternalReply", T("Message", settings.InternalReplyHtml)),
                T("ExternalReply", T("Message", settings.ExternalReplyHtml))));

        EnsureOofSuccess(await ExecuteAsync(request, ct));
    }

    // OOF operations answer with a single ResponseMessage instead of the usual ResponseMessages list.
    private static void EnsureOofSuccess(XElement response)
    {
        var message = response.Elements().FirstOrDefault(e => e.Name.LocalName == "ResponseMessage")
                      ?? throw new EwsException("Yanıtta ResponseMessage yok.");
        if ((string?)message.Attribute("ResponseClass") != "Error") return;
        var code = message.Elements().FirstOrDefault(e => e.Name.LocalName == "ResponseCode")?.Value;
        var text = message.Elements().FirstOrDefault(e => e.Name.LocalName == "MessageText")?.Value ?? code ?? "Bilinmeyen EWS hatası";
        throw new EwsException(text, code);
    }

    private static DateTimeOffset? ParseOofTime(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
}
