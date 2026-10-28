using ExchangeMail.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Windows.System;

namespace ExchangeMail.App.Helpers;

/// <summary>
/// Shows untrusted e-mail/calendar HTML in a WebView2: scripts off, links open in the default browser.
/// The HTML itself carries a Content-Security-Policy (see MailFormat.PrepareForDisplay).
/// </summary>
public sealed class HtmlViewer
{
    // NavigateToString is limited to 2 MB; larger bodies (big inline images) go through a mapped temp folder.
    private const int MaxInlineHtmlLength = 1_500_000;
    private const string VirtualHost = "message.exchangemail.invalid";
    private const string Empty = "<!DOCTYPE html><html><body></body></html>";

    private readonly WebView2 _view;
    private readonly string _tempFolder = Path.Combine(TempRoot, Guid.NewGuid().ToString("N"));
    private bool _ready;
    private string? _pending;
    private bool _forceLight;

    public HtmlViewer(WebView2 view)
    {
        _view = view;
        // Reading font, zoom and the dark setting apply to the open message right away, and so does a theme change.
        App.Preferences.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(UserPreferences.ReadingFontFamily) or nameof(UserPreferences.ReadingFontOverride)
                or nameof(UserPreferences.ReadingZoom) or nameof(UserPreferences.DarkMessageBodies))
                Show(_pending);
            if (e.PropertyName == nameof(UserPreferences.DarkMessageBodies)) DarkChanged?.Invoke(this, EventArgs.Empty);
        };
        _view.ActualThemeChanged += (_, _) =>
        {
            Show(_pending);
            DarkChanged?.Invoke(this, EventArgs.Empty);
        };
    }

    /// <summary>Messages are shown dark (dark theme, and the setting is on).</summary>
    public bool IsDark => WebViewTheme.IsDark(_view);

    /// <summary>Raised when <see cref="IsDark"/> may have changed.</summary>
    public event EventHandler? DarkChanged;

    /// <summary>Shows the current message with a light background although messages are dark (one that is hard to read dark).</summary>
    public bool ForceLight
    {
        get => _forceLight;
        set
        {
            if (_forceLight == value) return;
            _forceLight = value;
            Show(_pending);
        }
    }

    public static string TempRoot { get; } = Path.Combine(Path.GetTempPath(), ViewModels.AppInfo.Id);

    /// <summary>Removes rendered bodies left over from earlier runs.</summary>
    public static void CleanupTempFiles()
    {
        try
        {
            if (Directory.Exists(TempRoot)) Directory.Delete(TempRoot, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // a file may still be locked by another instance; harmless
        }
    }

    public async Task InitializeAsync()
    {
        await _view.EnsureCoreWebView2Async();
        var core = _view.CoreWebView2;
        core.Settings.IsScriptEnabled = false;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.IsStatusBarEnabled = true;
        core.Settings.AreHostObjectsAllowed = false;
        core.Settings.IsWebMessageEnabled = false;
        core.Settings.IsGeneralAutofillEnabled = false;
        core.Settings.IsPasswordAutosaveEnabled = false;

        Directory.CreateDirectory(_tempFolder);
        core.SetVirtualHostNameToFolderMapping(VirtualHost, _tempFolder, CoreWebView2HostResourceAccessKind.Deny);

        // Links open in the default browser / mail handler instead of inside the reading pane.
        core.NavigationStarting += (_, args) =>
        {
            if (!Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri)) return;
            if (uri.Host == VirtualHost || uri.Scheme is "data" or "about") return;
            args.Cancel = true;
            OpenExternal(uri);
        };
        core.NewWindowRequested += (_, args) =>
        {
            args.Handled = true;
            if (Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri)) OpenExternal(uri);
        };

        _ready = true;
        Show(_pending);
    }

    public void Show(string? html)
    {
        _pending = html;
        if (!_ready) return;
        var p = App.Preferences;
        var dark = IsDark && !_forceLight;
        WebViewTheme.Apply(_view, dark);
        var scheme = dark ? MailFormat.PageColorScheme.Dark
            : IsDark ? MailFormat.PageColorScheme.Light // "Light background" while the profile reports dark
            : MailFormat.PageColorScheme.Default;
        html = MailFormat.ApplyReadingPreferences(html ?? Empty, p.ReadingFontFamily, p.ReadingFontOverride, p.ReadingZoom, scheme);

        if (html.Length <= MaxInlineHtmlLength)
        {
            _view.NavigateToString(html);
            return;
        }

        foreach (var old in Directory.EnumerateFiles(_tempFolder, "*.html"))
        {
            try { File.Delete(old); } catch (IOException) { }
        }
        var name = Guid.NewGuid().ToString("N") + ".html";
        File.WriteAllText(Path.Combine(_tempFolder, name), html);
        _view.CoreWebView2.Navigate($"https://{VirtualHost}/{name}");
    }

    private static void OpenExternal(Uri uri)
    {
        if (uri.Scheme is "http" or "https" or "mailto")
            _ = Launcher.LaunchUriAsync(uri);
    }
}
