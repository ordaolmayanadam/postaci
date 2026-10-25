using System.ComponentModel;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ExchangeMail.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    /// <summary>Choices offered for the background refresh interval, in seconds.</summary>
    public static readonly int[] RefreshIntervals = [30, 60, 120, 300, 900];

    public static readonly int[] UiTextScales = [90, 100, 115, 130];
    public static readonly int[] ReadingZooms = [80, 90, 100, 110, 125, 150, 175, 200];
    public static readonly int[] ComposeFontSizes = [9, 10, 11, 12, 14, 16, 18];

    /// <summary>Stored values of the language choices, in the order shown (System, Türkçe, English).</summary>
    public static readonly string[] Languages = ["", Loc.Turkish, Loc.English];

    /// <summary>First entry of the font lists; stands for "don't override".</summary>
    public static string DefaultFontLabel => Strings.Settings_DefaultFont;

    private readonly string _startUiFont;
    private readonly int _startUiScale;
    private readonly string _startLanguage;

    private readonly MailSession _session;
    private readonly SettingsStore _store;
    private readonly ICredentialStore _credentials;

    public SettingsViewModel(MailSession session, SettingsStore store, ICredentialStore credentials)
    {
        _session = session;
        _store = store;
        _credentials = credentials;
        Preferences.PropertyChanged += OnPreferenceChanged;
        _startUiFont = Preferences.UiFontFamily;
        _startUiScale = Preferences.UiTextScale;
        _startLanguage = Preferences.Language;
        _hasSavedPassword = !Account.UseWindowsAuth && credentials.GetPassword(Account.EffectiveUserName) is not null;
    }

    public UserPreferences Preferences => _session.Preferences;

    /// <summary>Out of Office settings, shared with the shell's banner.</summary>
    public AutoReplyViewModel AutoReply { get; init; } = null!;

    /// <summary>Windows sign-in startup; null where not supported (tests).</summary>
    public IStartupService? Startup { get; init; }

    public bool MinimizeToTray
    {
        get => Preferences.MinimizeToTray;
        set => Preferences.MinimizeToTray = value;
    }

    public bool CanStartWithWindows => Startup is not null;

    private bool _startWithWindows;

    /// <summary>The switch; turning it on may not succeed (see <see cref="ApplyStartupState"/>).</summary>
    public bool StartWithWindows
    {
        get => _startWithWindows;
        set
        {
            if (Startup is null || _startWithWindows == value) return;
            _startWithWindows = value;
            OnPropertyChanged();
            _ = ChangeStartupAsync(value);
        }
    }

    /// <summary>Reads the current state; Windows can change it outside the app (Settings → Apps → Startup).</summary>
    public async Task LoadStartupAsync()
    {
        if (Startup is null) return;
        try
        {
            ApplyStartupState(await Startup.GetStateAsync(), reportBlocked: false);
        }
        catch (Exception ex)
        {
            Diagnostics.Log("Startup state", ex);
        }
    }

    private async Task ChangeStartupAsync(bool enabled)
    {
        try
        {
            ApplyStartupState(await Startup!.SetEnabledAsync(enabled), reportBlocked: enabled);
        }
        catch (Exception ex)
        {
            ErrorMessage = Loc.Format(Strings.Settings_StartupChangeFailed, ex.Message);
            await LoadStartupAsync();
        }
    }

    /// <summary>Shows the actual state; when turning it on was refused, says where it can be changed.</summary>
    private void ApplyStartupState(StartupState state, bool reportBlocked)
    {
        var enabled = state is StartupState.Enabled or StartupState.EnabledByPolicy;
        if (reportBlocked && !enabled)
        {
            ErrorMessage = state == StartupState.DisabledByPolicy ? Strings.Settings_StartupDisabledByPolicy : Strings.Settings_StartupDisabledByUser;
        }
        else if (enabled)
        {
            ErrorMessage = null;
        }
        if (_startWithWindows == enabled) return;
        _startWithWindows = enabled;
        OnPropertyChanged(nameof(StartWithWindows));
    }
    private AccountSettings Account => _session.Account;

    public string AccountEmail => Account.Email;
    public string ServerUrl => _session.Client.EwsUrl.ToString();
    public string SignInText => Account.UseWindowsAuth ? Strings.Settings_WindowsSignIn : Account.EffectiveUserName;
    public string AppVersion => Loc.Format(Strings.Settings_VersionFormat, Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "1.0.0");

    public int MarkAsReadIndex
    {
        get => (int)Preferences.MarkAsRead;
        set => Preferences.MarkAsRead = (MarkAsReadMode)Math.Clamp(value, 0, 2);
    }

    public bool IsDelayEnabled => Preferences.MarkAsRead == MarkAsReadMode.AfterDelay;

    public double MarkAsReadDelaySeconds
    {
        get => Preferences.MarkAsReadDelaySeconds;
        set => Preferences.MarkAsReadDelaySeconds = double.IsNaN(value) ? 5 : (int)Math.Clamp(value, 1, 120);
    }

    public int RefreshIndex
    {
        get => Math.Max(0, Array.IndexOf(RefreshIntervals, Preferences.RefreshIntervalSeconds));
        set => Preferences.RefreshIntervalSeconds = RefreshIntervals[Math.Clamp(value, 0, RefreshIntervals.Length - 1)];
    }

    public bool AlwaysShowRemoteImages
    {
        get => Preferences.AlwaysShowRemoteImages;
        set => Preferences.AlwaysShowRemoteImages = value;
    }

    /// <summary>Signature HTML for the editor (converted from a legacy plain-text signature if needed).</summary>
    public string SignatureHtml
    {
        get => Preferences.GetSignatureHtml();
        set
        {
            Preferences.SignatureHtml = IsBlankHtml(value) ? "" : value;
            Preferences.Signature = "";
            InfoMessage = Strings.Settings_SignatureSaved;
        }
    }

    public bool ShowNotifications
    {
        get => Preferences.ShowNotifications;
        set => Preferences.ShowNotifications = value;
    }

    private static bool IsBlankHtml(string html) => MailFormat.HtmlToPlainText(html).Length == 0 && !html.Contains("<img", StringComparison.OrdinalIgnoreCase);

    public int ThemeIndex
    {
        get => (int)Preferences.Theme;
        set => Preferences.Theme = (AppTheme)Math.Clamp(value, 0, 2);
    }

    /// <summary>0 = compact, 1 = comfortable folder list.</summary>
    public int FolderDensityIndex
    {
        get => Preferences.CompactFolderList ? 0 : 1;
        set => Preferences.CompactFolderList = value == 0;
    }

    /// <summary>Index into <see cref="Languages"/>; unknown stored values count as "follow Windows".</summary>
    public int LanguageIndex
    {
        get => Math.Max(0, Array.IndexOf(Languages, Preferences.Language));
        set => Preferences.Language = Languages[Math.Clamp(value, 0, Languages.Length - 1)];
    }

    /// <summary>Installed font families, supplied by the app. The list starts with <see cref="DefaultFontLabel"/>.</summary>
    public IReadOnlyList<string> FontChoices
    {
        get => _fontChoices;
        init => _fontChoices = [DefaultFontLabel, .. value.Where(f => f != DefaultFontLabel).Distinct().Order(StringComparer.CurrentCultureIgnoreCase)];
    }
    private readonly IReadOnlyList<string> _fontChoices = [DefaultFontLabel];

    public string UiFontChoice
    {
        get => ToChoice(Preferences.UiFontFamily);
        set
        {
            if (value is not null) Preferences.UiFontFamily = FromChoice(value); // null while the ComboBox fills its list
        }
    }

    public int UiTextScaleIndex
    {
        get => IndexOf(UiTextScales, Preferences.UiTextScale, 1);
        set => Preferences.UiTextScale = UiTextScales[Math.Clamp(value, 0, UiTextScales.Length - 1)];
    }

    /// <summary>The interface font or size differs from what this run started with.</summary>
    public bool IsRestartNeeded => Preferences.UiFontFamily != _startUiFont || Preferences.UiTextScale != _startUiScale;

    /// <summary>The interface language differs from what this run started with.</summary>
    public bool IsLanguageRestartNeeded => Preferences.Language != _startLanguage;

    public string ReadingFontChoice
    {
        get => ToChoice(Preferences.ReadingFontFamily);
        set
        {
            if (value is not null) Preferences.ReadingFontFamily = FromChoice(value); // null while the ComboBox fills its list
        }
    }

    public bool ReadingFontOverride
    {
        get => Preferences.ReadingFontOverride;
        set => Preferences.ReadingFontOverride = value;
    }

    public int ReadingZoomIndex
    {
        get => IndexOf(ReadingZooms, Preferences.ReadingZoom, 2);
        set => Preferences.ReadingZoom = ReadingZooms[Math.Clamp(value, 0, ReadingZooms.Length - 1)];
    }

    public string ComposeFontChoice
    {
        get => ToChoice(Preferences.ComposeFontFamily);
        set
        {
            if (value is not null) Preferences.ComposeFontFamily = FromChoice(value); // null while the ComboBox fills its list
        }
    }

    public int ComposeFontSizeIndex
    {
        get => IndexOf(ComposeFontSizes, Preferences.ComposeFontSize, 2);
        set => Preferences.ComposeFontSize = ComposeFontSizes[Math.Clamp(value, 0, ComposeFontSizes.Length - 1)];
    }

    /// <summary>Asks the app to restart so interface font and language changes take effect.</summary>
    public event EventHandler? RestartRequested;

    [RelayCommand]
    private void Restart() => RestartRequested?.Invoke(this, EventArgs.Empty);

    private static string ToChoice(string family) => string.IsNullOrWhiteSpace(family) ? DefaultFontLabel : family;
    private static string FromChoice(string? choice) => string.IsNullOrWhiteSpace(choice) || choice == DefaultFontLabel ? "" : choice;

    private static int IndexOf(int[] options, int value, int fallback)
    {
        var index = Array.IndexOf(options, value);
        return index >= 0 ? index : fallback;
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ForgetPasswordCommand))]
    private bool _hasSavedPassword;

    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _infoMessage;

    public event EventHandler? SignedOut;

    private void OnPreferenceChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(UserPreferences.MarkAsRead):
                OnPropertyChanged(nameof(MarkAsReadIndex));
                OnPropertyChanged(nameof(IsDelayEnabled));
                break;
            case nameof(UserPreferences.MarkAsReadDelaySeconds): OnPropertyChanged(nameof(MarkAsReadDelaySeconds)); break;
            case nameof(UserPreferences.RefreshIntervalSeconds): OnPropertyChanged(nameof(RefreshIndex)); break;
            case nameof(UserPreferences.AlwaysShowRemoteImages): OnPropertyChanged(nameof(AlwaysShowRemoteImages)); break;
            case nameof(UserPreferences.SignatureHtml): OnPropertyChanged(nameof(SignatureHtml)); break;
            case nameof(UserPreferences.ShowNotifications): OnPropertyChanged(nameof(ShowNotifications)); break;
            case nameof(UserPreferences.MinimizeToTray): OnPropertyChanged(nameof(MinimizeToTray)); break;
            case nameof(UserPreferences.UiFontFamily):
                OnPropertyChanged(nameof(UiFontChoice));
                OnPropertyChanged(nameof(IsRestartNeeded));
                break;
            case nameof(UserPreferences.UiTextScale):
                OnPropertyChanged(nameof(UiTextScaleIndex));
                OnPropertyChanged(nameof(IsRestartNeeded));
                break;
            case nameof(UserPreferences.ReadingFontFamily): OnPropertyChanged(nameof(ReadingFontChoice)); break;
            case nameof(UserPreferences.ReadingFontOverride): OnPropertyChanged(nameof(ReadingFontOverride)); break;
            case nameof(UserPreferences.ReadingZoom): OnPropertyChanged(nameof(ReadingZoomIndex)); break;
            case nameof(UserPreferences.ComposeFontFamily): OnPropertyChanged(nameof(ComposeFontChoice)); break;
            case nameof(UserPreferences.ComposeFontSize): OnPropertyChanged(nameof(ComposeFontSizeIndex)); break;
            case nameof(UserPreferences.Theme): OnPropertyChanged(nameof(ThemeIndex)); break;
            case nameof(UserPreferences.CompactFolderList): OnPropertyChanged(nameof(FolderDensityIndex)); break;
            case nameof(UserPreferences.Language):
                OnPropertyChanged(nameof(LanguageIndex));
                OnPropertyChanged(nameof(IsLanguageRestartNeeded));
                break;
        }

        try
        {
            _store.SavePreferences(Preferences);
            ErrorMessage = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorMessage = Loc.Format(Strings.Settings_SaveFailed, ex.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(HasSavedPassword))]
    private void ForgetPassword()
    {
        _credentials.RemovePassword(Account.EffectiveUserName);
        HasSavedPassword = false;
        InfoMessage = Strings.Settings_PasswordForgotten;
    }

    /// <summary>Signs out: forgets the stored password so the next start shows the sign-in page.</summary>
    [RelayCommand]
    private void SignOut()
    {
        _credentials.RemovePassword(Account.EffectiveUserName);
        SignedOut?.Invoke(this, EventArgs.Empty);
    }
}
