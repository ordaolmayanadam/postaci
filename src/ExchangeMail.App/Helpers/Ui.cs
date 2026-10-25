using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.UI.Text;

namespace ExchangeMail.App.Helpers;

/// <summary>Small functions used from x:Bind expressions.</summary>
public static class Ui
{
    public static bool HasText(string? value) => !string.IsNullOrEmpty(value);

    public static bool IsNotNull(object? value) => value is not null;

    public static Visibility VisibleIfNot(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    public static FontWeight BoldIf(bool value) => value ? FontWeights.SemiBold : FontWeights.Normal;

    public static Thickness Indent(int depth) => new(depth * 16, 0, 0, 0);

    public static Thickness LeftMargin(double width) => new(width, 0, 0, 0);

    /// <summary>
    /// A theme brush for <paramref name="theme"/>. Application.Resources alone answers for the Windows theme, which is
    /// wrong when Postacı is set to the other one; code-built UI must resolve brushes for its own element's theme.
    /// </summary>
    public static Brush? ThemeBrush(string key, ElementTheme theme)
    {
        var dark = theme == ElementTheme.Dark
                   || (theme == ElementTheme.Default && Application.Current.RequestedTheme == ApplicationTheme.Dark);
        string[] names = dark ? ["Dark", "Default"] : ["Light"];
        if (Find(Application.Current.Resources) is { } found) return found;
        return Application.Current.Resources.TryGetValue(key, out var value) ? value as Brush : null;

        Brush? Find(ResourceDictionary dictionary)
        {
            foreach (var name in names)
            {
                if (dictionary.ThemeDictionaries.TryGetValue(name, out var themed) && themed is ResourceDictionary d
                    && d.TryGetValue(key, out var value) && value is Brush brush)
                    return brush;
            }
            foreach (var merged in dictionary.MergedDictionaries)
            {
                if (Find(merged) is { } brush) return brush;
            }
            return null;
        }
    }

    /// <summary>The theme an element is shown in (its own, else the window's).</summary>
    public static ElementTheme ThemeOf(FrameworkElement element) =>
        element.ActualTheme != ElementTheme.Default ? element.ActualTheme
        : App.MainWindow?.CurrentTheme is { } t && t != ElementTheme.Default ? t
        : Application.Current.RequestedTheme == ApplicationTheme.Dark ? ElementTheme.Dark : ElementTheme.Light;

    /// <summary>Applies a pane width saved from a <see cref="Controls.ColumnSplitter"/> (0 = keep the default).</summary>
    public static void RestoreWidth(ColumnDefinition column, int saved, double? fallback = null)
    {
        double? width = saved > 0 ? saved : fallback;
        if (width is { } w) column.Width = new GridLength(Math.Clamp(w, column.MinWidth, column.MaxWidth));
    }

    /// <summary>Stores a pane width chosen with a splitter in the preferences file.</summary>
    public static void SaveWidth(Action<int> assign, double width)
    {
        assign((int)Math.Round(width));
        App.SavePreferences();
    }

    /// <summary>Interface text size factor chosen in Settings (1 = 100 %); code-built controls multiply by it.</summary>
    public static double TextScale { get; set; } = 1;

    /// <summary>A font for previews; the "Varsayılan" choice maps to the Windows UI font.</summary>
    public static FontFamily Font(string? choice) =>
        new(string.IsNullOrWhiteSpace(choice) || choice == ViewModels.SettingsViewModel.DefaultFontLabel ? "Segoe UI Variable Text, Segoe UI" : choice);

    public static double UiPreviewSize(int scaleIndex) =>
        14 * ViewModels.SettingsViewModel.UiTextScales[Math.Clamp(scaleIndex, 0, ViewModels.SettingsViewModel.UiTextScales.Length - 1)] / 100.0;

    public static double ReadingPreviewSize(int zoomIndex) =>
        10.5 * 96 / 72 * ViewModels.SettingsViewModel.ReadingZooms[Math.Clamp(zoomIndex, 0, ViewModels.SettingsViewModel.ReadingZooms.Length - 1)] / 100.0;

    public static double ComposePreviewSize(int sizeIndex) =>
        ViewModels.SettingsViewModel.ComposeFontSizes[Math.Clamp(sizeIndex, 0, ViewModels.SettingsViewModel.ComposeFontSizes.Length - 1)] * 96 / 72.0;

    /// <summary>"#RRGGBB" to a brush.</summary>
    public static SolidColorBrush Brush(string hex)
    {
        var rgb = uint.Parse(hex.TrimStart('#'), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return new SolidColorBrush(ColorHelper.FromArgb(0xFF, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
    }

    public static ElementTheme ToElementTheme(ViewModels.AppTheme theme) => theme switch
    {
        ViewModels.AppTheme.Light => ElementTheme.Light,
        ViewModels.AppTheme.Dark => ElementTheme.Dark,
        _ => ElementTheme.Default,
    };
}

public static class Dialogs
{
    public static async Task<bool> ConfirmAsync(XamlRoot root, string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = Strings.Common_Yes,
            CloseButtonText = Strings.Common_No,
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = root,
            RequestedTheme = (root.Content as FrameworkElement)?.ActualTheme ?? ElementTheme.Default,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}

public static class WindowHelper
{
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    /// <summary>
    /// Colors of the minimize/maximize/close buttons for <paramref name="theme"/>. With the content extended into the
    /// title bar they otherwise follow the Windows theme, so they turn invisible when Postacı uses the other theme.
    /// </summary>
    public static void ApplyCaptionTheme(Window window, ElementTheme theme)
    {
        try
        {
            var bar = window.AppWindow.TitleBar;
            var dark = theme == ElementTheme.Dark;
            byte fg = dark ? (byte)0xFF : (byte)0x00;
            bar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
            bar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
            bar.ButtonForegroundColor = Windows.UI.Color.FromArgb(0xFF, fg, fg, fg);
            bar.ButtonInactiveForegroundColor = Windows.UI.Color.FromArgb(0x80, fg, fg, fg);
            bar.ButtonHoverForegroundColor = Windows.UI.Color.FromArgb(0xFF, fg, fg, fg);
            bar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(dark ? (byte)0x18 : (byte)0x12, fg, fg, fg);
            bar.ButtonPressedForegroundColor = Windows.UI.Color.FromArgb(0xC0, fg, fg, fg);
            bar.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(dark ? (byte)0x10 : (byte)0x0C, fg, fg, fg);
        }
        catch (Exception ex)
        {
            ViewModels.Diagnostics.Log("Caption button colors", ex);
        }
    }

    /// <summary>Resizes a window using effective (DPI-independent) pixels.</summary>
    public static void Resize(Window window, int width, int height)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        var scale = GetDpiForWindow(hwnd) / 96.0;
        if (scale <= 0) scale = 1;
        window.AppWindow.Resize(new SizeInt32((int)(width * scale), (int)(height * scale)));
    }
}
