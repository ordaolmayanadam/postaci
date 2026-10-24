using System.Diagnostics;
using ExchangeMail.App.Helpers;
using ExchangeMail.App.Services;
using ExchangeMail.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ExchangeMail.App;

public partial class App : Application
{
    public App()
    {
        // Unpackaged apps would otherwise put the WebView2 profile next to the .exe, which fails under Program Files.
        AppInfo.MigrateLegacyData();
        Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", Path.Combine(AppInfo.DataDirectory, "WebView2"));

        HtmlViewer.CleanupTempFiles();

        InitializeComponent();
    }

    public static MainWindow MainWindow { get; private set; } = null!;
    public static SettingsStore Settings { get; } = new();
    public static ICredentialStore Credentials { get; } = new CredentialManagerStore();
    public static NotificationService Notifications { get; } = new();
    public static IStartupService Startup { get; } = new RegistryStartupService();

    /// <summary>The single live preferences instance (the settings page edits it, everything else reads it).</summary>
    public static UserPreferences Preferences { get; private set; } = null!;

    /// <summary>Started by Windows sign-in with --tray: stay hidden in the tray unless sign-in needs the user.</summary>
    public static bool StartedInTray { get; private set; }

    public static void SavePreferences()
    {
        try { Settings.SavePreferences(Preferences); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* not fatal */ }
    }

    /// <summary>The interface font chosen in Settings, or null for the Windows default.</summary>
    public static FontFamily? InterfaceFontFamily { get; private set; }

    /// <summary>Starts a new instance and closes this one (interface fonts and language only change at startup).</summary>
    public static void Restart()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Debug.WriteLine("Could not restart: " + ex);
            return;
        }
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
        // Before any window exists: XAML reads its texts once, when it is loaded.
        Loc.Apply(Loc.Resolve(Preferences.Language, System.Globalization.CultureInfo.CurrentUICulture));
        ApplyControlLanguage();
        StartedInTray = Environment.GetCommandLineArgs().Contains(RegistryStartupService.TrayArgument, StringComparer.OrdinalIgnoreCase);

        ApplyInterfaceFonts();
        Notifications.Register();
        MainWindow = new MainWindow();
        MainWindow.Closed += (_, _) => Notifications.Unregister();
        MainWindow.Activate();
        if (StartedInTray) MainWindow.HideToTray(showHint: false);
    }
}
