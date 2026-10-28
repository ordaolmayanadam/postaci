using System.Text.Json;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;

namespace ExchangeMail.App.Controls;

/// <summary>
/// Rich text (HTML) editor: a contenteditable document in WebView2 with a formatting toolbar.
/// Page scripts are disabled; the toolbar drives the document through ExecuteScriptAsync,
/// which WebView2 runs even when page scripts are off.
/// </summary>
public sealed partial class HtmlEditor : UserControl
{
    private static string Document => $$"""
        <!DOCTYPE html><html><head><meta charset="utf-8">
        <meta http-equiv="Content-Security-Policy" content="img-src data:; style-src 'unsafe-inline'">
        <style>
          html,body{height:100%;margin:0}
          body{box-sizing:border-box;padding:12px 14px;outline:none;overflow-wrap:break-word}
          p{margin:0} img{max-width:100%} a{color:LinkText}
          blockquote{margin:0 0 0 .8ex;border-left:2px solid #ccc;padding-left:1ex}
        </style></head><body contenteditable="true" spellcheck="true" lang="{{Loc.Current}}"></body></html>
        """;

    private static (string Name, string Color)[] Colors =>
    [
        (Strings.Editor_Black, "#000000"), (Strings.Editor_Red, "#C00000"), (Strings.Editor_Blue, "#0563C1"),
        (Strings.Editor_Green, "#00B050"), (Strings.Editor_Orange, "#ED7D31"), (Strings.Editor_Gray, "#7F7F7F"),
    ];

    private readonly WebView2 _web = new();
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private string _html = "";
    private bool _initStarted;

    public HtmlEditor()
    {
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Thickness(0, 0, 0, 6) };
        toolbar.Children.Add(ToolButton("", Strings.Editor_Bold, () => ExecAsync("bold")));
        toolbar.Children.Add(ToolButton("", Strings.Editor_Italic, () => ExecAsync("italic")));
        toolbar.Children.Add(ToolButton("", Strings.Editor_Underline, () => ExecAsync("underline")));
        toolbar.Children.Add(ColorButton());
        toolbar.Children.Add(SizeButton());
        toolbar.Children.Add(Separator());
        toolbar.Children.Add(ToolButton("", Strings.Editor_BulletList, () => ExecAsync("insertUnorderedList")));
        toolbar.Children.Add(TextButton("1.", Strings.Editor_NumberedList, () => ExecAsync("insertOrderedList")));
        toolbar.Children.Add(ToolButton("", Strings.Editor_InsertLink, InsertLinkAsync));
        toolbar.Children.Add(Separator());
        toolbar.Children.Add(ToolButton("", Strings.Editor_ClearFormatting, () => ExecAsync("removeFormat")));

        var frame = new Border { Child = _web, Style = Helpers.Ui.AppStyle("MessageBodyFrameStyle") };

        var root = new Grid { RowDefinitions = { new RowDefinition { Height = GridLength.Auto }, new RowDefinition() } };
        root.Children.Add(toolbar);
        root.Children.Add(frame);
        Grid.SetRow(frame, 1);
        Content = root;

        Loaded += async (_, _) =>
        {
            try { await EnsureInitializedAsync(); }
            catch (Exception) { /* surfaced by the next Get/Set call */ }
        };
        App.Preferences.PropertyChanged += async (_, e) =>
        {
            if (!_ready.Task.IsCompletedSuccessfully) return;
            try
            {
                if (e.PropertyName is nameof(ViewModels.UserPreferences.ComposeFontFamily) or nameof(ViewModels.UserPreferences.ComposeFontSize))
                    await ApplyDefaultFontAsync();
                else if (e.PropertyName == nameof(ViewModels.UserPreferences.DarkMessageBodies))
                    await ApplyColorSchemeAsync();
            }
            catch (Exception) { /* editor closed */ }
        };
        _web.ActualThemeChanged += async (_, _) =>
        {
            if (!_ready.Task.IsCompletedSuccessfully) return;
            try { await ApplyColorSchemeAsync(); }
            catch (Exception) { /* editor closed */ }
        };
    }

    /// <summary>
    /// Dark or light writing area, like message bodies. Only the document's own style changes; the message's HTML
    /// carries no colors from it, so recipients see it as written.
    /// </summary>
    private Task ApplyColorSchemeAsync()
    {
        var dark = Helpers.WebViewTheme.IsDark(_web);
        Helpers.WebViewTheme.Apply(_web, dark);
        return _web.CoreWebView2.ExecuteScriptAsync(
            $"document.documentElement.style.colorScheme = {Json(dark ? "dark" : "light")};" +
            $"document.documentElement.style.background = {Json(dark ? ViewModels.MailFormat.DarkPageBackground : "")};").AsTask();
    }

    /// <summary>Uses the compose font from Settings (the same font wraps the message when it is sent).</summary>
    private Task ApplyDefaultFontAsync()
    {
        var p = App.Preferences;
        var stack = ViewModels.MailFormat.FontStack(p.ComposeFontFamily, ViewModels.MailFormat.DefaultComposeFont);
        // Set on body's own style, which innerHTML (the message) does not include.
        return _web.CoreWebView2.ExecuteScriptAsync(
            $"document.body.style.fontFamily = {Json(stack)}; document.body.style.fontSize = {Json(p.ComposeFontSize + "pt")};").AsTask();
    }

    public async Task EnsureInitializedAsync()
    {
        if (_initStarted)
        {
            await _ready.Task;
            return;
        }
        _initStarted = true;
        try
        {
            await _web.EnsureCoreWebView2Async();
            var core = _web.CoreWebView2;
            core.Settings.IsScriptEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.IsWebMessageEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.NavigationStarting += (_, args) =>
            {
                if (!args.Uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && args.Uri != "about:blank") args.Cancel = true;
            };
            core.NewWindowRequested += (_, args) => args.Handled = true;
            Helpers.WebViewTheme.Apply(_web, Helpers.WebViewTheme.IsDark(_web)); // no white flash while the document loads

            var loaded = new TaskCompletionSource();
            void OnCompleted(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
            {
                core.NavigationCompleted -= OnCompleted;
                loaded.TrySetResult();
            }
            core.NavigationCompleted += OnCompleted;
            _web.NavigateToString(Document);
            await loaded.Task;

            // Content goes in by script, so large drafts are not limited by NavigateToString's 2 MB cap.
            await core.ExecuteScriptAsync($"document.body.innerHTML = {Json(_html)};");
            await ApplyDefaultFontAsync();
            await ApplyColorSchemeAsync();
            _ready.TrySetResult();
        }
        catch (Exception ex)
        {
            _ready.TrySetException(ex);
            throw;
        }
    }

    public async Task SetHtmlAsync(string html)
    {
        _html = html;
        if (!_ready.Task.IsCompletedSuccessfully) return; // applied when initialization finishes
        await _web.CoreWebView2.ExecuteScriptAsync($"document.body.innerHTML = {Json(html)};");
    }

    public async Task<string> GetHtmlAsync()
    {
        await EnsureInitializedAsync();
        var result = await _web.CoreWebView2.ExecuteScriptAsync("document.body.innerHTML");
        return JsonSerializer.Deserialize<string>(result) ?? "";
    }

    /// <summary>Puts the caret at the start of the text.</summary>
    public async Task FocusEditorAsync()
    {
        await EnsureInitializedAsync();
        _web.Focus(FocusState.Programmatic);
        await _web.CoreWebView2.ExecuteScriptAsync(
            "(function(){var r=document.createRange();r.setStart(document.body,0);r.collapse(true);var s=getSelection();s.removeAllRanges();s.addRange(r);document.body.focus();})();");
    }

    private async Task ExecAsync(string command, string? value = null)
    {
        await EnsureInitializedAsync();
        _web.Focus(FocusState.Programmatic);
        await _web.CoreWebView2.ExecuteScriptAsync($"document.execCommand({Json(command)}, false, {(value is null ? "null" : Json(value))});");
    }

    private async Task InsertLinkAsync()
    {
        var box = new TextBox { PlaceholderText = "https://", MinWidth = 320 };
        var dialog = new ContentDialog
        {
            Title = Strings.Editor_InsertLink,
            Content = box,
            PrimaryButtonText = Strings.Editor_Insert,
            CloseButtonText = Strings.Common_Cancel,
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
            RequestedTheme = ActualTheme,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var url = box.Text.Trim();
        if (url.Length == 0) return;
        if (!url.Contains("://", StringComparison.Ordinal) && !url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            url = (url.Contains('@') ? "mailto:" : "https://") + url;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https" or "mailto")) return;

        await EnsureInitializedAsync();
        _web.Focus(FocusState.Programmatic);
        // With nothing selected, insert the address itself as the link text.
        await _web.CoreWebView2.ExecuteScriptAsync($$"""
            (function(u){
              var s = window.getSelection();
              if (!s || s.isCollapsed) {
                var a = document.createElement('a'); a.href = u; a.textContent = u;
                document.execCommand('insertHTML', false, a.outerHTML);
              } else {
                document.execCommand('createLink', false, u);
              }
            })({{Json(uri.ToString())}});
            """);
    }

    private Button ToolButton(string glyph, string tooltip, Func<Task> action) =>
        Wire(new Button { Content = new FontIcon { Glyph = glyph, FontSize = 14 } }, tooltip, action);

    private Button TextButton(string text, string tooltip, Func<Task> action) =>
        Wire(new Button { Content = new TextBlock { Text = text, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold } }, tooltip, action);

    private static Button Wire(Button button, string tooltip, Func<Task> action)
    {
        button.Padding = new Thickness(8, 5, 8, 5);
        button.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        button.BorderThickness = new Thickness(0);
        ToolTipService.SetToolTip(button, tooltip);
        AutomationHelper.SetName(button, tooltip);
        button.Click += async (_, _) => await action();
        return button;
    }

    private DropDownButton ColorButton()
    {
        var flyout = new MenuFlyout();
        foreach (var (name, color) in Colors)
        {
            var item = new MenuFlyoutItem { Text = name, Icon = new FontIcon { Glyph = "", Foreground = Helpers.Ui.Brush(color) } };
            item.Click += async (_, _) => await ExecAsync("foreColor", color);
            flyout.Items.Add(item);
        }
        return DropDown("", Strings.Editor_TextColor, flyout);
    }

    private DropDownButton SizeButton()
    {
        var flyout = new MenuFlyout();
        foreach (var (name, size) in new[] { (Strings.Editor_SizeSmall, "2"), (Strings.Editor_SizeNormal, "3"), (Strings.Editor_SizeLarge, "5"), (Strings.Editor_SizeHuge, "6") })
        {
            var item = new MenuFlyoutItem { Text = name };
            item.Click += async (_, _) => await ExecAsync("fontSize", size);
            flyout.Items.Add(item);
        }
        return DropDown("", Strings.Editor_TextSize, flyout);
    }

    private static DropDownButton DropDown(string glyph, string tooltip, MenuFlyout flyout)
    {
        var button = new DropDownButton
        {
            Content = new FontIcon { Glyph = glyph, FontSize = 14 },
            Flyout = flyout,
            Padding = new Thickness(8, 5, 6, 5),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
        };
        ToolTipService.SetToolTip(button, tooltip);
        AutomationHelper.SetName(button, tooltip);
        return button;
    }

    private static Border Separator() => new()
    {
        Width = 1,
        Margin = new Thickness(6, 6, 6, 6),
        Background = new SolidColorBrush(ColorHelper.FromArgb(0x40, 0x80, 0x80, 0x80)),
    };

    private static string Json(string value) => JsonSerializer.Serialize(value);

    private static class AutomationHelper
    {
        public static void SetName(UIElement element, string name) =>
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(element, name);
    }
}
