using System.Runtime.InteropServices;
using ExchangeMail.App.Helpers;
using ExchangeMail.App.Services;
using ExchangeMail.App.Views;
using ExchangeMail.ViewModels;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Animation;

namespace ExchangeMail.App;

public sealed partial class MainWindow : Window
{
    public static string IconPath { get; } = Path.Combine(AppContext.BaseDirectory, "Assets", "Postaci.ico");
    private static string UnreadIconPath { get; } = Path.Combine(AppContext.BaseDirectory, "Assets", "Postaci-unread.ico");

    private TrayIcon? _tray;
    private bool _exitRequested;

    public MainWindow()
    {
        InitializeComponent();
        Title = AppInfo.Name;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.SetIcon(IconPath);
        WindowHelper.Resize(this, 1440, 900);
        ApplyTheme(App.Preferences.Theme);
        // Plain text (no style) inherits the interface font and size from the frame.
        if (App.InterfaceFontFamily is { } font) RootFrame.FontFamily = font;
        if (Ui.TextScale != 1) RootFrame.FontSize = 14 * Ui.TextScale;

        try
        {
            _tray = new TrayIcon(IconPath, UnreadIconPath, AppInfo.Name);
            _tray.Clicked += (_, _) => BringToFront();
            _tray.MenuProvider = BuildTrayMenu;
        }
        catch (Exception ex)
        {
            // Without a tray icon the window simply closes normally.
            System.Diagnostics.Debug.WriteLine("Could not create the tray icon: " + ex);
        }

        AppWindow.Closing += OnClosing;
        Closed += (_, _) => _tray?.Dispose();

        ShowLogin(autoConnect: true);
    }

    public ElementTheme CurrentTheme => (Content as FrameworkElement)?.RequestedTheme ?? ElementTheme.Default;

    public void ApplyTheme(AppTheme theme)
    {
        if (Content is FrameworkElement root) root.RequestedTheme = Ui.ToElementTheme(theme);
    }

    public void ShowLogin(bool autoConnect)
    {
        RootFrame.Navigate(typeof(LoginPage), autoConnect, new DrillInNavigationTransitionInfo());
        RootFrame.BackStack.Clear();
    }

    public void ShowMail(MailSession session)
    {
        RootFrame.Navigate(typeof(ShellPage), session, new DrillInNavigationTransitionInfo());
        RootFrame.BackStack.Clear();
    }

    /// <summary>Shows the window (also when hidden in the tray or minimized) and gives it focus.</summary>
    public void BringToFront()
    {
        AppWindow.Show();
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
            presenter.Restore();
        Activate();
        SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
    }

    /// <summary>Hides the window; the app keeps checking mail and stays reachable from the tray icon.</summary>
    public void HideToTray(bool showHint = true)
    {
        if (_tray is null) return;
        AppWindow.Hide();

        var preferences = App.Preferences;
        if (showHint && !preferences.TrayHintShown)
        {
            preferences.TrayHintShown = true;
            App.SavePreferences();
            App.Notifications.ShowInfo(Loc.Format(Strings.Tray_StillRunningTitle, AppInfo.Name), Strings.Tray_StillRunningText);
        }
    }

    /// <summary>Updates the tray icon and tooltip with the inbox's unread count.</summary>
    public void SetUnreadCount(int unread) =>
        _tray?.Update(unread > 0, unread > 0
            ? Loc.Format(unread == 1 ? Strings.Tray_UnreadTooltip_One : Strings.Tray_UnreadTooltip, AppInfo.Name, unread)
            : AppInfo.Name);

    /// <summary>Really closes the app (tray menu "Çıkış").</summary>
    public void Exit()
    {
        _exitRequested = true;
        Close();
    }

    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_exitRequested || _tray is null || !App.Preferences.MinimizeToTray) return;
        args.Cancel = true;
        HideToTray();
    }

    private IReadOnlyList<TrayMenuItem> BuildTrayMenu()
    {
        var shell = ShellPage.Current;
        return
        [
            new TrayMenuItem(Loc.Format(Strings.Tray_Open, AppInfo.Name), BringToFront, IsDefault: true),
            new TrayMenuItem(Strings.Mail_New, () =>
            {
                BringToFront();
                shell?.Mail.NewMailCommand.Execute(null);
            }, Enabled: shell is not null),
            new TrayMenuItem(Strings.Tray_CheckMail, () => shell?.RefreshNow(), Enabled: shell is not null),
            TrayMenuItem.Separator,
            new TrayMenuItem(Strings.Tray_Exit, Exit),
        ];
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hwnd);
}
