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
    private const string Document = """
        <!DOCTYPE html><html><head><meta charset="utf-8">
        <meta http-equiv="Content-Security-Policy" content="img-src data:; style-src 'unsafe-inline'">
        <style>
          html,body{height:100%;margin:0}
          body{box-sizing:border-box;padding:12px 14px;color:#1a1a1a;background:#fff;outline:none;overflow-wrap:break-word}
          p{margin:0} img{max-width:100%} a{color:#0563c1}
          blockquote{margin:0 0 0 .8ex;border-left:2px solid #ccc;padding-left:1ex}
        </style></head><body contenteditable="true" spellcheck="true" lang="tr"></body></html>
        """;

    private static readonly (string Name, string Color)[] Colors =
        [("Siyah", "#000000"), ("Kırmızı", "#C00000"), ("Mavi", "#0563C1"), ("Yeşil", "#00B050"), ("Turuncu", "#ED7D31"), ("Gri", "#7F7F7F")];

    private readonly WebView2 _web = new();
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private string _html = "";
    private bool _initStarted;

    public HtmlEditor()
    {
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Thickness(0, 0, 0, 6) };
        toolbar.Children.Add(ToolButton("", "Kalın (Ctrl+B)", () => ExecAsync("bold")));
        toolbar.Children.Add(ToolButton("", "İtalik (Ctrl+I)", () => ExecAsync("italic")));
        toolbar.Children.Add(ToolButton("", "Altı çizili (Ctrl+U)", () => ExecAsync("underline")));
        toolbar.Children.Add(ColorButton());
        toolbar.Children.Add(SizeButton());
        toolbar.Children.Add(Separator());
        toolbar.Children.Add(ToolButton("", "Madde işaretli liste", () => ExecAsync("insertUnorderedList")));
        toolbar.Children.Add(TextButton("1.", "Numaralı liste", () => ExecAsync("insertOrderedList")));
        toolbar.Children.Add(ToolButton("", "Bağlantı ekle", InsertLinkAsync));
        toolbar.Children.Add(Separator());
        toolbar.Children.Add(ToolButton("", "Biçimlendirmeyi temizle", () => ExecAsync("removeFormat")));

        var frame = new Border
        {
            Child = _web,
            Background = new SolidColorBrush(Microsoft.UI.Colors.White),
            BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(0x40, 0x80, 0x80, 0x80)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
        };

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
            if (e.PropertyName is not (nameof(ViewModels.UserPreferences.ComposeFontFamily) or nameof(ViewModels.UserPreferences.ComposeFontSize))) return;
            if (!_ready.Task.IsCompletedSuccessfully) return;
            try { await ApplyDefaultFontAsync(); }
            catch (Exception) { /* editor closed */ }
        };
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
            Title = "Bağlantı ekle",
            Content = box,
            PrimaryButtonText = "Ekle",
            CloseButtonText = "İptal",
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
        return DropDown("", "Yazı rengi", flyout);
    }

    private DropDownButton SizeButton()
    {
        var flyout = new MenuFlyout();
        foreach (var (name, size) in new[] { ("Küçük", "2"), ("Normal", "3"), ("Büyük", "5"), ("Çok büyük", "6") })
        {
            var item = new MenuFlyoutItem { Text = name };
            item.Click += async (_, _) => await ExecAsync("fontSize", size);
            flyout.Items.Add(item);
        }
        return DropDown("", "Yazı boyutu", flyout);
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
