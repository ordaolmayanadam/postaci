using System.Diagnostics;
using ExchangeMail.App.Helpers;
using ExchangeMail.App.Services;
using ExchangeMail.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;
using Windows.Storage;

namespace ExchangeMail.App;

public partial class App : Application
{
    public App()
    {
        // The package's own app data folder (removed with the app); the zip releases' settings are copied over once.
        // The demo mailbox keeps its settings apart, so the real account is neither migrated into it nor used.
        var dataDirectory = ApplicationData.Current.LocalFolder.Path;
        AppInfo.UseDataDirectory(DemoMode.IsRequested ? Path.Combine(dataDirectory, "Demo") : dataDirectory);
        Diagnostics.LogDirectory = AppInfo.DataDirectory;
        if (!DemoMode.IsRequested) AppInfo.MigrateEarlierData();
        Settings = new SettingsStore();
        Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", Path.Combine(AppInfo.DataDirectory, "WebView2"));

        HtmlViewer.CleanupTempFiles();

        // Crashes and lost async errors go to the diagnostics log (Settings → About), so they can be reported.
        UnhandledException += (_, e) => Diagnostics.Log("Unhandled UI exception", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) Diagnostics.Log("Unhandled exception", ex);
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Diagnostics.Log("Unobserved task exception", e.Exception);
            e.SetObserved();
        };

        InitializeComponent();
    }

    public static MainWindow MainWindow { get; private set; } = null!;
    /// <summary>Created once the data folder is known (see the constructor).</summary>
    public static SettingsStore Settings { get; private set; } = null!;
    public static ICredentialStore Credentials { get; } = new CredentialManagerStore();
    public static NotificationService Notifications { get; } = new();
    public static IStartupService Startup { get; } = new StartupTaskService();

    /// <summary>The single live preferences instance (the settings page edits it, everything else reads it).</summary>
    public static UserPreferences Preferences { get; private set; } = null!;

    /// <summary>Started by Windows sign-in (the startup task): stay hidden in the tray unless sign-in needs the user.</summary>
    public static bool StartedInTray { get; private set; }

    public static void SavePreferences()
    {
        try { Settings.SavePreferences(Preferences); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* not fatal */ }
    }

    /// <summary>The interface font chosen in Settings, or null for the Windows default.</summary>
    public static FontFamily? InterfaceFontFamily { get; private set; }

    /// <summary>Starts a new instance and ends this one (interface fonts and language only change at startup).</summary>
    public static void Restart()
    {
        // The process ends inside Restart, so the tray icon and notification registration are released first.
        MainWindow.RemoveTrayIcon();
        Notifications.Unregister();
        Program.ReleaseInstanceKey();
        var failure = AppInstance.Restart("");
        Diagnostics.Log($"Restart failed: {failure}");
        MainWindow.Exit();
    }

    /// <summary>
    /// Applies the interface font and text size before any page is created. WinUI controls read the font from the
    /// ContentControlThemeFontFamily / ControlContentThemeFontSize resources; the text styles (Caption, Body, ...)
    /// have theirs built in, so they are shadowed by copies with the chosen font and size.
    /// </summary>
    private void ApplyInterfaceFonts()
    {
        var scale = Math.Clamp(Preferences.UiTextScale, 50, 200) / 100.0;
        Ui.TextScale = scale;
        InterfaceFontFamily = string.IsNullOrWhiteSpace(Preferences.UiFontFamily) ? null : new FontFamily(Preferences.UiFontFamily);
        if (InterfaceFontFamily is null && scale == 1) return;

        if (InterfaceFontFamily is not null) Resources["ContentControlThemeFontFamily"] = InterfaceFontFamily;
        if (scale != 1)
        {
            Resources["ControlContentThemeFontSize"] = 14 * scale;
            Resources["ContentControlFontSize"] = 14 * scale;
        }

        foreach (var (key, size) in new[]
                 {
                     ("BaseTextBlockStyle", 14), ("BodyTextBlockStyle", 14), ("BodyStrongTextBlockStyle", 14),
                     ("CaptionTextBlockStyle", 12), ("SubtitleTextBlockStyle", 20), ("TitleTextBlockStyle", 28),
                     ("TitleLargeTextBlockStyle", 40), ("DisplayTextBlockStyle", 68),
                 })
        {
            if (!Resources.TryGetValue(key, out var value) || value is not Style original) continue;
            var style = new Style(typeof(TextBlock)) { BasedOn = original };
            if (InterfaceFontFamily is not null) style.Setters.Add(new Setter(TextBlock.FontFamilyProperty, InterfaceFontFamily));
            style.Setters.Add(new Setter(TextBlock.FontSizeProperty, size * scale));
            Resources[key] = style;
        }
    }

    /// <summary>Built-in WinUI texts (date pickers, context menus, ...) come from MRT, which has its own language list.</summary>
    private static void ApplyControlLanguage()
    {
        try
        {
            Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = System.Globalization.CultureInfo.CurrentCulture.Name;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or ArgumentException)
        {
            Debug.WriteLine("Could not set the control language: " + ex); // controls then follow Windows
        }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Preferences = Settings.LoadPreferences();
        if (DemoMode.IsRequested) DemoMode.ApplyTo(Preferences);
        // Before any window exists: XAML reads its texts once, when it is loaded.
        Loc.Apply(Loc.Resolve(Preferences.Language, System.Globalization.CultureInfo.CurrentUICulture));
        ApplyControlLanguage();
        ApplyInterfaceFonts();

        // Registering first lets a click on a notification of an earlier session reach this instance.
        Notifications.Register();
        var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
        StartedInTray = activation.Kind == ExtendedActivationKind.StartupTask;
        if (activation.Kind == ExtendedActivationKind.AppNotification && activation.Data is AppNotificationActivatedEventArgs clicked)
            Notifications.HandleLaunch(clicked);

        MainWindow = new MainWindow();
        MainWindow.Closed += (_, _) => Notifications.Unregister();
        MainWindow.Activate();
#if DEBUG
        if (DemoMode.IsRequested) MainWindow.ShowMail(DemoMode.CreateSession(Preferences));
#endif
        if (StartedInTray) MainWindow.HideToTray(showHint: false);
    }
}
