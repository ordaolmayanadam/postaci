using System.Diagnostics;
using ExchangeMail.Core.Models;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using Microsoft.Windows.BadgeNotifications;

namespace ExchangeMail.App.Services;

/// <summary>Windows toast notifications for new mail and the unread badge on the taskbar icon.</summary>
public sealed class NotificationService
{
    private bool _registered;
    private bool _launchRequested;
    private string? _launchMessageId;

    /// <summary>A notification was clicked; the argument is the message's item id (null for "several new messages").</summary>
    public event EventHandler<string?>? MessageRequested;

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
        args.Arguments.TryGetValue("id", out var id);
        _launchRequested = true;
        _launchMessageId = id;
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
        args.Arguments.TryGetValue("id", out var id);
        // Raised on a background thread.
        App.MainWindow.DispatcherQueue.TryEnqueue(() =>
        {
            App.MainWindow.BringToFront();
            MessageRequested?.Invoke(this, id);
        });
    }
}
