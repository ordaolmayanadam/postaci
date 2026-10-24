using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExchangeMail.Core.Ews;
using ExchangeMail.Core.Models;

namespace ExchangeMail.ViewModels;

public sealed partial class ComposeViewModel : ObservableObject
{
    private static readonly string[] ReplyPrefixes = ["YNT:", "RE:", "AW:", "SV:"];
    private static readonly string[] ForwardPrefixes = ["İLT:", "ILT:", "FW:", "FWD:", "WG:"];

    private readonly MailSession _session;
    private readonly EwsItemId? _referenceId;
    private readonly List<string> _removedAttachmentIds = [];

    /// <summary>The draft this window writes to, once it exists on the server.</summary>
    private EwsItemId? _draftId;

    public ComposeViewModel(MailSession session, ComposeMode mode, MailDetail? original)
    {
        if (mode != ComposeMode.New && original is null)
            throw new ArgumentNullException(nameof(original), "Replies and forwards need the original message.");

        _session = session;
        Mode = mode;
        Attachments.CollectionChanged += (_, _) => OnPropertyChanged(nameof(AttachmentsSizeText));
        ExistingAttachments.CollectionChanged += (_, _) => OnPropertyChanged(nameof(AttachmentsSizeText));
        _referenceId = original?.ItemId;

        var self = session.Account.Email;
        bool NotSelf(MailboxAddress m) => !string.Equals(m.Address, self, StringComparison.OrdinalIgnoreCase);

        switch (mode)
        {
            case ComposeMode.Reply:
                _to = Join(original!.From is null ? [] : [original.From]);
                _subject = Prefixed(Strings.Compose_ReplyPrefix, original.Subject, ReplyPrefixes);
                break;
            case ComposeMode.ReplyAll:
                var to = (original!.From is null ? [] : new[] { original.From }).Concat(original.To.Where(NotSelf));
                _to = Join(to.DistinctBy(m => m.Address.ToLowerInvariant()));
                _cc = Join(original.Cc.Where(NotSelf));
                _subject = Prefixed(Strings.Compose_ReplyPrefix, original.Subject, ReplyPrefixes);
                break;
            case ComposeMode.Forward:
                _subject = Prefixed(Strings.Compose_ForwardPrefix, original!.Subject, ForwardPrefixes);
                break;
        }
        OriginalSummary = original is null ? "" : $"{original.From?.Display} · {MailFormat.LongDate(original.Received ?? original.Sent)} · {original.Subject}";

        // Two empty paragraphs to type in, then the signature.
        var signature = session.Preferences.GetSignatureHtml();
        InitialHtml = "<p><br></p>" + (signature.Length > 0 ? "<p><br></p>" + signature : "");
    }

    /// <summary>Opens an existing message from the Drafts folder for editing.</summary>
    public static ComposeViewModel ForDraft(MailSession session, MailDetail draft)
    {
        var vm = new ComposeViewModel(session, ComposeMode.New, null)
        {
            IsEditingDraft = true,
            To = Join(draft.To),
            Cc = Join(draft.Cc),
            Bcc = Join(draft.Bcc),
            ShowBcc = draft.Bcc.Count > 0,
            Subject = draft.Subject,
            IsHighImportance = draft.Importance == Importance.High,
            InitialHtml = draft.IsHtml ? MailFormat.ExtractBodyHtml(draft.Body) : MailFormat.PlainTextToHtml(draft.Body),
        };
        vm._draftId = draft.ItemId;
        foreach (var a in draft.Attachments.Where(a => !a.IsInline)) vm.ExistingAttachments.Add(a);
        return vm;
    }

    public ComposeMode Mode { get; }

    public bool IsEditingDraft { get; private init; }

    public string WindowTitle => (IsEditingDraft ? Strings.Compose_TitleDraft : Mode switch
    {
        ComposeMode.Reply => Strings.Compose_TitleReply,
        ComposeMode.ReplyAll => Strings.Compose_TitleReplyAll,
        ComposeMode.Forward => Strings.Compose_TitleForward,
        _ => Strings.Compose_TitleNew,
    }) + (string.IsNullOrWhiteSpace(Subject) ? "" : " – " + Subject);

    /// <summary>A reply/forward whose quoted original Exchange has not added to the body yet.</summary>
    public bool IsResponse => Mode != ComposeMode.New && _draftId is null;
    public string OriginalSummary { get; }

    /// <summary>HTML the editor starts with (signature, or the draft's body).</summary>
    public string InitialHtml { get; private init; }

    /// <summary>Reads the editor's current HTML; supplied by the view.</summary>
    public Func<Task<string>>? ReadBodyHtmlAsync { get; set; }

    [ObservableProperty] private string _to = "";
    [ObservableProperty] private string _cc = "";
    [ObservableProperty] private string _bcc = "";
    [ObservableProperty] private bool _showBcc;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    private string _subject = "";

    [ObservableProperty] private bool _isHighImportance;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand), nameof(SaveDraftCommand))]
    private bool _isBusy;

    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _infoMessage;

    /// <summary>New files to attach.</summary>
    public ObservableCollection<OutgoingAttachment> Attachments { get; } = [];

    /// <summary>Attachments already on the draft (editing a draft, or after saving).</summary>
    public ObservableCollection<AttachmentInfo> ExistingAttachments { get; } = [];

    /// <summary>True when attachments were added or removed since the last save.</summary>
    public bool HasAttachmentChanges => Attachments.Count > 0 || _removedAttachmentIds.Count > 0;

    /// <summary>Asks the user a yes/no question; supplied by the view.</summary>
    public Func<string, string, Task<bool>>? ConfirmAsync { get; set; }

    /// <summary>Raised after the message was sent; the view closes itself.</summary>
    public event EventHandler? Sent;

    /// <summary>Raised after a successful save; the view resets its "unsaved changes" baseline.</summary>
    public event EventHandler? DraftSaved;

    /// <summary>Raised when the body must be replaced (the server added the quoted original to a reply draft).</summary>
    public event EventHandler<string>? BodyReplaced;

    public void AddAttachment(string fileName, byte[] content) =>
        Attachments.Add(new OutgoingAttachment(Path.GetFileName(fileName), MailFormat.GuessContentType(fileName), content));

    [RelayCommand]
    private void RemoveAttachment(OutgoingAttachment? attachment)
    {
        if (attachment is not null) Attachments.Remove(attachment);
    }

    [RelayCommand]
    private void RemoveExistingAttachment(AttachmentInfo? attachment)
    {
        if (attachment is null) return;
        ExistingAttachments.Remove(attachment);
        _removedAttachmentIds.Add(attachment.Id);
    }

    public string AttachmentsSizeText => MailFormat.FileSize(Attachments.Sum(a => (long)a.Content.Length) + ExistingAttachments.Sum(a => a.Size));

    /// <summary>Recipient suggestions from the global address list for the token being typed.</summary>
    public async Task<IReadOnlyList<MailboxAddress>> SuggestRecipientsAsync(string fieldText, CancellationToken ct)
    {
        var token = RecipientParser.LastToken(fieldText);
        if (token.Length < 2 || token.Contains('@')) return [];
        try
        {
            return await _session.Client.ResolveNamesAsync(token, ct);
        }
        catch (EwsException)
        {
            return [];
        }
    }

    private bool CanSubmit() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanSubmit))]
    private async Task SendAsync()
    {
        var message = await BuildMessageAsync(requireRecipients: true);
        if (message is null) return;

        if (string.IsNullOrWhiteSpace(Subject) && ConfirmAsync is not null
            && !await ConfirmAsync(Strings.Compose_NoSubjectTitle, Strings.Compose_NoSubjectQuestion))
            return;

        await RunAsync(async () =>
        {
            await _session.Client.SendAsync(message);
            Sent?.Invoke(this, EventArgs.Empty);
        });
    }

    [RelayCommand(CanExecute = nameof(CanSubmit))]
    private async Task SaveDraftAsync()
    {
        var message = await BuildMessageAsync(requireRecipients: false);
        if (message is null) return;
        await RunAsync(async () =>
        {
            var wasResponse = IsResponse;
            _draftId = await _session.Client.SaveDraftAsync(message);

            // Reload what the server now holds: saved attachments (and, for a forward, the original's attachments)
            // and, the first time a reply is saved, the body including the quoted original.
            var saved = await _session.Client.GetItemAsync(_draftId);
            _draftId = saved.ItemId;
            Attachments.Clear();
            _removedAttachmentIds.Clear();
            ExistingAttachments.Clear();
            foreach (var a in saved.Attachments.Where(a => !a.IsInline)) ExistingAttachments.Add(a);
            OnPropertyChanged(nameof(AttachmentsSizeText));
            if (wasResponse)
            {
                OnPropertyChanged(nameof(IsResponse));
                BodyReplaced?.Invoke(this, saved.IsHtml ? MailFormat.ExtractBodyHtml(saved.Body) : MailFormat.PlainTextToHtml(saved.Body));
            }

            InfoMessage = Loc.Format(Strings.Compose_DraftSavedAt, DateTime.Now.ToString("t", System.Globalization.CultureInfo.CurrentCulture));
            DraftSaved?.Invoke(this, EventArgs.Empty);
        });
    }

    private async Task<OutgoingMessage?> BuildMessageAsync(bool requireRecipients)
    {
        ErrorMessage = null;
        InfoMessage = null;
        var to = RecipientParser.Parse(To, out var badTo);
        var cc = RecipientParser.Parse(Cc, out var badCc);
        var bcc = RecipientParser.Parse(Bcc, out var badBcc);
        var invalid = badTo.Concat(badCc).Concat(badBcc).ToList();
        if (invalid.Count > 0)
        {
            ErrorMessage = Loc.Format(Strings.Compose_InvalidAddresses, string.Join(", ", invalid));
            return null;
        }
        if (requireRecipients && to.Count + cc.Count + bcc.Count == 0)
        {
            ErrorMessage = Strings.Compose_RecipientRequired;
            return null;
        }

        var html = ReadBodyHtmlAsync is null ? InitialHtml : await ReadBodyHtmlAsync();
        return new OutgoingMessage
        {
            Mode = Mode,
            ReferenceItemId = _referenceId,
            ExistingDraftId = _draftId,
            RemovedAttachmentIds = _removedAttachmentIds.ToList(),
            To = to,
            Cc = cc,
            Bcc = bcc,
            Subject = Subject.Trim(),
            HtmlBody = _draftId is null
                ? MailFormat.WrapComposedHtml(html, _session.Preferences.ComposeFontFamily, _session.Preferences.ComposeFontSize)
                : html,
            Importance = IsHighImportance ? Importance.High : Importance.Normal,
            Attachments = Attachments.ToList(),
        };
    }

    private async Task RunAsync(Func<Task> action)
    {
        IsBusy = true;
        try
        {
            await action();
        }
        catch (Exception ex) when (ex is EwsException or ArgumentException)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string Join(IEnumerable<MailboxAddress> addresses)
    {
        var text = string.Join("; ", addresses.Where(a => a.Address.Length > 0).Select(Format));
        return text.Length == 0 ? "" : text + "; ";
    }

    // Names such as "Yılmaz, Ali" are quoted so the separator inside them is not treated as a recipient boundary.
    private static string Format(MailboxAddress m) =>
        string.IsNullOrWhiteSpace(m.Name) || m.Name == m.Address ? m.Address
        : m.Name.IndexOfAny([',', ';']) >= 0 ? $"\"{m.Name.Replace("\"", "")}\" <{m.Address}>"
        : $"{m.Name} <{m.Address}>";

    private static string Prefixed(string prefix, string subject, string[] knownPrefixes)
    {
        var trimmed = subject.TrimStart();
        return knownPrefixes.Any(p => trimmed.StartsWith(p, StringComparison.OrdinalIgnoreCase)) ? trimmed : prefix + trimmed;
    }
}
