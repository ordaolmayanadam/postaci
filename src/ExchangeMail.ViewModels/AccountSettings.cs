using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using ExchangeMail.Core.Ews;

namespace ExchangeMail.ViewModels;

/// <summary>Non-secret connection settings. The password lives in <see cref="ICredentialStore"/>.</summary>
public sealed class AccountSettings
{
    public string Email { get; set; } = "";
    /// <summary>DOMAIN\user or UPN. Empty means "use the e-mail address".</summary>
    public string UserName { get; set; } = "";
    /// <summary>Explicit EWS endpoint. Empty means "use Autodiscover".</summary>
    public string EwsUrl { get; set; } = "";
    public bool UseWindowsAuth { get; set; }
    public bool RememberPassword { get; set; } = true;

    public string EffectiveUserName => string.IsNullOrWhiteSpace(UserName) ? Email.Trim() : UserName.Trim();

    public ICredentials CreateCredentials(string password)
    {
        if (UseWindowsAuth) return CredentialCache.DefaultNetworkCredentials;

        var user = EffectiveUserName;
        var slash = user.IndexOf('\\');
        return slash > 0
            ? new NetworkCredential(user[(slash + 1)..], password, user[..slash])
            : new NetworkCredential(user, password);
    }
}

/// <summary>Whether the app starts when the user signs in to Windows, and who decided it.</summary>
public enum StartupState
{
    Disabled,
    Enabled,
    /// <summary>Turned off by the user in Windows (Settings → Apps → Startup); only they can turn it back on there.</summary>
    DisabledByUser,
    DisabledByPolicy,
    EnabledByPolicy,
}

/// <summary>Starting the app when the user signs in to Windows; implemented by the app (the package's startup task).</summary>
public interface IStartupService
{
    Task<StartupState> GetStateAsync();

    /// <summary>Asks for the change and returns the resulting state, which may not be the one asked for.</summary>
    Task<StartupState> SetEnabledAsync(bool enabled);
}

/// <summary>Secure password storage, implemented by the app (Windows Credential Manager).</summary>
public interface ICredentialStore
{
    string? GetPassword(string userName);
    void SavePassword(string userName, string password);
    void RemovePassword(string userName);
}

public sealed class SettingsStore(string? directory = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static readonly JsonSerializerOptions PreferenceOptions = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    private readonly string _directory = directory ?? AppInfo.DataDirectory;

    private string SettingsPath => Path.Combine(_directory, "settings.json");
    private string PreferencesPath => Path.Combine(_directory, "preferences.json");

    public bool Exists => File.Exists(SettingsPath);

    public AccountSettings Load()
    {
        try
        {
            return File.Exists(SettingsPath)
                ? JsonSerializer.Deserialize<AccountSettings>(File.ReadAllText(SettingsPath), JsonOptions) ?? new AccountSettings()
                : new AccountSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new AccountSettings();
        }
    }

    public void Save(AccountSettings settings)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
    }

    public UserPreferences LoadPreferences()
    {
        try
        {
            return File.Exists(PreferencesPath)
                ? JsonSerializer.Deserialize<UserPreferences>(File.ReadAllText(PreferencesPath), PreferenceOptions) ?? new UserPreferences()
                : new UserPreferences();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new UserPreferences();
        }
    }

    public void SavePreferences(UserPreferences preferences)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(PreferencesPath, JsonSerializer.Serialize(preferences, PreferenceOptions));
    }
}

/// <summary>An authenticated connection to one mailbox.</summary>
public sealed class MailSession(EwsClient client, AccountSettings account, UserPreferences? preferences = null) : IDisposable
{
    public EwsClient Client { get; } = WithAnchor(client, account);
    public AccountSettings Account { get; } = account;
    /// <summary>Shared, live preferences; the settings page edits this instance.</summary>
    public UserPreferences Preferences { get; } = preferences ?? new UserPreferences();

    public void Dispose() => Client.Dispose();

    private static EwsClient WithAnchor(EwsClient client, AccountSettings account)
    {
        if (!string.IsNullOrWhiteSpace(account.Email)) client.AnchorMailbox ??= account.Email;
        return client;
    }
}
