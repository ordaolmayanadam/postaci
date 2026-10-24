using CommunityToolkit.Mvvm.ComponentModel;
using ExchangeMail.Core.Ews;
using ExchangeMail.Core.Models;

namespace ExchangeMail.ViewModels;

/// <summary>
/// The small card shown for a sender or recipient: the directory entry, the chain of managers above it (top first)
/// and its direct reports.
/// </summary>
public sealed partial class PersonCardViewModel(EwsClient client) : ObservableObject
{
    /// <summary>Stops at the top of the organization, or after this many levels if the directory has a loop.</summary>
    private const int MaxManagerLevels = 10;

    private CancellationTokenSource? _cts;

    /// <summary>The address that was clicked, shown while loading and when it is not in the directory.</summary>
    [ObservableProperty] private MailboxAddress? _address;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInDirectory), nameof(Title))]
    private DirectoryPerson? _person;

    /// <summary>Managers from the top of the organization down to the person's own manager.</summary>
    [ObservableProperty] private IReadOnlyList<DirectoryPerson> _managers = [];

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isLoadingManagers;
    [ObservableProperty] private string? _errorMessage;

    public bool IsInDirectory => Person is not null;

    /// <summary>"Job title · Department", whichever are known.</summary>
    public string Title => Person is null ? "" : string.Join(" · ", new[] { Person.JobTitle, Person.Department }.Where(s => !string.IsNullOrEmpty(s)));

    /// <summary>Shows <paramref name="address"/>: its directory entry first, then walks up the manager chain.</summary>
    public async Task LoadAsync(MailboxAddress address)
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        var ct = cts.Token;
        Address = address;
        Person = null;
        Managers = [];
        ErrorMessage = null;
        IsLoading = true;
        try
        {
            var person = await client.GetDirectoryPersonAsync(address.Address.Length > 0 ? address.Address : address.Display, ct);
            if (ct.IsCancellationRequested) return;
            Person = person;
            IsLoading = false;
            if (person is not null) await LoadManagersAsync(person, ct);
        }
        catch (OperationCanceledException)
        {
        }
        catch (EwsException ex)
        {
            if (!ct.IsCancellationRequested) ErrorMessage = Loc.Format(Strings.Person_LoadFailed, ex.Message);
        }
        finally
        {
            if (_cts == cts)
            {
                IsLoading = false;
                IsLoadingManagers = false;
            }
        }
    }

    private async Task LoadManagersAsync(DirectoryPerson person, CancellationToken ct)
    {
        IsLoadingManagers = true;
        var chain = new List<DirectoryPerson>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { person.Email ?? person.DisplayName };
        var current = person;
        while (chain.Count < MaxManagerLevels && ManagerKey(current) is { } key && seen.Add(key))
        {
            var manager = await client.GetDirectoryPersonAsync(key, ct);
            if (manager is null || ct.IsCancellationRequested) break;
            chain.Insert(0, manager);
            current = manager;
            Managers = chain.ToList(); // show each level as it arrives
        }
        IsLoadingManagers = false;
    }

    /// <summary>The manager's SMTP address when known (exact), otherwise the name the directory gives.</summary>
    private static string? ManagerKey(DirectoryPerson p) =>
        p.ManagerEmail is { } email && email.Contains('@') ? email : p.ManagerName;
}
