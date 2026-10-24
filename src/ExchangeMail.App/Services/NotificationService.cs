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

    /// <summary>A notification was clicked; the argument is the message's item id (null for "several new messages").</summary>
    public event EventHandler<string?>? MessageRequested;

    public void Register()
    {
        try
        {
            AppNotificationManager.Default.NotificationInvoked += OnInvoked;
            // Unpackaged apps pass the name and icon shown on their notifications.
            AppNotificationManager.Default.Register(ViewModels.AppInfo.Name, new Uri(MainWindow.IconPath));
            _registered = true;
        }
        catch (Exception ex)
        {
            // Notifications are a convenience; mail keeps working without them.
            Debug.WriteLine("Bildirimler kullanılamıyor: " + ex);
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
                .AddText(mail.From?.Display ?? "Yeni ileti")
                .AddText(string.IsNullOrWhiteSpace(mail.Subject) ? "(Konu yok)" : mail.Subject);
            if (mail.Preview.Length > 0)
                builder.AddText(mail.Preview.Length > 140 ? mail.Preview[..140] + "…" : mail.Preview);
        }
        else
        {
            var senders = items.Select(i => i.From?.Display).OfType<string>().Distinct().Take(3);
            builder.AddArgument("action", "inbox")
                .AddText($"{items.Count} yeni ileti")
                .AddText(string.Join(", ", senders));
        }

        try
        {
            AppNotificationManager.Default.Show(builder.BuildNotification());
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
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
            Debug.WriteLine(ex);
        }
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
