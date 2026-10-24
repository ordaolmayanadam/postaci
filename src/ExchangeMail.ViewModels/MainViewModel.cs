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

    public MainViewModel(MailSession session)
    {
        _session = session;
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
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand), nameof(ToggleReadCommand), nameof(MoveToCommand), nameof(RespondToMeetingCommand))]
    [NotifyPropertyChangedFor(nameof(IsMeetingRequest))]
    private MessageItemViewModel? _selectedMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCurrent), nameof(ShowPlaceholder))]
    [NotifyCanExecuteChangedFor(nameof(ReplyCommand), nameof(ReplyAllCommand), nameof(ForwardCommand), nameof(RespondToMeetingCommand), nameof(EditDraftCommand))]
    [NotifyPropertyChangedFor(nameof(IsMeetingRequest))]
    private MessageDetailViewModel? _current;

    /// <summary>The open message is a meeting invitation that can be accepted or declined.</summary>
    public bool IsMeetingRequest => Current is not null
        && SelectedMessage?.Summary.ItemClass.StartsWith("IPM.Schedule.Meeting.Request", StringComparison.OrdinalIgnoreCase) == true;

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

    partial void OnSelectedMessageChanged(MessageItemViewModel? value) => _ = LoadDetailAsync(value);

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

            var detail = new MessageDetailViewModel(await Client.GetItemAsync(item.Id, ct), preferences.AlwaysShowRemoteImages);
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

    private async Task SetReadAsync(MessageItemViewModel item, bool isRead, CancellationToken ct = default)
    {
        var ids = await Client.SetReadStateAsync([item.Id], isRead, ct);
        if (ids.Count > 0) item.Id = ids[0];
        if (item.IsRead == isRead) return;
        item.IsRead = isRead;
        if (SelectedFolder is { } folder) folder.UnreadCount = Math.Max(0, folder.UnreadCount + (isRead ? -1 : 1));
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

    private bool HasSelection() => SelectedMessage is not null;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task DeleteAsync()
    {
        if (SelectedMessage is not { } item) return;
        var permanent = SelectedFolder?.WellKnown == WellKnownFolder.DeletedItems;
        if (permanent && ConfirmAsync is not null
            && !await ConfirmAsync(Strings.Mail_PermanentDeleteTitle, Strings.Mail_PermanentDeleteQuestion))
            return;

        await RemoveFromListAsync(item, () => Client.DeleteItemsAsync([item.Id], permanent ? DeleteMode.SoftDelete : DeleteMode.MoveToDeletedItems));
        var deleted = Folders.FirstOrDefault(f => f.WellKnown == WellKnownFolder.DeletedItems);
        if (deleted is not null && !permanent) deleted.TotalCount++;
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task MoveToAsync(FolderViewModel? target)
    {
        if (SelectedMessage is not { } item || target is null || target == SelectedFolder) return;
        await RemoveFromListAsync(item, () => Client.MoveItemsAsync([item.Id], target.Id));
        if (!item.IsRead) target.UnreadCount++;
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task ToggleReadAsync()
    {
        if (SelectedMessage is not { } item) return;
        try
        {
            await SetReadAsync(item, !item.IsRead);
        }
        catch (EwsException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    private async Task RemoveFromListAsync(MessageItemViewModel item, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (EwsException ex)
        {
            ErrorMessage = ex.Message;
            return;
        }

        var index = Messages.IndexOf(item);
        if (index < 0) return;
        if (!item.IsRead && SelectedFolder is { } folder) folder.UnreadCount = Math.Max(0, folder.UnreadCount - 1);
        Messages.RemoveAt(index);
        _nextOffset = Math.Max(0, _nextOffset - 1);
        SelectedMessage = Messages.Count == 0 ? null : Messages[Math.Min(index, Messages.Count - 1)];
    }

    /// <param name="response">"Accept", "Tentative" or "Decline" (from the view's CommandParameter).</param>
    [RelayCommand(CanExecute = nameof(IsMeetingRequest))]
    private async Task RespondToMeetingAsync(string? response)
    {
        if (SelectedMessage is not { } item || !Enum.TryParse<MeetingResponse>(response, out var kind)) return;
        // Exchange moves an answered invitation to Deleted Items, so it leaves the list.
        await RemoveFromListAsync(item, () => Client.RespondToMeetingAsync(item.Id, kind));
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
        if (_backgroundRefreshRunning || Folders.Count == 0) return 0;
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
                var page = await Client.FindItemsAsync(folder.Id, 0, 25);
                if (folder == SelectedFolder && !IsLoadingList) added = MergeNewItems(folder, page);
                if (folder == inbox) inboxPage = page;
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
        }
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
        var item = Messages.FirstOrDefault(m => m.Id.Id == itemId);
        if (item is null)
        {
            await LoadMessagesAsync(reset: true);
            item = Messages.FirstOrDefault(m => m.Id.Id == itemId);
        }
        if (item is not null) SelectedMessage = item;
    }
}
