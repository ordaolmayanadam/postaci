using System.Xml.Linq;
using ExchangeMail.Core.Models;

namespace ExchangeMail.Core.Ews;

/// <summary>Helpers for building and reading EWS SOAP payloads.</summary>
internal static class Xml
{
    public static readonly XNamespace Soap = "http://schemas.xmlsoap.org/soap/envelope/";
    public static readonly XNamespace TNs = "http://schemas.microsoft.com/exchange/services/2006/types";
    public static readonly XNamespace MNs = "http://schemas.microsoft.com/exchange/services/2006/messages";

    public static XElement T(string name, params object?[] content) => new(TNs + name, content);
    public static XElement M(string name, params object?[] content) => new(MNs + name, content);

    public static XElement Field(string uri) => T("FieldURI", new XAttribute("FieldURI", uri));

    public static XElement DistinguishedFolder(string id) => T("DistinguishedFolderId", new XAttribute("Id", id));

    public static XElement FolderIdElement(string id) => T("FolderId", new XAttribute("Id", id));

    public static XElement ItemIdElement(EwsItemId id, string name = "ItemId") =>
        T(name, new XAttribute("Id", id.Id), id.ChangeKey is null ? null : new XAttribute("ChangeKey", id.ChangeKey));

    public static XElement MailboxElement(string address) => T("Mailbox", T("EmailAddress", address));

    public static EwsItemId ParseItemId(XElement? el) =>
        el is null
            ? throw new EwsException(Strings.Ews_ItemIdMissing)
            : new EwsItemId((string)el.Attribute("Id")!, (string?)el.Attribute("ChangeKey"));

    public static MailboxAddress? ParseMailbox(XElement? mailbox)
    {
        if (mailbox is null) return null;
        var name = (string?)mailbox.Element(TNs + "Name");
        var address = (string?)mailbox.Element(TNs + "EmailAddress") ?? "";
        if (name is null && address.Length == 0) return null;
        return new MailboxAddress(name, address);
    }

    public static IReadOnlyList<MailboxAddress> ParseMailboxList(XElement? list) =>
        list?.Elements(TNs + "Mailbox").Select(ParseMailbox).OfType<MailboxAddress>().ToList() ?? [];

    public static Importance ParseImportance(XElement? el) =>
        Enum.TryParse<Importance>((string?)el, out var value) ? value : Importance.Normal;
}
