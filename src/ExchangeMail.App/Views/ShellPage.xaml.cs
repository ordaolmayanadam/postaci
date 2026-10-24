using System.ComponentModel;
using ExchangeMail.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;

namespace ExchangeMail.App.Views;

/// <summary>Signed-in shell: left navigation rail (mail, calendar, contacts, settings) and the background mail poll.</summary>
public sealed partial class ShellPage : Page
{
    private MailSession? _session;
    private DispatcherQueueTimer? _pollTimer;

    public ShellPage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (Nav.SettingsItem is NavigationViewItem settings) settings.Content = "Ayarlar";
        };
    }

    /// <summary>The signed-in shell, for the tray menu; null while signed out.</summary>
    public static ShellPage? Current { get; private set; }

    public MainViewModel Mail { get; private set; } = null!;
    public CalendarViewModel Calendar { get; private set; } = null!;
    public ContactsViewModel Contacts { get; private set; } = null!;
    public SettingsViewModel Settings { get; private set; } = null!;
    public AutoReplyViewModel AutoReply { get; private set; } = null!;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is not MailSession session) return;

        _session = session;
        Mail = new MainViewModel(session);
        Calendar = new CalendarViewModel(session);
        Contacts = new ContactsViewModel(session);
        AutoReply = new AutoReplyViewModel(session);
        Settings = new SettingsViewModel(session, App.Settings, App.Credentials) { AutoReply = AutoReply, Startup = App.Startup, FontChoices = Services.FontCatalog.InstalledFamilies };
        Current = this;
        Bindings.Update();
        _ = AutoReply.LoadAsync();

        Mail.ComposeRequested += OnComposeRequested;
        Mail.NewMailArrived += OnNewMailArrived;
        Mail.PropertyChanged += OnMailPropertyChanged;
        App.Notifications.MessageRequested += OnNotificationClicked;
        Contacts.ComposeRequested += OnComposeRequested;
        Settings.SignedOut += OnSignedOut;
        session.Preferences.PropertyChanged += OnPreferenceChanged;

        _pollTimer = DispatcherQueue.CreateTimer();
        _pollTimer.Interval = TimeSpan.FromSeconds(Math.Max(15, session.Preferences.RefreshIntervalSeconds));
        _pollTimer.Tick += async (_, _) =>
        {
            try { await Mail.BackgroundRefreshAsync(); }
            catch (Exception) { /* transient; the next tick retries */ }
        };
        _pollTimer.Start();

        Nav.SelectedItem = MailItem;
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _pollTimer?.Stop();
        App.Notifications.MessageRequested -= OnNotificationClicked;
        App.Notifications.SetBadge(0);
        App.MainWindow.SetUnreadCount(0);
        if (Current == this) Current = null;
        if (_session is not null) _session.Preferences.PropertyChanged -= OnPreferenceChanged;
    }

    private void OnNewMailArrived(object? sender, IReadOnlyList<ExchangeMail.Core.Models.MailSummary> items)
    {
        if (_session?.Preferences.ShowNotifications == true) App.Notifications.ShowNewMail(items);
    }

    private void OnMailPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.InboxUnreadCount)) return;
        App.Notifications.SetBadge(Mail.InboxUnreadCount);
        App.MainWindow.SetUnreadCount(Mail.InboxUnreadCount);
    }

    /// <summary>Checks for new mail immediately (tray menu).</summary>
    public async void RefreshNow()
    {
        try { await Mail.BackgroundRefreshAsync(); }
        catch (Exception) { /* transient */ }
    }

    private async void OnNotificationClicked(object? sender, string? itemId)
    {
        Nav.SelectedItem = MailItem;
        if (itemId is not null) await Mail.OpenInboxMessageAsync(itemId);
    }

    private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            Show(typeof(SettingsPage), Settings);
            _ = AutoReply.LoadAsync(); // may have been changed in Outlook/OWA meanwhile
            return;
        }

        switch ((args.SelectedItem as NavigationViewItem)?.Tag as string)
        {
            case "mail": Show(typeof(MailPage), Mail); break;
            case "calendar": Show(typeof(CalendarPage), Calendar); break;
            case "contacts": Show(typeof(ContactsPage), Contacts); break;
        }
    }

    private void Show(Type pageType, object viewModel)
    {
        if (ContentFrame.CurrentSourcePageType == pageType) return;
        // Pages use NavigationCacheMode="Required", so each one keeps its state between visits.
        ContentFrame.Navigate(pageType, viewModel, new EntranceNavigationTransitionInfo());
        ContentFrame.BackStack.Clear();
    }

    private void OnNavigationAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        Nav.SelectedItem = args.Element;
    }

    private void OnComposeRequested(object? sender, ComposeViewModel compose) => new ComposeWindow(compose).Activate();

    private void OnPreferenceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_session is null) return;
        switch (e.PropertyName)
        {
            case nameof(UserPreferences.Theme):
                App.MainWindow.ApplyTheme(_session.Preferences.Theme);
                break;
            case nameof(UserPreferences.RefreshIntervalSeconds) when _pollTimer is not null:
                _pollTimer.Interval = TimeSpan.FromSeconds(Math.Max(15, _session.Preferences.RefreshIntervalSeconds));
                break;
        }
    }

    private void OnSignedOut(object? sender, EventArgs e)
    {
        _pollTimer?.Stop();
        _session?.Dispose();
        App.MainWindow.ShowLogin(autoConnect: false);
    }
}
