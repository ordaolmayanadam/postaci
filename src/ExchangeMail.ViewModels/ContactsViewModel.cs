using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExchangeMail.Core.Ews;
using ExchangeMail.Core.Models;

namespace ExchangeMail.ViewModels;

public sealed partial class ContactsViewModel : ObservableObject
{
    private const int PageSize = 100;

    private readonly MailSession _session;
    private CancellationTokenSource? _loadCts;
    private int _nextOffset;
    private bool _initialized;

    public ContactsViewModel(MailSession session)
    {
        _session = session;
        Contacts.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
    }

    public ObservableCollection<ContactItemViewModel> Contacts { get; } = [];

    /// <summary>0 = personal Contacts folder, 1 = company directory (GAL).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirectory), nameof(EmptyText), nameof(SearchPlaceholder))]
    private int _sourceIndex;

    public bool IsDirectory => SourceIndex == 1;

    public string SearchPlaceholder => IsDirectory ? Strings.Contacts_SearchDirectoryPlaceholder : Strings.Contacts_SearchMinePlaceholder;

    [ObservableProperty] private string _searchText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand), nameof(SendMailCommand), nameof(AddToContactsCommand), nameof(EditCommand))]
    private ContactItemViewModel? _selected;

    public bool HasSelection => Selected is not null;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadMoreCommand))]
    private bool _hasMore;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadMoreCommand))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool _isLoading;

    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string _statusText = "";

    public bool IsEmpty => Contacts.Count == 0 && !IsLoading;

    public string EmptyText => IsDirectory && string.IsNullOrWhiteSpace(SearchText)
        ? Strings.Contacts_DirectoryHint
        : Strings.Contacts_NoneFound;

    /// <summary>Asks the user a yes/no question; supplied by the view.</summary>
    public Func<string, string, Task<bool>>? ConfirmAsync { get; set; }

    public event EventHandler<ComposeViewModel>? ComposeRequested;

    /// <summary>Raised by the Edit command; the view shows the editor and calls <see cref="UpdateAsync"/>.</summary>
    public event EventHandler<ContactItemViewModel>? EditRequested;

    public Task InitializeAsync()
    {
        if (_initialized) return Task.CompletedTask;
        _initialized = true;
        return LoadAsync(reset: true);
    }

    partial void OnSourceIndexChanged(int value)
    {
        Selected = null;
        _ = LoadAsync(reset: true);
    }

    partial void OnSearchTextChanged(string value) => OnPropertyChanged(nameof(EmptyText));

    private async Task LoadAsync(bool reset)
    {
        _loadCts?.Cancel();
        var cts = _loadCts = new CancellationTokenSource();
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            IReadOnlyList<ContactInfo> items;
            if (IsDirectory)
            {
                items = await _session.Client.SearchDirectoryAsync(SearchText, cts.Token);
                HasMore = false;
                StatusText = items.Count >= 100 ? Strings.Contacts_First100
                    : items.Count > 0 ? Loc.Count(items.Count, Strings.Common_ResultCount_One, Strings.Common_ResultCount) : "";
            }
            else
            {
                var page = await _session.Client.FindContactsAsync(reset ? 0 : _nextOffset, PageSize, SearchText, cts.Token);
                items = page.Items;
                _nextOffset = page.NextOffset;
                HasMore = !page.IsLastPage;
                StatusText = Loc.Count(page.TotalCount, Strings.Contacts_Count_One, Strings.Contacts_Count);
            }
            if (cts.IsCancellationRequested) return;

            if (reset)
            {
                Selected = null;
                Contacts.Clear();
            }
            foreach (var c in items) Contacts.Add(new ContactItemViewModel(c));
        }
        catch (OperationCanceledException)
        {
        }
        catch (EwsException ex)
        {
            ErrorMessage = Loc.Format(Strings.Contacts_LoadFailed, ex.Message);
        }
        finally
        {
            if (_loadCts == cts) IsLoading = false;
        }
    }

    [RelayCommand]
    private Task SearchAsync() => LoadAsync(reset: true);

    private bool CanLoadMore() => HasMore && !IsLoading;

    [RelayCommand(CanExecute = nameof(CanLoadMore))]
    private Task LoadMoreAsync() => LoadAsync(reset: false);

    private bool CanSendMail() => Selected?.HasEmail == true;

    [RelayCommand(CanExecute = nameof(CanSendMail))]
    private void SendMail()
    {
        if (Selected is not { } contact) return;
        var compose = new ComposeViewModel(_session, ComposeMode.New, null)
        {
            To = new MailboxAddress(contact.DisplayName, contact.Email!).ToString() + "; ",
        };
        ComposeRequested?.Invoke(this, compose);
    }

    private bool CanDelete() => Selected is { IsDirectoryEntry: false };

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task DeleteAsync()
    {
        if (Selected is not { Contact.ItemId: { } id } contact) return;
        if (ConfirmAsync is not null && !await ConfirmAsync(Strings.Contacts_DeleteTitle, Loc.Format(Strings.Contacts_DeleteQuestion, contact.DisplayName)))
            return;
        try
        {
            await _session.Client.DeleteItemsAsync([id]);
            Contacts.Remove(contact);
            Selected = null;
        }
        catch (EwsException ex)
        {
            ErrorMessage = Loc.Format(Strings.Contacts_DeleteFailed, ex.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private void Edit()
    {
        if (Selected is { } contact) EditRequested?.Invoke(this, contact);
    }

    public async Task<bool> UpdateAsync(ContactItemViewModel contact, NewContact changes)
    {
        if (contact.Contact.ItemId is not { } id) return false;
        try
        {
            await _session.Client.UpdateContactAsync(id, changes);
            var selectedId = id.Id;
            await LoadAsync(reset: true);
            Selected = Contacts.FirstOrDefault(c => c.Contact.ItemId?.Id == selectedId);
            StatusText = Strings.Contacts_Updated;
            return true;
        }
        catch (Exception ex) when (ex is EwsException or ArgumentException)
        {
            ErrorMessage = Loc.Format(Strings.Contacts_UpdateFailed, ex.Message);
            return false;
        }
    }

    private bool CanAddToContacts() => Selected is { IsDirectoryEntry: true, HasEmail: true };

    /// <summary>Copies a directory entry into the personal Contacts folder.</summary>
    [RelayCommand(CanExecute = nameof(CanAddToContacts))]
    private async Task AddToContactsAsync()
    {
        if (Selected is not { } c) return;
        var parts = c.DisplayName.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var created = await CreateAsync(new NewContact
        {
            GivenName = parts.ElementAtOrDefault(0) ?? "",
            Surname = parts.ElementAtOrDefault(1) ?? "",
            Email = c.Email ?? "",
            Company = c.Contact.Company ?? "",
            JobTitle = c.Contact.JobTitle ?? "",
            Department = c.Contact.Department ?? "",
            BusinessPhone = c.Contact.BusinessPhone ?? "",
            MobilePhone = c.Contact.MobilePhone ?? "",
        }, reload: false);
        if (created) StatusText = Loc.Format(Strings.Contacts_AddedFormat, c.DisplayName);
    }

    /// <summary>Creates a personal contact from the editor dialog.</summary>
    public async Task<bool> CreateAsync(NewContact contact, bool reload = true)
    {
        try
        {
            await _session.Client.CreateContactAsync(contact);
            if (reload && !IsDirectory) await LoadAsync(reset: true);
            return true;
        }
        catch (Exception ex) when (ex is EwsException or ArgumentException)
        {
            ErrorMessage = Loc.Format(Strings.Contacts_SaveFailed, ex.Message);
            return false;
        }
    }
}

public sealed class ContactItemViewModel(ContactInfo contact)
{
    public ContactInfo Contact { get; } = contact;
    public string DisplayName => string.IsNullOrWhiteSpace(Contact.DisplayName) ? Contact.Email ?? Strings.Contacts_Unnamed : Contact.DisplayName;
    public string? Email => Contact.Email;
    public bool HasEmail => !string.IsNullOrWhiteSpace(Contact.Email);
    public bool IsDirectoryEntry => Contact.IsDirectoryEntry;

    public string Subtitle => string.Join(" · ", new[] { Contact.JobTitle, Contact.Department, Contact.Company }
        .Where(s => !string.IsNullOrWhiteSpace(s)));

    public string ListSecondLine => Contact.IsDistributionList ? Strings.Contacts_DistributionList : Email ?? Subtitle;
    public string BusinessPhone => Contact.BusinessPhone ?? "";
    public string MobilePhone => Contact.MobilePhone ?? "";
    public bool HasBusinessPhone => !string.IsNullOrWhiteSpace(Contact.BusinessPhone);
    public bool HasMobilePhone => !string.IsNullOrWhiteSpace(Contact.MobilePhone);
    public bool HasSubtitle => Subtitle.Length > 0;
}
