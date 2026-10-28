using ExchangeMail.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace ExchangeMail.App.Helpers;

/// <summary>
/// Whether message bodies and editors (WebView2) are shown dark: in dark theme, unless turned off in Settings. The
/// page then offers a dark color scheme (see MailFormat.ApplyReadingPreferences) and WebView2 reports
/// prefers-color-scheme: dark, so messages written with a dark variant use it.
/// </summary>
internal static class WebViewTheme
{
    private static readonly Windows.UI.Color DarkPage = ColorHelper.FromArgb(0xFF,
        Convert.ToByte(MailFormat.DarkPageBackground[1..3], 16),
        Convert.ToByte(MailFormat.DarkPageBackground[3..5], 16),
        Convert.ToByte(MailFormat.DarkPageBackground[5..7], 16));

    public static bool IsDark(FrameworkElement element) =>
        App.Preferences.DarkMessageBodies && element.ActualTheme == ElementTheme.Dark;

    /// <summary>
    /// Sets the background shown before and around the page and the scheme pages see. The scheme belongs to the
    /// shared WebView2 profile, so every viewer and editor reports the same one.
    /// </summary>
    public static void Apply(WebView2 view, bool darkPage)
    {
        view.DefaultBackgroundColor = darkPage ? DarkPage : Colors.White;
        if (view.CoreWebView2 is { } core)
            core.Profile.PreferredColorScheme = IsDark(view) ? CoreWebView2PreferredColorScheme.Dark : CoreWebView2PreferredColorScheme.Light;
    }
}
