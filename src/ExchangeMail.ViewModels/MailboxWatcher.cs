using ExchangeMail.Core.Ews;
using ExchangeMail.Core.Models;

namespace ExchangeMail.ViewModels;

/// <summary>
/// Keeps an EWS streaming subscription open so changes arrive within seconds instead of at the next poll.
/// Reconnects when the server closes the connection, subscribes again when the subscription is lost and backs off
/// while the server is unreachable; the regular poll keeps working the whole time as a safety net.
/// </summary>
public sealed class MailboxWatcher(EwsClient client)
{
    /// <summary>Exchange closes a streaming connection after this many minutes; we reconnect right away.</summary>
    public int ConnectionMinutes { get; init; } = 10;

    public TimeSpan FirstRetryDelay { get; init; } = TimeSpan.FromSeconds(15);
    public TimeSpan MaxRetryDelay { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Raised with each batch of events. An empty batch after a reconnect means changes may have been missed while
    /// disconnected, so the listener should refresh anyway.
    /// </summary>
    public event EventHandler<IReadOnlyList<MailboxEvent>>? Changed;

    public bool IsConnected { get; private set; }

    public async Task RunAsync(CancellationToken ct)
    {
        var delay = FirstRetryDelay;
        var hadOutage = false;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var subscription = await client.SubscribeToStreamingAsync(ct);
                IsConnected = true;
                Diagnostics.Log("Streaming notifications connected");
                if (hadOutage) Changed?.Invoke(this, []);
                hadOutage = false;
                delay = FirstRetryDelay;
                while (!ct.IsCancellationRequested)
                    await client.ReceiveStreamingEventsAsync(subscription, ConnectionMinutes, events => Changed?.Invoke(this, events), ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (ex is EwsException or HttpRequestException or IOException)
            {
                // Lost subscription, server restart, network or a proxy that cuts long requests: try again later.
                Diagnostics.Log($"Streaming notifications (retry in {delay.TotalSeconds:0} s)", ex);
            }

            IsConnected = false;
            hadOutage = true;
            try
            {
                await Task.Delay(delay, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaxRetryDelay.Ticks));
        }
        IsConnected = false;
    }
}
