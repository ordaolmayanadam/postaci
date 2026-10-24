using System.Xml.Linq;
using ExchangeMail.Core.Models;
using static ExchangeMail.Core.Ews.Xml;

namespace ExchangeMail.Core.Ews;

public sealed partial class EwsClient
{
    /// <summary>Lists the personal Contacts folder, sorted by name. <paramref name="query"/> is an AQS string.</summary>
    public async Task<ContactPage> FindContactsAsync(int offset = 0, int pageSize = 100, string? query = null, CancellationToken ct = default)
    {
        var request = M("FindItem", new XAttribute("Traversal", "Shallow"),
            M("ItemShape",
                T("BaseShape", "IdOnly"),
                T("AdditionalProperties",
                    Field("contacts:DisplayName"),
                    Field("contacts:GivenName"),
                    Field("contacts:Surname"),
                    Field("contacts:CompanyName"),
                    Field("contacts:JobTitle"),
                    Field("contacts:Department"),
                    IndexedField("contacts:EmailAddress", "EmailAddress1"),
                    IndexedField("contacts:PhoneNumber", "BusinessPhone"),
                    IndexedField("contacts:PhoneNumber", "MobilePhone"))),
            M("IndexedPageItemView",
                new XAttribute("MaxEntriesReturned", pageSize),
                new XAttribute("Offset", offset),
                new XAttribute("BasePoint", "Beginning")),
            M("SortOrder", T("FieldOrder", new XAttribute("Order", "Ascending"), Field("contacts:DisplayName"))),
            M("ParentFolderIds", DistinguishedFolder("contacts")),
            string.IsNullOrWhiteSpace(query) ? null : M("QueryString", query.Trim()));

        var root = Single(await ExecuteAsync(request, ct)).Element(MNs + "RootFolder")
                   ?? throw new EwsException("FindItem yanıtında RootFolder yok.");

        var items = root.Element(TNs + "Items")?.Elements().Select(ParseContact).ToList() ?? [];
        return new ContactPage(
            items,
            (int?)root.Attribute("TotalItemsInView") ?? items.Count,
            (int?)root.Attribute("IndexedPagingOffset") ?? offset + items.Count,
            (bool?)root.Attribute("IncludesLastItemInRange") ?? true);
    }

    /// <summary>Searches the global address list (company directory). Exchange returns at most 100 matches.</summary>
    public async Task<IReadOnlyList<ContactInfo>> SearchDirectoryAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];

        var request = M("ResolveNames",
            new XAttribute("ReturnFullContactData", "true"),
            new XAttribute("SearchScope", "ActiveDirectory"),
            M("UnresolvedEntry", text.Trim()));

        var message = ResponseMessages(await ExecuteAsync(request, ct)).FirstOrDefault();
        if (message is null || (string?)message.Element(MNs + "ResponseCode") == "ErrorNameResolutionNoResults")
            return [];
        if ((string?)message.Attribute("ResponseClass") == "Error") Ok(message);

        return message.Element(MNs + "ResolutionSet")?.Elements(TNs + "Resolution").Select(r =>
        {
            var mailbox = ParseMailbox(r.Element(TNs + "Mailbox"));
            var contact = r.Element(TNs + "Contact");
            return new ContactInfo(
                null,
                (string?)contact?.Element(TNs + "DisplayName") ?? mailbox?.Display ?? "",
                mailbox?.Address,
                (string?)contact?.Element(TNs + "CompanyName"),
                (string?)contact?.Element(TNs + "JobTitle"),
                (string?)contact?.Element(TNs + "Department"),
                Entry(contact?.Element(TNs + "PhoneNumbers"), "BusinessPhone"),
                Entry(contact?.Element(TNs + "PhoneNumbers"), "MobilePhone"),
                (string?)r.Element(TNs + "Mailbox")?.Element(TNs + "MailboxType") is "PublicDL" or "PrivateDL");
        }).ToList() ?? [];
    }

    public async Task<EwsItemId> CreateContactAsync(NewContact contact, CancellationToken ct = default)
    {
        var displayName = $"{contact.GivenName} {contact.Surname}".Trim();
        if (displayName.Length == 0) throw new ArgumentException("Ad veya soyad girin.", nameof(contact));

        var phones = new[] { ("BusinessPhone", contact.BusinessPhone), ("MobilePhone", contact.MobilePhone) }
            .Where(p => !string.IsNullOrWhiteSpace(p.Item2)).ToList();

        // Element order follows the EWS schema for ContactItemType.
        var item = T("Contact",
            T("DisplayName", displayName),
            Optional("GivenName", contact.GivenName),
            Optional("CompanyName", contact.Company),
            string.IsNullOrWhiteSpace(contact.Email) ? null
                : T("EmailAddresses", T("Entry", new XAttribute("Key", "EmailAddress1"), contact.Email.Trim())),
            phones.Count == 0 ? null
                : T("PhoneNumbers", phones.Select(p => T("Entry", new XAttribute("Key", p.Item1), p.Item2.Trim()))),
            Optional("Department", contact.Department),
            Optional("JobTitle", contact.JobTitle),
            Optional("Surname", contact.Surname));

        var request = M("CreateItem",
            M("SavedItemFolderId", DistinguishedFolder("contacts")),
            M("Items", item));
        var message = Single(await ExecuteAsync(request, ct));
        return ParseItemId(message.Element(MNs + "Items")?.Elements().FirstOrDefault()?.Element(TNs + "ItemId"));

        static XElement? Optional(string name, string value) => string.IsNullOrWhiteSpace(value) ? null : T(name, value.Trim());
    }

    /// <summary>Overwrites the editable fields of a personal contact; empty values clear the field.</summary>
    public async Task UpdateContactAsync(EwsItemId id, NewContact contact, CancellationToken ct = default)
    {
        var displayName = $"{contact.GivenName} {contact.Surname}".Trim();
        if (displayName.Length == 0) throw new ArgumentException("Ad veya soyad girin.", nameof(contact));

        var updates = new List<XElement> { SetContactField(Field("contacts:DisplayName"), T("DisplayName", displayName)) };
        foreach (var (uri, name, value) in new[]
                 {
                     ("contacts:GivenName", "GivenName", contact.GivenName),
                     ("contacts:Surname", "Surname", contact.Surname),
                     ("contacts:CompanyName", "CompanyName", contact.Company),
                     ("contacts:JobTitle", "JobTitle", contact.JobTitle),
                     ("contacts:Department", "Department", contact.Department),
                 })
        {
            updates.Add(string.IsNullOrWhiteSpace(value)
                ? T("DeleteItemField", Field(uri))
                : SetContactField(Field(uri), T(name, value.Trim())));
        }

        foreach (var (uri, dictionary, key, value) in new[]
                 {
                     ("contacts:EmailAddress", "EmailAddresses", "EmailAddress1", contact.Email),
                     ("contacts:PhoneNumber", "PhoneNumbers", "BusinessPhone", contact.BusinessPhone),
                     ("contacts:PhoneNumber", "PhoneNumbers", "MobilePhone", contact.MobilePhone),
                 })
        {
            updates.Add(string.IsNullOrWhiteSpace(value)
                ? T("DeleteItemField", IndexedField(uri, key))
                : SetContactField(IndexedField(uri, key), T(dictionary, T("Entry", new XAttribute("Key", key), value.Trim()))));
        }

        var request = M("UpdateItem",
            new XAttribute("ConflictResolution", "AlwaysOverwrite"),
            M("ItemChanges", T("ItemChange", ItemIdElement(id with { ChangeKey = null }), T("Updates", updates))));
        foreach (var m in ResponseMessages(await ExecuteAsync(request, ct))) Ok(m);

        static XElement SetContactField(XElement fieldUri, XElement value) => T("SetItemField", fieldUri, T("Contact", value));
    }

    private static XElement IndexedField(string uri, string index) =>
        T("IndexedFieldURI", new XAttribute("FieldURI", uri), new XAttribute("FieldIndex", index));

    private static string? Entry(XElement? dictionary, string key)
    {
        var value = (string?)dictionary?.Elements(TNs + "Entry").FirstOrDefault(e => (string?)e.Attribute("Key") == key);
        if (string.IsNullOrWhiteSpace(value)) return null;
        // Directory entries come back as "SMTP:user@domain".
        return value.StartsWith("SMTP:", StringComparison.OrdinalIgnoreCase) ? value[5..] : value;
    }

    private static ContactInfo ParseContact(XElement item) => new(
        ParseItemId(item.Element(TNs + "ItemId")),
        (string?)item.Element(TNs + "DisplayName") ?? "",
        Entry(item.Element(TNs + "EmailAddresses"), "EmailAddress1"),
        (string?)item.Element(TNs + "CompanyName"),
        (string?)item.Element(TNs + "JobTitle"),
        (string?)item.Element(TNs + "Department"),
        Entry(item.Element(TNs + "PhoneNumbers"), "BusinessPhone"),
        Entry(item.Element(TNs + "PhoneNumbers"), "MobilePhone"),
        item.Name.LocalName == "DistributionList",
        (string?)item.Element(TNs + "GivenName"),
        (string?)item.Element(TNs + "Surname"));
}
