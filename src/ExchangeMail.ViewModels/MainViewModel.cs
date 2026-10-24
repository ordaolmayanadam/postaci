using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExchangeMail.Core.Ews;
using ExchangeMail.Core.Models;

namespace ExchangeMail.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private const int PageSize = 50;

    private readonly MailSession _session;
    private CancellationTokenSource? _listCts;
    private CancellationTokenSource? _detailCts;
    private int _nextOffset;
    private bool _backgroundRefreshRunning;
    private bool _pollAgain;

    /// <summary>Rows selected in the list (Ctrl/Shift-click); <see cref="SelectedMessage"/> is set only when it is one.</summary>
    private IReadOnlyList<MessageItemViewModel> _selection = [];
    private bool _settingSelection;

    public MainViewModel(MailSession session)
    {
        _session = session;
        _isConversationView = session.Preferences.ConversationView;
        Messages.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsListEmpty));
    }

    public string AccountEmail => _session.Account.Email;
    public string ServerHost => _session.Client.EwsUrl.Host;
    public EwsClient Client => _session.Client;

    public ObservableCollection<FolderViewModel> Folders { get; } = [];

    private FolderViewModel? Inbox => Folders.FirstOrDefault(f => f.WellKnown == WellKnownFolder.Inbox);

    /// <summary>Unread mail in the inbox, for the taskbar badge.</summary>
    public int InboxUnreadCount => Inbox?.UnreadCount ?? 0;

    /// <summary>Ids already seen in the inbox; null until the inbox was first loaded.</summary>
    private HashSet<string>? _knownInboxIds;

    /// <summary>Raised by the background poll for unread mail that arrived in the inbox since the last poll.</summary>
    public event EventHandler<IReadOnlyList<MailSummary>>? NewMailArrived;
    public ObservableCollection<MessageItemViewModel> Messages { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDraftsFolder))]
    [NotifyCanExecuteChangedFor(nameof(EditDraftCommand))]
    private FolderViewModel? _selectedFolder;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RespondToMeetingCommand))]
    [NotifyPropertyChangedFor(nameof(IsMeetingRequest))]
    private MessageItemViewModel? _selectedMessage;

    public IReadOnlyList<MessageItemViewModel> SelectedItems => _selection;
    public bool HasSelection => _selection.Count > 0;
    public bool IsMultiSelect => _selection.Count > 1;

    /// <summary>Text of the empty reading pane.</summary>
    public string PlaceholderText => IsMultiSelect ? Loc.Format(Strings.Mail_ItemsSelected, _selection.Count) : Strings.Mail_SelectItem;

    /// <summary>Called by the view when the list selection changes.</summary>
    public void SetSelection(IReadOnlyList<MessageItemViewModel> items)
    {
        _settingSelection = true;
        try
        {
            _selection = items.ToList();
            SelectedMessage = _selection.Count == 1 ? _selection[0] : null;
        }
        finally
        {
            _settingSelection = false;
        }
        OnSelectionChanged();
    }

    private void OnSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedItems));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(IsMultiSelect));
        OnPropertyChanged(nameof(PlaceholderText));
        DeleteCommand.NotifyCanExecuteChanged();
        ToggleReadCommand.NotifyCanExecuteChanged();
        MoveToCommand.NotifyCanExecuteChanged();
    }

    /// <summary>The list shows conversations instead of messages (saved in the preferences).</summary>
    [ObservableProperty]
    private bool _isConversationView;

    partial void OnIsConversationViewChanged(bool value)
    {
        _session.Preferences.ConversationView = value;
        _ = LoadMessagesAsync(reset: true);
    }

    /// <summary>
    /// Conversations are listed unless searching (results are messages) or in Drafts/Outbox, whose items are edited
    /// one by one.
    /// </summary>
    private bool UsesConversations =>
        IsConversationView && !IsSearchActive && SelectedFolder?.WellKnown is not (WellKnownFolder.Drafts or WellKnownFolder.Outbox);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCurrent), nameof(ShowPlaceholder))]
    [NotifyCanExecuteChangedFor(nameof(ReplyCommand), nameof(ReplyAllCommand), nameof(ForwardCommand), nameof(RespondToMeetingCommand), nameof(EditDraftCommand))]
    [NotifyPropertyChangedFor(nameof(IsMeetingRequest))]
    private MessageDetailViewModel? _current;

    /// <summary>The open message is a meeting invitation that can be accepted or declined.</summary>
    public bool IsMeetingRequest => Current is not null
        && (Current.Detail.ItemClass ?? SelectedMessage?.Summary.ItemClass)?.StartsWith("IPM.Schedule.Meeting.Request", StringComparison.OrdinalIgnoreCase) == true;

    public bool HasCurrent => Current is not null;
    public bool ShowPlaceholder => Current is null && !IsLoadingMessage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadMoreCommand))]
    private bool _hasMore;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadMoreCommand))]
    [NotifyPropertyChangedFor(nameof(IsListEmpty))]
    private bool _isLoadingList;

    public bool IsListEmpty => Messages.Count == 0 && !IsLoadingList;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPlaceholder))]
    private bool _isLoadingMessage;

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSearchActive))]
    private string? _activeQuery;

    public bool IsSearchActive => !string.IsNullOrEmpty(ActiveQuery);

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>Asks the user a yes/no question; supplied by the view.</summary>
    public Func<string, string, Task<bool>>? ConfirmAsync { get; set; }

    public event EventHandler<ComposeViewModel>? ComposeRequested;

    public async Task InitializeAsync()
    {
        await LoadFoldersAsync();
        SelectedFolder ??= Folders.FirstOrDefault(f => f.WellKnown == WellKnownFolder.Inbox) ?? Folders.FirstOrDefault();
    }

    private async Task LoadFoldersAsync()
    {
        try
        {
            var tree = await Client.GetFolderTreeAsync();
            var selectedId = SelectedFolder?.Id;
            Folders.Clear();
            foreach (var f in FolderViewModel.Flatten(tree)) Folders.Add(f);
            if (Inbox is { } inbox)
            {
                inbox.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(FolderViewModel.UnreadCount)) OnPropertyChanged(nameof(InboxUnreadCount));
                };
            }
            OnPropertyChanged(nameof(InboxUnreadCount));
            if (selectedId is not null)
                SelectedFolder = Folders.FirstOrDefault(f => f.Id == selectedId);
        }
        catch (EwsException ex)
        {
            ErrorMessage = Loc.Format(Strings.Mail_FoldersLoadFailed, ex.Message);
        }
    }

    partial void OnSelectedFolderChanged(FolderViewModel? value)
    {
        SearchText = "";
        ActiveQuery = null;
        _ = LoadMessagesAsync(reset: true);
    }

    partial void OnSelectedMessageChanged(MessageItemViewModel? value)
    {
        // Set directly (after a delete, from a notification): the selection becomes that one row.
        if (!_settingSelection)
        {
            _selection = value is null ? [] : [value];
            OnSelectionChanged();
        }
        _ = LoadDetailAsync(value);
    }

    private async Task LoadMessagesAsync(bool reset)
    {
        var folder = SelectedFolder;
        _listCts?.Cancel();
        if (folder is null)
        {
            Messages.Clear();
            return;
        }

        var cts = _listCts = new CancellationTokenSource();
        IsLoadingList = true;
        ErrorMessage = null;
        try
        {
            if (UsesConversations)
            {
                await LoadConversationsAsync(folder, reset, cts.Token);
                return;
            }

            var page = await Client.FindItemsAsync(folder.Id, reset ? 0 : _nextOffset, PageSize, ActiveQuery, cts.Token);
            if (cts.IsCancellationRequested) return;

            if (reset)
            {
                SelectedMessage = null;
                Messages.Clear();
            }
            foreach (var item in page.Items) Messages.Add(new MessageItemViewModel(item, folder.ShowsRecipients));
            if (reset && folder.WellKnown == WellKnownFolder.Inbox && !IsSearchActive)
                _knownInboxIds ??= page.Items.Select(i => i.ItemId.Id).ToHashSet();
            _nextOffset = page.NextOffset;
            HasMore = !page.IsLastPage;
            StatusText = IsSearchActive
                ? Loc.Count(page.TotalCount, Strings.Common_ResultCount_One, Strings.Common_ResultCount)
                : Loc.Count(page.TotalCount, Strings.Mail_ItemCount_One, Strings.Mail_ItemCount);
        }
        catch (OperationCanceledException)
        {
        }
        catch (EwsException ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            if (_listCts == cts) IsLoadingList = false;
        }
    }

    private async Task LoadConversationsAsync(FolderViewModel folder, bool reset, CancellationToken ct)
    {
        var page = await Client.FindConversationsAsync(folder.Id, reset ? 0 : _nextOffset, PageSize, ct);
        if (ct.IsCancellationRequested) return;
        if (reset)
        {
            SelectedMessage = null;
            Messages.Clear();
        }
        foreach (var c in page.Items) Messages.Add(MessageItemViewModel.ForConversation(c, folder.ShowsRecipients));
        _nextOffset = page.NextOffset;
        HasMore = !page.IsLastPage;
        StatusText = Loc.Count(page.TotalCount, Strings.Mail_ConversationCount_One, Strings.Mail_ConversationCount);
    }

    /// <summary>
    /// For a conversation: all its messages (including sent replies) for the reading pane, and the message replies go
    /// to — the newest one from someone else, as in Outlook, or simply the newest when every message is ours.
    /// </summary>
    private async Task<(EwsItemId Id, IReadOnlyList<ThreadMessage>? Thread)> ResolveConversationAsync(MessageItemViewModel item, CancellationToken ct)
    {
        if (item.Conversation is not { GlobalMessageCount: > 1 } conversation) return (item.Id, null);
        var thread = await Client.GetConversationItemsAsync(conversation.ConversationId, ct);
        if (thread.Count == 0) return (item.Id, null);
        var self = _session.Account.Email;
        var target = thread.FirstOrDefault(m => !string.Equals(m.From?.Address, self, StringComparison.OrdinalIgnoreCase)) ?? thread[0];
        return (target.ItemId, thread);
    }

    private async Task LoadDetailAsync(MessageItemViewModel? item)
    {
        _detailCts?.Cancel();
        if (item is null)
        {
            Current = null;
            return;
        }

        var cts = _detailCts = new CancellationTokenSource();
        var ct = cts.Token;
        IsLoadingMessage = true;
        try
        {
            var preferences = _session.Preferences;
            if (!item.IsRead && preferences.MarkAsRead == MarkAsReadMode.WhenSelected)
                await TryMarkReadAsync(item, ct);

            var (id, thread) = await ResolveConversationAsync(item, ct);
            var detail = new MessageDetailViewModel(await Client.GetItemAsync(id, ct), preferences.AlwaysShowRemoteImages, thread);
            await detail.LoadInlineImagesAsync(Client, ct);
            if (ct.IsCancellationRequested) return;
            Current = detail;

            if (!item.IsRead && preferences.MarkAsRead == MarkAsReadMode.AfterDelay)
                _ = MarkReadAfterDelayAsync(item, TimeSpan.FromSeconds(Math.Max(1, preferences.MarkAsReadDelaySeconds)), ct);
        }
        catch (OperationCanceledException)
        {
        }
        catch (EwsException ex)
        {
            if (!ct.IsCancellationRequested)
            {
                Current = null;
                ErrorMessage = Loc.Format(Strings.Mail_OpenFailed, ex.Message);
            }
        }
        finally
        {
            if (_detailCts == cts) IsLoadingMessage = false;
        }
    }

    private async Task TryMarkReadAsync(MessageItemViewModel item, CancellationToken ct)
    {
        try
        {
            await SetReadAsync(item, true, ct);
        }
        catch (EwsException ex)
        {
            ErrorMessage = Loc.Format(Strings.Mail_MarkReadFailed, ex.Message); // the message is still shown
        }
    }

    private async Task MarkReadAfterDelayAsync(MessageItemViewModel item, TimeSpan delay, CancellationToken ct)
    {
        try
        {
            await Task.Delay(delay, ct);
            if (SelectedMessage == item && !item.IsRead) await TryMarkReadAsync(item, ct);
        }
        catch (OperationCanceledException)
        {
            // another message was selected before the delay elapsed
        }
    }

    private Task SetReadAsync(MessageItemViewModel item, bool isRead, CancellationToken ct = default) => SetReadAsync([item], isRead, ct);

    /// <summary>Marks rows read or unread with one request; conversations cover all their items in this folder.</summary>
    private async Task SetReadAsync(IReadOnlyList<MessageItemViewModel> items, bool isRead, CancellationToken ct = default)
    {
        var ids = await Client.SetReadStateAsync(items.SelectMany(i => i.ItemIds).ToList(), isRead, ct);
        var index = 0;
        foreach (var item in items)
        {
            // New change keys come back in request order; only single messages keep theirs.
            if (item.Conversation is null && index < ids.Count) item.Id = ids[index];
            index += item.ItemIds.Count;

            var before = item.UnreadItems;
            item.SetRead(isRead);
            if (SelectedFolder is { } folder) folder.UnreadCount = Math.Max(0, folder.UnreadCount + item.UnreadItems - before);
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadFoldersAsync();
        await LoadMessagesAsync(reset: true);
    }

    private bool CanLoadMore() => HasMore && !IsLoadingList;

    [RelayCommand(CanExecute = nameof(CanLoadMore))]
    private Task LoadMoreAsync() => LoadMessagesAsync(reset: false);

    [RelayCommand]
    private Task SearchAsync()
    {
        ActiveQuery = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim();
        return LoadMessagesAsync(reset: true);
    }

    [RelayCommand]
    private Task ClearSearchAsync()
    {
        SearchText = "";
        if (!IsSearchActive) return Task.CompletedTask;
        ActiveQuery = null;
        return LoadMessagesAsync(reset: true);
    }

    [RelayCommand]
    private void NewMail() => RequestCompose(new ComposeViewModel(_session, ComposeMode.New, null));

    private void RequestCompose(ComposeViewModel compose)
    {
        // Sending or saving changes the Drafts folder; keep it current if it is on screen.
        compose.Sent += (_, _) => RefreshDraftsIfShown();
        compose.DraftSaved += (_, _) => RefreshDraftsIfShown();
        ComposeRequested?.Invoke(this, compose);
    }

    private void RefreshDraftsIfShown()
    {
        if (IsDraftsFolder && !IsLoadingList && !IsSearchActive) _ = LoadMessagesAsync(reset: true);
    }

    public bool IsDraftsFolder => SelectedFolder?.WellKnown == WellKnownFolder.Drafts;

    private bool CanEditDraft() => IsDraftsFolder && Current is not null;

    /// <summary>Opens the selected draft in the compose window.</summary>
    [RelayCommand(CanExecute = nameof(CanEditDraft))]
    private void EditDraft() => RequestCompose(ComposeViewModel.ForDraft(_session, Current!.Detail));

    private bool CanRespond() => Current is not null;

    [RelayCommand(CanExecute = nameof(CanRespond))]
    private void Reply() => RequestCompose(new ComposeViewModel(_session, ComposeMode.Reply, Current!.Detail));

    [RelayCommand(CanExecute = nameof(CanRespond))]
    private void ReplyAll() => RequestCompose(new ComposeViewModel(_session, ComposeMode.ReplyAll, Current!.Detail));

    [RelayCommand(CanExecute = nameof(CanRespond))]
    private void Forward() => RequestCompose(new ComposeViewModel(_session, ComposeMode.Forward, Current!.Detail));

    private bool CanActOnSelection() => _selection.Count > 0;

    [RelayCommand(CanExecute = nameof(CanActOnSelection))]
    private async Task DeleteAsync()
    {
        var items = _selection.ToList();
        if (items.Count == 0) return;
        var permanent = SelectedFolder?.WellKnown == WellKnownFolder.DeletedItems;
        if (permanent && ConfirmAsync is not null
            && !await ConfirmAsync(Strings.Mail_PermanentDeleteTitle,
                items.Count == 1 ? Strings.Mail_PermanentDeleteQuestion : Loc.Format(Strings.Mail_PermanentDeleteManyQuestion, items.Count)))
            return;

        var ids = items.SelectMany(i => i.ItemIds).ToList();
        if (await RemoveFromListAsync(items, () => Client.DeleteItemsAsync(ids, permanent ? DeleteMode.SoftDelete : DeleteMode.MoveToDeletedItems))
            && !permanent && Folders.FirstOrDefault(f => f.WellKnown == WellKnownFolder.DeletedItems) is { } deleted)
            deleted.TotalCount += ids.Count;
    }

    [RelayCommand(CanExecute = nameof(CanActOnSelection))]
    private Task MoveToAsync(FolderViewModel? target) => target is null ? Task.CompletedTask : MoveAsync(_selection.ToList(), target);

    /// <summary>Moves rows to <paramref name="target"/> (Move menu, or rows dropped on a folder).</summary>
    public async Task MoveAsync(IReadOnlyList<MessageItemViewModel> items, FolderViewModel target)
    {
        if (items.Count == 0 || target == SelectedFolder) return;
        var ids = items.SelectMany(i => i.ItemIds).ToList();
        var unread = items.Sum(i => i.UnreadItems);
        if (await RemoveFromListAsync(items, () => Client.MoveItemsAsync(ids, target.Id)))
        {
            target.UnreadCount += unread;
            target.TotalCount += ids.Count;
        }
    }

    /// <summary>Marks the selection read, or unread when all of it is read already.</summary>
    [RelayCommand(CanExecute = nameof(CanActOnSelection))]
    private async Task ToggleReadAsync()
    {
        var items = _selection.ToList();
        if (items.Count == 0) return;
        try
        {
            await SetReadAsync(items, items.Any(i => !i.IsRead));
        }
        catch (EwsException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    /// <summary>Runs <paramref name="action"/> and, if it worked, takes the rows out of the list. False on failure.</summary>
    private async Task<bool> RemoveFromListAsync(IReadOnlyList<MessageItemViewModel> items, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (EwsException ex)
        {
            ErrorMessage = ex.Message;
            return false;
        }

        var first = items.Select(Messages.IndexOf).Where(i => i >= 0).DefaultIfEmpty(-1).Min();
        foreach (var item in items)
        {
            if (!Messages.Remove(item)) continue;
            if (SelectedFolder is { } folder) folder.UnreadCount = Math.Max(0, folder.UnreadCount - item.UnreadItems);
            _nextOffset = Math.Max(0, _nextOffset - 1);
        }
        // Keep working down the list: select what moved into the first removed row's place.
        if (first >= 0) SelectedMessage = Messages.Count == 0 ? null : Messages[Math.Min(first, Messages.Count - 1)];
        return true;
    }

    /// <param name="response">"Accept", "Tentative" or "Decline" (from the view's CommandParameter).</param>
    [RelayCommand(CanExecute = nameof(IsMeetingRequest))]
    private async Task RespondToMeetingAsync(string? response)
    {
        if (SelectedMessage is not { } item || Current is not { } current || !Enum.TryParse<MeetingResponse>(response, out var kind)) return;
        // Exchange moves an answered invitation to Deleted Items. A plain message row leaves the list; a conversation
        // row (the invitation was one of its messages) is reloaded instead.
        if (item.Conversation is null)
        {
            await RemoveFromListAsync([item], () => Client.RespondToMeetingAsync(item.Id, kind));
        }
        else
        {
            try
            {
                await Client.RespondToMeetingAsync(current.Detail.ItemId, kind);
            }
            catch (EwsException ex)
            {
                ErrorMessage = ex.Message;
                return;
            }
            await LoadMessagesAsync(reset: true);
        }
        StatusText = kind switch
        {
            MeetingResponse.Accept => Strings.Meeting_Accepted,
            MeetingResponse.Tentative => Strings.Meeting_TentativeSent,
            _ => Strings.Meeting_Declined,
        };
    }

    public Task<AttachmentContent> DownloadAttachmentAsync(AttachmentViewModel attachment) =>
        Client.GetAttachmentAsync(attachment.Info);

    /// <summary>
    /// Periodic poll: refreshes unread counts, prepends newly arrived messages to the current list and raises
    /// <see cref="NewMailArrived"/> for new unread inbox mail. Returns the number of messages added to the current list.
    /// </summary>
    public async Task<int> BackgroundRefreshAsync()
    {
        try
        {
            return await PollAsync();
        }
        catch (EwsException)
        {
            return 0; // transient; the next tick will try again
        }
    }

    /// <summary>
    /// Manual "Send/Receive": the same check as the background poll, but failures are shown. Messages are sent the
    /// moment they are written, so there is nothing queued to send; if the folders never loaded, everything reloads.
    /// </summary>
    [RelayCommand]
    private async Task SendReceiveAsync()
    {
        if (Folders.Count == 0)
        {
            await RefreshAsync();
            return;
        }
        try
        {
            await PollAsync();
        }
        catch (EwsException ex)
        {
            ErrorMessage = Loc.Format(Strings.Mail_SendReceiveFailed, ex.Message);
        }
    }

    private async Task<int> PollAsync()
    {
        if (Folders.Count == 0) return 0;
        if (_backgroundRefreshRunning)
        {
            // A change was reported while a check is under way; it may have missed it, so check once more after.
            _pollAgain = true;
            return 0;
        }
        _backgroundRefreshRunning = true;
        try
        {
            var counts = await Client.GetFolderCountsAsync(Folders.Select(f => f.Id));
            foreach (var f in Folders)
            {
                if (counts.TryGetValue(f.Id, out var c))
                {
                    f.UnreadCount = c.Unread;
                    f.TotalCount = c.Total;
                }
            }

            var inbox = Inbox;
            MailPage? inboxPage = null;
            var added = 0;
            if (SelectedFolder is { } folder && !IsSearchActive && !IsLoadingList)
            {
                if (UsesConversations)
                {
                    var conversations = await Client.FindConversationsAsync(folder.Id, 0, 25);
                    if (folder == SelectedFolder && !IsLoadingList && UsesConversations) added = MergeConversations(folder, conversations);
                }
                else
                {
                    var page = await Client.FindItemsAsync(folder.Id, 0, 25);
                    if (folder == SelectedFolder && !IsLoadingList) added = MergeNewItems(folder, page);
                    if (folder == inbox) inboxPage = page;
                }
            }

            if (inbox is not null)
            {
                inboxPage ??= await Client.FindItemsAsync(inbox.Id, 0, 25);
                DetectNewInboxMail(inboxPage);
            }
            return added;
        }
        finally
        {
            _backgroundRefreshRunning = false;
            if (_pollAgain)
            {
                _pollAgain = false;
                _ = BackgroundRefreshAsync();
            }
        }
    }

    /// <summary>
    /// Brings the first conversations up to date: new ones are inserted, ones with new activity move to the top and
    /// get fresh counts. The selected row is kept as it is so the reading pane doesn't jump.
    /// </summary>
    private int MergeConversations(FolderViewModel folder, ConversationPage page)
    {
        var added = 0;
        for (var i = 0; i < page.Items.Count && i <= Messages.Count; i++)
        {
            var fresh = page.Items[i];
            var existing = Messages.FirstOrDefault(m => m.Conversation?.ConversationId == fresh.ConversationId);
            if (existing is null) added++;

            var row = existing is not null && (existing == SelectedMessage || Unchanged(existing.Conversation!, fresh))
                ? existing
                : MessageItemViewModel.ForConversation(fresh, folder.ShowsRecipients);
            if (existing is not null && row != existing) Messages[Messages.IndexOf(existing)] = row;

            var index = Messages.IndexOf(row);
            if (index < 0) Messages.Insert(i, row);
            else if (index != i) Messages.Move(index, i);
        }
        _nextOffset += added;
        return added;

        // The record holds lists, so compare what can change instead of using record equality.
        static bool Unchanged(ConversationSummary a, ConversationSummary b) =>
            a.LastDelivery == b.LastDelivery && a.UnreadCount == b.UnreadCount
            && a.MessageCount == b.MessageCount && a.GlobalMessageCount == b.GlobalMessageCount;
    }

    /// <summary>
    /// Whether streamed changes concern mail folders (and are worth a refresh). Calendar and contact changes don't;
    /// an empty batch (reported after a reconnect) always does.
    /// </summary>
    public bool ConcernsMail(IReadOnlyList<MailboxEvent> events)
    {
        if (events.Count == 0) return true;
        var ids = Folders.Select(f => f.Id).ToHashSet();
        return events.Any(e => e.Kind == MailboxEventKind.NewMail
            || (e.ParentFolderId is { } p && ids.Contains(p))
            || (e.OldParentFolderId is { } o && ids.Contains(o))
            || (e.FolderId is { } f && ids.Contains(f)));
    }

    private int MergeNewItems(FolderViewModel folder, MailPage page)
    {
        var existing = new Dictionary<string, MessageItemViewModel>();
        foreach (var m in Messages) existing.TryAdd(m.Id.Id, m);
        var fresh = page.Items.TakeWhile(i => !existing.ContainsKey(i.ItemId.Id)).ToList();
        for (var i = 0; i < fresh.Count; i++)
            Messages.Insert(i, new MessageItemViewModel(fresh[i], folder.ShowsRecipients));
        _nextOffset += fresh.Count;

        // Pick up read/unread changes made in other clients (Outlook, OWA, phone).
        foreach (var summary in page.Items)
        {
            if (existing.TryGetValue(summary.ItemId.Id, out var vm) && vm.IsRead != summary.IsRead)
            {
                vm.IsRead = summary.IsRead;
                vm.Id = summary.ItemId;
            }
        }
        return fresh.Count;
    }

    private void DetectNewInboxMail(MailPage inboxPage)
    {
        // The first look at the inbox only records what is already there.
        if (_knownInboxIds is null)
        {
            _knownInboxIds = inboxPage.Items.Select(i => i.ItemId.Id).ToHashSet();
            return;
        }
        var fresh = inboxPage.Items.Where(i => _knownInboxIds.Add(i.ItemId.Id) && !i.IsRead).ToList();
        if (fresh.Count > 0) NewMailArrived?.Invoke(this, fresh);
    }

    /// <summary>Shows a message of the inbox, e.g. after its notification was clicked.</summary>
    public async Task OpenInboxMessageAsync(string itemId)
    {
        if (Inbox is not { } inbox) return;
        if (SelectedFolder != inbox) SelectedFolder = inbox;
        else if (IsSearchActive) await ClearSearchAsync();

        for (var i = 0; i < 100 && IsLoadingList; i++) await Task.Delay(100);
        MessageItemViewModel? Find() => Messages.FirstOrDefault(m => m.ItemIds.Any(i => i.Id == itemId));
        var item = Find();
        if (item is null)
        {
            await LoadMessagesAsync(reset: true);
            item = Find();
        }
        if (item is not null) SelectedMessage = item;
    }
}
