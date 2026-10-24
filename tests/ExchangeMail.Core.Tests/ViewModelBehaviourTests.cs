using System.Globalization;
using ExchangeMail.Core.Ews;
using ExchangeMail.Core.Models;
using ExchangeMail.ViewModels;

namespace ExchangeMail.Core.Tests;

public class ViewModelBehaviourTests
{
    private static MailSession Session(UserPreferences? preferences = null) => new(
        EwsClient.Create(new Uri("https://mail.test.local/EWS/Exchange.asmx"), System.Net.CredentialCache.DefaultNetworkCredentials),
        new AccountSettings { Email = "ben@sirket.com" },
        preferences);

    [Fact]
    public void Preferences_round_trip_through_settings_store()
    {
        var dir = Path.Combine(Path.GetTempPath(), "exchangemail-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SettingsStore(dir);
            store.SavePreferences(new UserPreferences
            {
                MarkAsRead = MarkAsReadMode.AfterDelay, MarkAsReadDelaySeconds = 7, Signature = "Saygılarımla\nAli", Theme = AppTheme.Dark,
            });

            var loaded = store.LoadPreferences();

            Assert.Equal(MarkAsReadMode.AfterDelay, loaded.MarkAsRead);
            Assert.Equal(7, loaded.MarkAsReadDelaySeconds);
            Assert.Equal("Saygılarımla\nAli", loaded.Signature);
            Assert.Equal(AppTheme.Dark, loaded.Theme);
            Assert.Contains("\"AfterDelay\"", File.ReadAllText(Path.Combine(dir, "preferences.json")));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Settings_changes_are_saved_immediately()
    {
        var dir = Path.Combine(Path.GetTempPath(), "exchangemail-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SettingsStore(dir);
            var session = Session();
            var vm = new SettingsViewModel(session, store, new MemoryCredentials());

            vm.MarkAsReadIndex = 2;
            vm.RefreshIndex = 3;

            Assert.Equal(MarkAsReadMode.Never, store.LoadPreferences().MarkAsRead);
            Assert.Equal(300, store.LoadPreferences().RefreshIntervalSeconds);
            Assert.Equal(MarkAsReadMode.Never, session.Preferences.MarkAsRead);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void New_message_starts_with_html_signature_or_converted_legacy_signature()
    {
        var html = new ComposeViewModel(Session(new UserPreferences { SignatureHtml = "<b>Ali</b>" }), ComposeMode.New, null);
        Assert.Equal("<p><br></p><p><br></p><b>Ali</b>", html.InitialHtml);
        Assert.False(html.HasAttachmentChanges);

        var legacy = new ComposeViewModel(Session(new UserPreferences { Signature = "Ali Veli\nBT" }), ComposeMode.New, null);
        Assert.EndsWith("Ali Veli<br>BT</div>", legacy.InitialHtml);

        var none = new ComposeViewModel(Session(), ComposeMode.New, null);
        Assert.Equal("<p><br></p>", none.InitialHtml);
    }

    [Fact]
    public void Week_range_starts_on_culture_first_day()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
        try
        {
            var vm = new CalendarViewModel(Session()) { };
            // AnchorDate setter triggers a load; the range itself is pure.
            var (start, end) = RangeFor(vm, new DateTime(2026, 10, 29), CalendarRange.Week);
            Assert.Equal(new DateTime(2026, 10, 26), start); // Monday
            Assert.Equal(new DateTime(2026, 11, 2), end);

            (start, end) = RangeFor(vm, new DateTime(2026, 10, 29), CalendarRange.Month);
            Assert.Equal(new DateTime(2026, 10, 1), start);
            Assert.Equal(new DateTime(2026, 11, 1), end);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    private static (DateTime, DateTime) RangeFor(CalendarViewModel vm, DateTime anchor, CalendarRange range)
    {
        // Set the backing state without hitting the network: GetRange only reads AnchorDate and Range.
        typeof(CalendarViewModel).GetField("_anchorDate", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(vm, anchor);
        typeof(CalendarViewModel).GetField("_range", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(vm, range);
        return vm.GetRange();
    }

    [Fact]
    public void Tray_and_startup_settings_are_applied_and_saved()
    {
        var dir = Path.Combine(Path.GetTempPath(), "postaci-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SettingsStore(dir);
            var startup = new FakeStartup();
            var vm = new SettingsViewModel(Session(), store, new MemoryCredentials()) { Startup = startup };
            Assert.True(vm.MinimizeToTray);
            Assert.True(vm.CanStartWithWindows);

            vm.MinimizeToTray = false;
            vm.StartWithWindows = true;

            Assert.False(store.LoadPreferences().MinimizeToTray);
            Assert.True(startup.IsEnabled);
            Assert.True(vm.StartWithWindows);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    private sealed class FakeStartup : IStartupService
    {
        public bool IsEnabled { get; private set; }
        public void SetEnabled(bool enabled) => IsEnabled = enabled;
    }

    private sealed class MemoryCredentials : ICredentialStore
    {
        private readonly Dictionary<string, string> _store = [];
        public string? GetPassword(string userName) => _store.GetValueOrDefault(userName);
        public void SavePassword(string userName, string password) => _store[userName] = password;
        public void RemovePassword(string userName) => _store.Remove(userName);
    }
}
