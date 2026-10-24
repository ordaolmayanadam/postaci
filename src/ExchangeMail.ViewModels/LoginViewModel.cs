using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExchangeMail.Core.Ews;

namespace ExchangeMail.ViewModels;

public sealed partial class LoginViewModel : ObservableObject
{
    private readonly SettingsStore _settings;
    private readonly ICredentialStore _credentials;
    private readonly UserPreferences? _preferences;

    /// <param name="preferences">The app's live preferences instance; loaded from <paramref name="settings"/> when null.</param>
    public LoginViewModel(SettingsStore settings, ICredentialStore credentials, UserPreferences? preferences = null)
    {
        _settings = settings;
        _credentials = credentials;
        _preferences = preferences;

        var saved = settings.Load();
        _email = saved.Email;
        _userName = saved.UserName;
        _ewsUrl = saved.EwsUrl;
        _useWindowsAuth = saved.UseWindowsAuth;
        _rememberPassword = saved.RememberPassword;
        if (!saved.UseWindowsAuth && saved.RememberPassword && saved.EffectiveUserName.Length > 0)
            Password = credentials.GetPassword(saved.EffectiveUserName) ?? "";

        CanAutoConnect = settings.Exists && saved.Email.Length > 0 && (saved.UseWindowsAuth || Password.Length > 0);
        if (settings.Exists && !saved.UseWindowsAuth && saved.RememberPassword && Password.Length == 0)
            _errorMessage = Strings.Login_SavedPasswordMissing;
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    private string _email;

    [ObservableProperty]
    private string _userName;

    [ObservableProperty]
    private string _ewsUrl;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPasswordLogin))]
    private bool _useWindowsAuth;

    [ObservableProperty]
    private bool _rememberPassword;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string _statusText = "";

    /// <summary>Set by the view from PasswordBox.PasswordChanged; deliberately not observable.</summary>
    public string Password { get; set; } = "";

    public bool IsPasswordLogin => !UseWindowsAuth;

    /// <summary>True when saved settings are complete enough to connect without user input.</summary>
    public bool CanAutoConnect { get; }

    public event EventHandler<MailSession>? Connected;

    private bool CanConnect() => !IsBusy && Email.Contains('@');

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync(CancellationToken ct)
    {
        ErrorMessage = null;
        if (!UseWindowsAuth && Password.Length == 0)
        {
            ErrorMessage = Strings.Login_PasswordRequired;
            return;
        }

        var account = new AccountSettings
        {
            Email = Email.Trim(),
            UserName = UserName.Trim(),
            EwsUrl = EwsUrl.Trim(),
            UseWindowsAuth = UseWindowsAuth,
            RememberPassword = RememberPassword,
        };

        IsBusy = true;
        EwsClient? client = null;
        try
        {
            var credentials = account.CreateCredentials(Password);
            Uri ewsUri;
            if (account.EwsUrl.Length > 0)
            {
                if (!Uri.TryCreate(account.EwsUrl, UriKind.Absolute, out ewsUri!) || ewsUri.Scheme != Uri.UriSchemeHttps)
                {
                    ErrorMessage = Strings.Login_EwsUrlInvalid;
                    return;
                }
            }
            else
            {
                StatusText = Strings.Login_Autodiscovering;
                ewsUri = await Autodiscover.DiscoverEwsUrlAsync(account.Email, credentials, ct);
                account.EwsUrl = ewsUri.ToString();
                EwsUrl = account.EwsUrl;
            }

            StatusText = Loc.Format(Strings.Login_ConnectingFormat, ewsUri.Host);
            client = EwsClient.Create(ewsUri, credentials);
            await client.GetWellKnownFolderIdsAsync(ct); // verifies credentials and mailbox access

            _settings.Save(account);
            try
            {
                if (!account.UseWindowsAuth && account.RememberPassword)
                    _credentials.SavePassword(account.EffectiveUserName, Password);
                else
                    _credentials.RemovePassword(account.EffectiveUserName);
            }
            catch (Exception ex)
            {
                // Not fatal: the user is signed in, they will just be asked again next time.
                System.Diagnostics.Debug.WriteLine("Could not save the password: " + ex);
            }

            var session = new MailSession(client, account, _preferences ?? _settings.LoadPreferences());
            client = null;
            Connected?.Invoke(this, session);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is EwsException or ArgumentException)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            client?.Dispose();
            StatusText = "";
            IsBusy = false;
        }
    }
}
