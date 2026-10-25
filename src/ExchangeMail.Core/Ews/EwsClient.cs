using System.Net;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using ExchangeMail.Core.Models;
using static ExchangeMail.Core.Ews.Xml;

namespace ExchangeMail.Core.Ews;

/// <summary>
/// Minimal Exchange Web Services client for on-premises Exchange (2013 SP1, 2016, 2019, Subscription Edition).
/// Speaks raw SOAP over HttpClient so it does not depend on the retired EWS Managed API.
/// </summary>
public sealed partial class EwsClient : IDisposable
{
    public const string ServerVersion = "Exchange2013_SP1";

    private static readonly string[] WellKnownIds = ["msgfolderroot", "inbox", "drafts", "sentitems", "deleteditems", "junkemail", "outbox"];
    private const int MaxBusyRetries = 2;

    private readonly HttpClient _http;
    private readonly HttpMessageHandler _handler;

    public EwsClient(Uri ewsUrl, HttpMessageHandler handler, bool disposeHandler = true)
    {
        EwsUrl = ewsUrl;
        _handler = handler;
        _http = new HttpClient(handler, disposeHandler) { Timeout = TimeSpan.FromSeconds(100) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Postaci/1.0");
    }

    public Uri EwsUrl { get; }

    /// <summary>
    /// Mailbox sent as X-AnchorMailbox so a multi-server Exchange routes every request (and the streaming
    /// subscription in particular) to the server that holds the mailbox.
    /// </summary>
    public string? AnchorMailbox { get; set; }

    /// <summary>Creates a client that authenticates with NTLM/Kerberos (Negotiate) or Basic, whichever the server offers.</summary>
    public static EwsClient Create(Uri ewsUrl, ICredentials credentials) => new(ewsUrl, CreateHandler(credentials));

    public static HttpMessageHandler CreateHandler(ICredentials credentials) => new SocketsHttpHandler
    {
        Credentials = credentials,
        PreAuthenticate = true,
        AutomaticDecompression = DecompressionMethods.All,
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
    };

    public void Dispose()
    {
        _streamingHttp?.Dispose();
        _http.Dispose();
    }

    #region Folders

    /// <summary>Returns the mail folder tree under the mailbox root, well-known folders first.</summary>
    public async Task<IReadOnlyList<MailFolder>> GetFolderTreeAsync(CancellationToken ct = default)
    {
        var wellKnown = await GetWellKnownFolderIdsAsync(ct);
        if (!wellKnown.TryGetValue("msgfolderroot", out var rootId))
            throw new EwsException(Strings.Ews_MailboxRootNotFound);

        var request = M("FindFolder", new XAttribute("Traversal", "Deep"),
            M("FolderShape",
                T("BaseShape", "Default"),
                T("AdditionalProperties",
                    Field("folder:FolderClass"),
                    Field("folder:ParentFolderId"),
                    // PR_ATTR_HIDDEN: system folders that Outlook does not show.
                    T("ExtendedFieldURI", new XAttribute("PropertyTag", "0x10F4"), new XAttribute("PropertyType", "Boolean")))),
            M("ParentFolderIds", FolderIdElement(rootId)));

        var message = Single(await ExecuteAsync(request, ct));
        var folders = message.Element(MNs + "RootFolder")?.Element(TNs + "Folders")?.Elements(TNs + "Folder") ?? [];

        var byId = new Dictionary<string, MailFolder>();
        foreach (var f in folders)
        {
            var folderClass = (string?)f.Element(TNs + "FolderClass");
            if (folderClass is not null && !folderClass.StartsWith("IPF.Note", StringComparison.OrdinalIgnoreCase))
                continue;
            if (f.Elements(TNs + "ExtendedProperty").Any(p => (string?)p.Element(TNs + "Value") == "true"))
                continue;

            var id = (string)f.Element(TNs + "FolderId")!.Attribute("Id")!;
            byId[id] = new MailFolder
            {
                Id = id,
                ParentId = (string?)f.Element(TNs + "ParentFolderId")?.Attribute("Id"),
                DisplayName = (string?)f.Element(TNs + "DisplayName") ?? "",
                TotalCount = (int?)f.Element(TNs + "TotalCount") ?? 0,
                UnreadCount = (int?)f.Element(TNs + "UnreadCount") ?? 0,
            };
        }

        foreach (var (key, kind) in new[]
                 {
                     ("inbox", WellKnownFolder.Inbox), ("drafts", WellKnownFolder.Drafts), ("sentitems", WellKnownFolder.SentItems),
                     ("deleteditems", WellKnownFolder.DeletedItems), ("junkemail", WellKnownFolder.JunkEmail), ("outbox", WellKnownFolder.Outbox),
                 })
        {
            if (wellKnown.TryGetValue(key, out var id) && byId.TryGetValue(id, out var folder))
                folder.WellKnown = kind;
        }

        var roots = new List<MailFolder>();
        foreach (var folder in byId.Values)
        {
            if (folder.ParentId == rootId) roots.Add(folder);
            else if (folder.ParentId is not null && byId.TryGetValue(folder.ParentId, out var parent)) parent.Children.Add(folder);
            // Children of hidden/filtered folders are dropped on purpose.
        }

        Sort(roots);
        return roots;

        static void Sort(List<MailFolder> list)
        {
            list.Sort((a, b) =>
            {
                var wa = a.WellKnown == WellKnownFolder.None ? int.MaxValue : (int)a.WellKnown;
                var wb = b.WellKnown == WellKnownFolder.None ? int.MaxValue : (int)b.WellKnown;
                return wa != wb ? wa.CompareTo(wb) : string.Compare(a.DisplayName, b.DisplayName, StringComparison.CurrentCultureIgnoreCase);
            });
            foreach (var f in list) Sort(f.Children);
        }
    }

    /// <summary>Maps distinguished folder names (inbox, sentitems, ...) to folder ids. Missing folders are skipped.</summary>
    public async Task<IReadOnlyDictionary<string, string>> GetWellKnownFolderIdsAsync(CancellationToken ct = default)
    {
        var request = M("GetFolder",
            M("FolderShape", T("BaseShape", "IdOnly")),
            M("FolderIds", WellKnownIds.Select(DistinguishedFolder)));

        var response = await ExecuteAsync(request, ct);
        var result = new Dictionary<string, string>();
        var messages = ResponseMessages(response).ToList();
        for (var i = 0; i < messages.Count && i < WellKnownIds.Length; i++)
        {
            if ((string?)messages[i].Attribute("ResponseClass") == "Error") continue;
            var id = (string?)messages[i].Element(MNs + "Folders")?.Elements().FirstOrDefault()?.Element(TNs + "FolderId")?.Attribute("Id");
            if (id is not null) result[WellKnownIds[i]] = id;
        }
        if (result.Count == 0) Ok(messages.First());
        return result;
    }

    /// <summary>Returns (unread, total) counts for the given folders.</summary>
    public async Task<IReadOnlyDictionary<string, (int Unread, int Total)>> GetFolderCountsAsync(IEnumerable<string> folderIds, CancellationToken ct = default)
    {
        var ids = folderIds.ToList();
        var result = new Dictionary<string, (int, int)>();
        if (ids.Count == 0) return result;

        var request = M("GetFolder",
            M("FolderShape", T("BaseShape", "Default")),
            M("FolderIds", ids.Select(FolderIdElement)));

        foreach (var message in ResponseMessages(await ExecuteAsync(request, ct)))
        {
            if ((string?)message.Attribute("ResponseClass") == "Error") continue;
            var folder = message.Element(MNs + "Folders")?.Elements().FirstOrDefault();
            var id = (string?)folder?.Element(TNs + "FolderId")?.Attribute("Id");
            if (folder is null || id is null) continue;
            result[id] = ((int?)folder.Element(TNs + "UnreadCount") ?? 0, (int?)folder.Element(TNs + "TotalCount") ?? 0);
        }
        return result;
    }

    #endregion

    #region Items

    /// <summary>Lists messages newest first. <paramref name="query"/> is an AQS string (e.g. "from:ali rapor").</summary>
    public async Task<MailPage> FindItemsAsync(string folderId, int offset = 0, int pageSize = 50, string? query = null, CancellationToken ct = default)
    {
        var request = M("FindItem", new XAttribute("Traversal", "Shallow"),
            M("ItemShape", T("BaseShape", "IdOnly"), T("AdditionalProperties", SummaryFields())),
            M("IndexedPageItemView",
                new XAttribute("MaxEntriesReturned", pageSize),
                new XAttribute("Offset", offset),
                new XAttribute("BasePoint", "Beginning")),
            M("SortOrder", T("FieldOrder", new XAttribute("Order", "Descending"), Field("item:DateTimeReceived"))),
            M("ParentFolderIds", FolderIdElement(folderId)),
            string.IsNullOrWhiteSpace(query) ? null : M("QueryString", query.Trim()));

        var root = Single(await ExecuteAsync(request, ct)).Element(MNs + "RootFolder")
                   ?? throw new EwsException(Strings.Ews_RootFolderMissing);

        var items = root.Element(TNs + "Items")?.Elements().Select(ParseSummary).ToList() ?? [];
        var total = (int?)root.Attribute("TotalItemsInView") ?? items.Count;
        var next = (int?)root.Attribute("IndexedPagingOffset") ?? offset + items.Count;
        var isLast = (bool?)root.Attribute("IncludesLastItemInRange") ?? true;
        return new MailPage(items, total, next, isLast);
    }

    public async Task<MailDetail> GetItemAsync(EwsItemId id, CancellationToken ct = default)
    {
        var request = M("GetItem",
            M("ItemShape",
                T("BaseShape", "IdOnly"),
                T("BodyType", "HTML"),
                T("FilterHtmlContent", "true"),
                T("AdditionalProperties",
                    Field("item:Subject"),
                    Field("item:Body"),
                    Field("item:Attachments"),
                    Field("item:DateTimeSent"),
                    Field("item:DateTimeReceived"),
                    Field("item:Importance"),
                    Field("message:From"),
                    Field("message:ToRecipients"),
                    Field("message:CcRecipients"),
                    Field("message:BccRecipients"),
                    Field("message:IsRead"),
                    Field("item:ItemClass"))),
            M("ItemIds", ItemIdElement(id)));

        var item = Single(await ExecuteAsync(request, ct)).Element(MNs + "Items")?.Elements().FirstOrDefault()
                   ?? throw new EwsException(Strings.Ews_MessageNotFound, "ErrorItemNotFound");

        var body = item.Element(TNs + "Body");
        var attachments = item.Element(TNs + "Attachments")?.Elements().Select(a => new AttachmentInfo(
            Id: (string)a.Element(TNs + "AttachmentId")!.Attribute("Id")!,
            Name: (string?)a.Element(TNs + "Name") ?? "ek",
            ContentType: (string?)a.Element(TNs + "ContentType"),
            ContentId: (string?)a.Element(TNs + "ContentId"),
            Size: (long?)a.Element(TNs + "Size") ?? 0,
            IsInline: (bool?)a.Element(TNs + "IsInline") ?? false,
            IsItemAttachment: a.Name == TNs + "ItemAttachment")).ToList() ?? [];

        return new MailDetail(
            ParseItemId(item.Element(TNs + "ItemId")),
            (string?)item.Element(TNs + "Subject") ?? "",
            ParseMailbox(item.Element(TNs + "From")?.Element(TNs + "Mailbox")),
            ParseMailboxList(item.Element(TNs + "ToRecipients")),
            ParseMailboxList(item.Element(TNs + "CcRecipients")),
            ParseMailboxList(item.Element(TNs + "BccRecipients")),
            (DateTimeOffset?)item.Element(TNs + "DateTimeSent"),
            (DateTimeOffset?)item.Element(TNs + "DateTimeReceived"),
            body?.Value ?? "",
            !string.Equals((string?)body?.Attribute("BodyType"), "Text", StringComparison.OrdinalIgnoreCase),
            (bool?)item.Element(TNs + "IsRead") ?? true,
            ParseImportance(item.Element(TNs + "Importance")),
            attachments,
            (string?)item.Element(TNs + "ItemClass"));
    }

    /// <summary>Marks items read or unread and returns their new ids (change keys change on every update).</summary>
    public async Task<IReadOnlyList<EwsItemId>> SetReadStateAsync(IEnumerable<EwsItemId> ids, bool isRead, CancellationToken ct = default)
    {
        // Exchange requires a change key unless ConflictResolution is AlwaysOverwrite; the read flag never conflicts.
        var request = M("UpdateItem",
            new XAttribute("MessageDisposition", "SaveOnly"),
            new XAttribute("ConflictResolution", "AlwaysOverwrite"),
            new XAttribute("SuppressReadReceipts", "true"),
            M("ItemChanges", ids.Select(id => T("ItemChange",
                ItemIdElement(id with { ChangeKey = null }),
                T("Updates", T("SetItemField", Field("message:IsRead"), T("Message", T("IsRead", isRead ? "true" : "false"))))))));

        return ResponseMessages(await ExecuteAsync(request, ct))
            .Select(m => ParseItemId(Ok(m).Element(MNs + "Items")?.Elements().FirstOrDefault()?.Element(TNs + "ItemId")))
            .ToList();
    }

    public async Task DeleteItemsAsync(IEnumerable<EwsItemId> ids, DeleteMode mode = DeleteMode.MoveToDeletedItems, CancellationToken ct = default)
    {
        var request = M("DeleteItem", new XAttribute("DeleteType", mode.ToString()),
            M("ItemIds", ids.Select(id => ItemIdElement(id with { ChangeKey = null }))));
        foreach (var m in ResponseMessages(await ExecuteAsync(request, ct))) Ok(m);
    }

    public async Task MoveItemsAsync(IEnumerable<EwsItemId> ids, string targetFolderId, CancellationToken ct = default)
    {
        var request = M("MoveItem",
            M("ToFolderId", FolderIdElement(targetFolderId)),
            M("ItemIds", ids.Select(id => ItemIdElement(id with { ChangeKey = null }))));
        foreach (var m in ResponseMessages(await ExecuteAsync(request, ct))) Ok(m);
    }

    #endregion

    #region Attachments

    /// <summary>Downloads an attachment. Item attachments (attached e-mails) are returned as .eml MIME content.</summary>
    public async Task<AttachmentContent> GetAttachmentAsync(AttachmentInfo attachment, CancellationToken ct = default)
    {
        var request = M("GetAttachment",
            attachment.IsItemAttachment ? M("AttachmentShape", T("IncludeMimeContent", "true")) : null,
            M("AttachmentIds", T("AttachmentId", new XAttribute("Id", attachment.Id))));

        var el = Single(await ExecuteAsync(request, ct)).Element(MNs + "Attachments")?.Elements().FirstOrDefault()
                 ?? throw new EwsException(Strings.Ews_AttachmentNotFound, "ErrorAttachmentNotFound");

        if (el.Name == TNs + "ItemAttachment")
        {
            var mime = el.Elements().FirstOrDefault(e => e.Element(TNs + "MimeContent") is not null)?.Element(TNs + "MimeContent")?.Value
                       ?? throw new EwsException(Strings.Ews_AttachedMessageDownloadFailed);
            var name = (string?)el.Element(TNs + "Name") ?? "ileti";
            return new AttachmentContent(SafeFileName(name) + ".eml", "message/rfc822", Convert.FromBase64String(mime));
        }

        return new AttachmentContent(
            SafeFileName((string?)el.Element(TNs + "Name") ?? attachment.Name),
            (string?)el.Element(TNs + "ContentType") ?? attachment.ContentType,
            Convert.FromBase64String((string?)el.Element(TNs + "Content") ?? ""));
    }

    private static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(['\\', '/', ':', '*', '?', '"', '<', '>', '|']).ToHashSet();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return cleaned.Length == 0 ? "ek" : cleaned;
    }

    #endregion

    #region Sending

    /// <summary>Sends a new message, reply, reply-all or forward and saves a copy to Sent Items.</summary>
    public async Task SendAsync(OutgoingMessage message, CancellationToken ct = default)
    {
        Validate(message, requireRecipients: true);

        if (message.Mode == ComposeMode.New && message.ExistingDraftId is null && message.Attachments.Count == 0)
        {
            var create = M("CreateItem", new XAttribute("MessageDisposition", "SendAndSaveCopy"),
                M("SavedItemFolderId", DistinguishedFolder("sentitems")),
                M("Items", BuildMessage(message)));
            Single(await ExecuteAsync(create, ct));
            return;
        }

        // Replies and messages with attachments go through a draft so recipients and attachments can be set precisely.
        var draft = await SaveDraftAsync(message, ct);
        var send = M("SendItem", new XAttribute("SaveItemToFolder", "true"),
            M("ItemIds", ItemIdElement(draft)),
            M("SavedItemFolderId", DistinguishedFolder("sentitems")));
        Single(await ExecuteAsync(send, ct));
    }

    /// <summary>Saves the message to the Drafts folder and returns the draft's id.</summary>
    public async Task<EwsItemId> SaveDraftAsync(OutgoingMessage message, CancellationToken ct = default)
    {
        Validate(message, requireRecipients: false);
        EwsItemId id;

        if (message.ExistingDraftId is { } existing)
        {
            // Re-saving or sending a draft that already exists: overwrite it instead of creating a copy.
            id = await UpdateDraftHeadersAsync(existing, message, ct, includeBody: true);
            if (message.RemovedAttachmentIds.Count > 0)
            {
                await DeleteAttachmentsAsync(message.RemovedAttachmentIds, ct);
                id = await GetCurrentItemIdAsync(id, ct);
            }
        }
        else if (message.Mode == ComposeMode.New)
        {
            id = await CreateDraftAsync(BuildMessage(message), ct);
        }
        else
        {
            var reference = await GetCurrentItemIdAsync(message.ReferenceItemId!, ct);
            var responseName = message.Mode switch
            {
                ComposeMode.Reply => "ReplyToItem",
                ComposeMode.ReplyAll => "ReplyAllToItem",
                _ => "ForwardItem",
            };
            // Exchange fills in the subject prefix, default recipients and the quoted original.
            var response = T(responseName,
                ItemIdElement(reference, "ReferenceItemId"),
                T("NewBodyContent", new XAttribute("BodyType", "HTML"), message.HtmlBody));
            id = await CreateDraftAsync(response, ct);
            id = await UpdateDraftHeadersAsync(id, message, ct);
        }

        if (message.Attachments.Count > 0)
            id = await CreateAttachmentsAsync(id, message.Attachments, ct);

        return id;
    }

    private static void Validate(OutgoingMessage message, bool requireRecipients)
    {
        if (message.Mode != ComposeMode.New && message.ReferenceItemId is null && message.ExistingDraftId is null)
            throw new ArgumentException(Strings.Ews_ReferenceItemRequired, nameof(message));
        if (requireRecipients && message.To.Count + message.Cc.Count + message.Bcc.Count == 0)
            throw new ArgumentException(Strings.Ews_RecipientRequired, nameof(message));
    }

    private static XElement BuildMessage(OutgoingMessage message) =>
        // Element order follows the EWS schema (ItemType fields, then MessageType fields).
        T("Message",
            T("Subject", message.Subject),
            T("Body", new XAttribute("BodyType", "HTML"), message.HtmlBody),
            T("Importance", message.Importance.ToString()),
            Recipients("ToRecipients", message.To),
            Recipients("CcRecipients", message.Cc),
            Recipients("BccRecipients", message.Bcc));

    private static XElement? Recipients(string name, IList<string> addresses) =>
        addresses.Count == 0 ? null : T(name, addresses.Select(MailboxElement));

    private async Task<EwsItemId> CreateDraftAsync(XElement item, CancellationToken ct)
    {
        var create = M("CreateItem", new XAttribute("MessageDisposition", "SaveOnly"),
            M("SavedItemFolderId", DistinguishedFolder("drafts")),
            M("Items", item));
        var message = Single(await ExecuteAsync(create, ct));
        return ParseItemId(message.Element(MNs + "Items")?.Elements().FirstOrDefault()?.Element(TNs + "ItemId"));
    }

    private async Task<EwsItemId> GetCurrentItemIdAsync(EwsItemId id, CancellationToken ct)
    {
        var request = M("GetItem", M("ItemShape", T("BaseShape", "IdOnly")), M("ItemIds", ItemIdElement(id with { ChangeKey = null })));
        var message = Single(await ExecuteAsync(request, ct));
        return ParseItemId(message.Element(MNs + "Items")?.Elements().FirstOrDefault()?.Element(TNs + "ItemId"));
    }

    private async Task<EwsItemId> UpdateDraftHeadersAsync(EwsItemId draft, OutgoingMessage message, CancellationToken ct, bool includeBody = false)
    {
        var updates = new List<XElement>
        {
            SetField("item:Subject", T("Subject", message.Subject)),
            SetField("item:Importance", T("Importance", message.Importance.ToString())),
        };
        if (includeBody) updates.Add(SetField("item:Body", T("Body", new XAttribute("BodyType", "HTML"), message.HtmlBody)));

        foreach (var (uri, name, list) in new[]
                 {
                     ("message:ToRecipients", "ToRecipients", message.To),
                     ("message:CcRecipients", "CcRecipients", message.Cc),
                     ("message:BccRecipients", "BccRecipients", message.Bcc),
                 })
        {
            if (list.Count > 0)
                updates.Add(SetField(uri, T(name, list.Select(MailboxElement))));
            else if (includeBody || (name != "BccRecipients" && message.Mode is ComposeMode.Reply or ComposeMode.ReplyAll))
                updates.Add(T("DeleteItemField", Field(uri))); // the user removed recipients that were already on the draft
        }

        var request = M("UpdateItem",
            new XAttribute("MessageDisposition", "SaveOnly"),
            new XAttribute("ConflictResolution", "AlwaysOverwrite"),
            M("ItemChanges", T("ItemChange", ItemIdElement(draft), T("Updates", updates))));

        var result = Single(await ExecuteAsync(request, ct));
        return ParseItemId(result.Element(MNs + "Items")?.Elements().FirstOrDefault()?.Element(TNs + "ItemId"));

        static XElement SetField(string uri, XElement value) => T("SetItemField", Field(uri), T("Message", value));
    }

    private async Task DeleteAttachmentsAsync(IEnumerable<string> attachmentIds, CancellationToken ct)
    {
        var request = M("DeleteAttachment",
            M("AttachmentIds", attachmentIds.Select(id => T("AttachmentId", new XAttribute("Id", id)))));
        foreach (var m in ResponseMessages(await ExecuteAsync(request, ct))) Ok(m);
    }

    private async Task<EwsItemId> CreateAttachmentsAsync(EwsItemId parent, IEnumerable<OutgoingAttachment> attachments, CancellationToken ct)
    {
        var request = M("CreateAttachment",
            M("ParentItemId", new XAttribute("Id", parent.Id), parent.ChangeKey is null ? null : new XAttribute("ChangeKey", parent.ChangeKey)),
            M("Attachments", attachments.Select(a => T("FileAttachment",
                T("Name", a.Name),
                T("ContentType", a.ContentType),
                T("Content", Convert.ToBase64String(a.Content))))));

        var latest = parent;
        foreach (var m in ResponseMessages(await ExecuteAsync(request, ct)))
        {
            var attachmentId = Ok(m).Element(MNs + "Attachments")?.Elements().FirstOrDefault()?.Element(TNs + "AttachmentId");
            var changeKey = (string?)attachmentId?.Attribute("RootItemChangeKey");
            if (changeKey is not null) latest = new EwsItemId((string?)attachmentId!.Attribute("RootItemId") ?? parent.Id, changeKey);
        }
        return latest;
    }

    #endregion

    #region Directory

    /// <summary>Searches the global address list and contacts for recipients matching <paramref name="text"/>.</summary>
    public async Task<IReadOnlyList<MailboxAddress>> ResolveNamesAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];

        var request = M("ResolveNames",
            new XAttribute("ReturnFullContactData", "false"),
            new XAttribute("SearchScope", "ActiveDirectoryContacts"),
            M("UnresolvedEntry", text.Trim()));

        var message = ResponseMessages(await ExecuteAsync(request, ct)).FirstOrDefault();
        if (message is null || (string?)message.Element(MNs + "ResponseCode") == "ErrorNameResolutionNoResults")
            return [];
        if ((string?)message.Attribute("ResponseClass") == "Error") Ok(message);

        return message.Element(MNs + "ResolutionSet")?.Elements(TNs + "Resolution")
                   .Select(r => ParseMailbox(r.Element(TNs + "Mailbox")))
                   .Where(m => m is not null && m.Address.Contains('@'))
                   .Cast<MailboxAddress>()
                   .ToList() ?? [];
    }

    #endregion

    #region SOAP plumbing

    /// <summary>Windows time zone id sent with calendar requests so all-day events and views use local time.</summary>
    public string? TimeZoneId { get; set; } = OperatingSystem.IsWindows() ? TimeZoneInfo.Local.Id : null;

    private async Task<XElement> ExecuteAsync(XElement operation, CancellationToken ct, bool withTimeZone = false)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await SendSoapAsync(operation, ct, withTimeZone);
            }
            catch (EwsServerBusyException busy) when (attempt < MaxBusyRetries)
            {
                await Task.Delay(busy.BackOff, ct);
            }
        }
    }

    /// <summary>A POST of <paramref name="operation"/> wrapped in a SOAP envelope, with the routing header.</summary>
    private HttpRequestMessage BuildRequest(XElement operation, bool withTimeZone)
    {
        var envelope = new XElement(Soap + "Envelope",
            new XAttribute(XNamespace.Xmlns + "soap", Soap),
            new XAttribute(XNamespace.Xmlns + "t", TNs),
            new XAttribute(XNamespace.Xmlns + "m", MNs),
            new XElement(Soap + "Header",
                T("RequestServerVersion", new XAttribute("Version", ServerVersion)),
                withTimeZone && TimeZoneId is not null ? T("TimeZoneContext", T("TimeZoneDefinition", new XAttribute("Id", TimeZoneId))) : null),
            new XElement(Soap + "Body", operation));

        var request = new HttpRequestMessage(HttpMethod.Post, EwsUrl)
        {
            Content = new StringContent(envelope.ToString(SaveOptions.DisableFormatting), Encoding.UTF8, "text/xml"),
        };
        if (!string.IsNullOrEmpty(AnchorMailbox)) request.Headers.TryAddWithoutValidation("X-AnchorMailbox", AnchorMailbox);
        return request;
    }

    private async Task<XElement> SendSoapAsync(XElement operation, CancellationToken ct, bool withTimeZone)
    {
        using var request = BuildRequest(operation, withTimeZone);
        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new EwsException(Loc.Format(Strings.Ews_ServerUnreachable, ex.Message), null, ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new EwsException(Strings.Ews_Timeout, null, ex);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new EwsAuthenticationException(Strings.Ews_AuthFailed);

            XDocument doc;
            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                doc = await XDocument.LoadAsync(stream, LoadOptions.None, ct);
            }
            catch (XmlException ex)
            {
                throw new EwsException(Loc.Format(Strings.Ews_UnexpectedResponse, (int)response.StatusCode, response.ReasonPhrase), null, ex);
            }

            ThrowIfFault(doc);
            if (!response.IsSuccessStatusCode)
                throw new EwsException($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
            return BodyOf(doc);
        }
    }

    private static void ThrowIfFault(XDocument doc)
    {
        var fault = doc.Descendants(Soap + "Fault").FirstOrDefault();
        if (fault is null) return;
        // Fault details use the .../2006/errors namespace, so match on local names.
        var code = (string?)fault.Descendants().FirstOrDefault(e => e.Name.LocalName == "ResponseCode");
        var text = (string?)fault.Descendants().FirstOrDefault(e => e.Name.LocalName == "Message")
                   ?? (string?)fault.Element("faultstring") ?? Strings.Ews_SoapFault;
        if (code == "ErrorServerBusy") throw new EwsServerBusyException(text, ParseBackOff(fault));
        throw new EwsException(text, code);
    }

    private static XElement BodyOf(XDocument doc) =>
        doc.Root?.Element(Soap + "Body")?.Elements().FirstOrDefault() ?? throw new EwsException(Strings.Ews_EmptyResponse);

    private static TimeSpan ParseBackOff(XElement fault)
    {
        var value = fault.Descendants().FirstOrDefault(e => e.Name.LocalName == "Value" && (string?)e.Attribute("Name") == "BackOffMilliseconds");
        var ms = int.TryParse((string?)value, out var parsed) ? parsed : 2000;
        return TimeSpan.FromMilliseconds(Math.Clamp(ms, 500, 30_000));
    }

    private static IEnumerable<XElement> ResponseMessages(XElement response) =>
        response.Element(MNs + "ResponseMessages")?.Elements() ?? [];

    private static XElement Single(XElement response) =>
        Ok(ResponseMessages(response).FirstOrDefault() ?? throw new EwsException(Strings.Ews_NoResponseMessage));

    private static XElement Ok(XElement message)
    {
        if ((string?)message.Attribute("ResponseClass") != "Error") return message;
        var code = (string?)message.Element(MNs + "ResponseCode");
        var text = (string?)message.Element(MNs + "MessageText") ?? code ?? Strings.Ews_UnknownError;
        throw new EwsException(text, code);
    }

    /// <summary>The fields <see cref="ParseSummary"/> reads.</summary>
    private static XElement[] SummaryFields() =>
    [
        Field("item:Subject"), Field("message:From"), Field("item:DisplayTo"), Field("item:DateTimeReceived"),
        Field("message:IsRead"), Field("item:HasAttachments"), Field("item:Importance"), Field("item:Preview"),
        Field("item:ItemClass"),
    ];

    private static MailSummary ParseSummary(XElement item) => new(
        ParseItemId(item.Element(TNs + "ItemId")),
        (string?)item.Element(TNs + "Subject") ?? "",
        ParseMailbox(item.Element(TNs + "From")?.Element(TNs + "Mailbox")),
        (string?)item.Element(TNs + "DisplayTo") ?? "",
        (DateTimeOffset?)item.Element(TNs + "DateTimeReceived") ?? DateTimeOffset.MinValue,
        (bool?)item.Element(TNs + "IsRead") ?? true,
        (bool?)item.Element(TNs + "HasAttachments") ?? false,
        ParseImportance(item.Element(TNs + "Importance")),
        ((string?)item.Element(TNs + "Preview") ?? "").Trim(),
        (string?)item.Element(TNs + "ItemClass") ?? "IPM.Note");

    private sealed class EwsServerBusyException(string message, TimeSpan backOff) : EwsException(message, "ErrorServerBusy")
    {
        public TimeSpan BackOff { get; } = backOff;
    }

    #endregion
}
