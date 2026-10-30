using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ExchangeMail.Core.Models;
using ExchangeMail.ViewModels;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using Microsoft.Windows.BadgeNotifications;

namespace ExchangeMail.App.Services;

/// <summary>Dismiss or snooze, chosen on a reminder notification.</summary>
public sealed record ReminderAction(EwsItemId Id, TimeSpan? SnoozeFor);

/// <summary>Windows toast notifications for new mail and event reminders, and the unread badge on the taskbar icon.</summary>
public sealed class NotificationService
{
    private const string ReminderGroup = "reminder";
    private static readonly int[] SnoozeMinutes = [5, 10, 15, 30, 60];

    private bool _registered;
    private bool _launchRequested;
    private string? _launchMessageId;
    private ReminderAction? _launchReminderAction;

    /// <summary>A notification was clicked; the argument is the message's item id (null for "several new messages").</summary>
    public event EventHandler<string?>? MessageRequested;

    /// <summary>Dismiss or Snooze was chosen on a reminder.</summary>
    public event EventHandler<ReminderAction>? ReminderActionRequested;

    /// <summary>A reminder itself was clicked.</summary>
    public event EventHandler? CalendarRequested;

    /// <summary>
    /// Registers for clicks on this app's notifications. The name and icon shown on them come from the package
    /// manifest; a click while the app is closed starts it through the COM activator declared there.
    /// </summary>
    public void Register()
    {
        try
        {
            AppNotificationManager.Default.NotificationInvoked += OnInvoked;
            AppNotificationManager.Default.Register();
            _registered = true;
        }
        catch (Exception ex)
        {
            // Notifications are a convenience; mail keeps working without them.
            ViewModels.Diagnostics.Log("Notification registration", ex);
        }
    }

    public void Unregister()
    {
        if (!_registered) return;
        try
        {
            SetBadge(0);
            AppNotificationManager.Default.Unregister();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
        _registered = false;
    }

    public void ShowNewMail(IReadOnlyList<MailSummary> items)
    {
        if (!_registered || items.Count == 0) return;

        var builder = new AppNotificationBuilder();
        if (items.Count == 1)
        {
            var mail = items[0];
            builder.AddArgument("action", "open").AddArgument("id", mail.ItemId.Id)
                .AddText(mail.From?.Display ?? Strings.Notification_NewMessage)
                .AddText(string.IsNullOrWhiteSpace(mail.Subject) ? Strings.Common_NoSubject : mail.Subject);
            if (mail.Preview.Length > 0)
                builder.AddText(mail.Preview.Length > 140 ? mail.Preview[..140] + "…" : mail.Preview);
        }
        else
        {
            var senders = items.Select(i => i.From?.Display).OfType<string>().Distinct().Take(3);
            builder.AddArgument("action", "inbox")
                .AddText(Loc.Count(items.Count, Strings.Notification_NewMessages_One, Strings.Notification_NewMessages))
                .AddText(string.Join(", ", senders));
        }

        try
        {
            AppNotificationManager.Default.Show(builder.BuildNotification());
        }
        catch (Exception ex)
        {
            ViewModels.Diagnostics.Log("New mail notification", ex);
        }
    }

    /// <summary>
    /// An event reminder. It stays on screen until acted on (the reminder scenario). Join opens the meeting address
    /// through Windows, so it works even when Postacı has been closed since; Snooze and Dismiss go to Exchange.
    /// </summary>
    public void ShowReminder(DueReminder due)
    {
        if (!_registered) return;
        var r = due.Reminder;
        var id = r.ItemId;

        var snooze = new AppNotificationComboBox("snooze");
        foreach (var minutes in SnoozeMinutes)
            snooze.AddItem(minutes.ToString(CultureInfo.InvariantCulture),
                minutes == 60 ? Strings.Reminder_SnoozeHour : Loc.Format(Strings.Reminder_SnoozeMinutes, minutes));
        snooze.SetSelectedItem("5");

        var builder = new AppNotificationBuilder()
            .SetScenario(AppNotificationScenario.Reminder)
            .AddArgument("action", "calendar")
            .AddText(string.IsNullOrWhiteSpace(r.Subject) ? Strings.Common_NoSubject : r.Subject)
            .AddText(WhenText(r))
            .AddComboBox(snooze);
        if (due.JoinUrl is { } join)
            builder.AddButton(new AppNotificationButton(Strings.Meeting_Join).SetInvokeUri(new Uri(join)));
        builder.AddButton(new AppNotificationButton(Strings.Reminder_Snooze)
            .AddArgument("action", "snooze").AddArgument("id", id.Id).AddArgument("ck", id.ChangeKey ?? ""));
        builder.AddButton(new AppNotificationButton(Strings.Reminder_Dismiss)
            .AddArgument("action", "dismiss").AddArgument("id", id.Id).AddArgument("ck", id.ChangeKey ?? ""));

        try
        {
            var notification = builder.BuildNotification();
            notification.Tag = ReminderTag(due.Key);
            notification.Group = ReminderGroup;
            AppNotificationManager.Default.Show(notification);
        }
        catch (Exception ex)
        {
            ViewModels.Diagnostics.Log("Reminder notification", ex);
        }
    }

    /// <summary>Takes away a reminder that was dismissed or snoozed elsewhere (in Outlook, on a phone).</summary>
    public async void RemoveReminder(string key)
    {
        if (!_registered) return;
        try
        {
            await AppNotificationManager.Default.RemoveByTagAndGroupAsync(ReminderTag(key), ReminderGroup);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }

    /// <summary>"14:00 – 15:00 · Room 3", with the date when the event is not today.</summary>
    private static string WhenText(EventReminder r)
    {
        var culture = CultureInfo.CurrentCulture;
        var start = r.Start.ToLocalTime();
        var time = $"{start.ToString("t", culture)} – {r.End.ToLocalTime().ToString("t", culture)}";
        if (start.Date != DateTime.Today) time = $"{ViewModels.MailFormat.DayMonth(start.DateTime)} {time}";
        return string.IsNullOrWhiteSpace(r.Location) ? time : $"{time} · {r.Location}";
    }

    /// <summary>Notification tags are limited to 64 characters; item ids are longer.</summary>
    private static string ReminderTag(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..32];

    /// <summary>A plain informational notification (no action when clicked other than opening the app).</summary>
    public void ShowInfo(string title, string text)
    {
        if (!_registered) return;
        try
        {
            AppNotificationManager.Default.Show(new AppNotificationBuilder().AddArgument("action", "open-app").AddText(title).AddText(text).BuildNotification());
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }

    /// <summary>Shows the unread count on the taskbar icon; 0 clears it.</summary>
    public void SetBadge(int unread)
    {
        if (!_registered) return;
        try
        {
            if (unread > 0) BadgeNotificationManager.Current.SetBadgeAsCount((uint)Math.Min(unread, 99));
            else BadgeNotificationManager.Current.ClearBadge();
        }
        catch (Exception ex)
        {
            ViewModels.Diagnostics.Log("Badge notification", ex);
        }
    }

    /// <summary>The app was started by a click on one of its notifications; the shell opens it once signed in.</summary>
    public void HandleLaunch(AppNotificationActivatedEventArgs args)
    {
        // Snooze or Dismiss on a reminder shown before Postacı was closed: done once signed in.
        if (ReminderActionOf(args) is { } reminder)
        {
            _launchReminderAction = reminder;
            return;
        }
        args.Arguments.TryGetValue("action", out var action);
        if (action == "calendar") return;
        args.Arguments.TryGetValue("id", out var id);
        _launchRequested = true;
        _launchMessageId = id;
    }

    /// <summary>The reminder action the app was started with, once.</summary>
    public bool TryTakeLaunchReminderAction(out ReminderAction? action)
    {
        action = _launchReminderAction;
        _launchReminderAction = null;
        return action is not null;
    }

    private static ReminderAction? ReminderActionOf(AppNotificationActivatedEventArgs args)
    {
        if (!args.Arguments.TryGetValue("action", out var action) || action is not ("snooze" or "dismiss")
            || !args.Arguments.TryGetValue("id", out var id))
            return null;
        args.Arguments.TryGetValue("ck", out var changeKey);
        var itemId = new EwsItemId(id, string.IsNullOrEmpty(changeKey) ? null : changeKey);
        if (action == "dismiss") return new ReminderAction(itemId, null);
        var minutes = args.UserInput.TryGetValue("snooze", out var chosen) && int.TryParse(chosen, CultureInfo.InvariantCulture, out var m) ? m : 5;
        return new ReminderAction(itemId, TimeSpan.FromMinutes(minutes));
    }

    /// <summary>The notification the app was started from, once; null id for "several new messages".</summary>
    public bool TryTakeLaunchRequest(out string? messageId)
    {
        messageId = _launchMessageId;
        if (!_launchRequested) return false;
        _launchRequested = false;
        _launchMessageId = null;
        return true;
    }

    private void OnInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        // Raised on a background thread.
        if (ReminderActionOf(args) is { } reminder)
        {
            App.MainWindow.DispatcherQueue.TryEnqueue(() => ReminderActionRequested?.Invoke(this, reminder));
            return;
        }
        args.Arguments.TryGetValue("action", out var action);
        if (action == "calendar")
        {
            App.MainWindow.DispatcherQueue.TryEnqueue(() =>
            {
                App.MainWindow.BringToFront();
                CalendarRequested?.Invoke(this, EventArgs.Empty);
            });
            return;
        }

        args.Arguments.TryGetValue("id", out var id);
        App.MainWindow.DispatcherQueue.TryEnqueue(() =>
        {
            App.MainWindow.BringToFront();
            MessageRequested?.Invoke(this, id);
        });
    }
}
