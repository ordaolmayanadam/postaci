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

    /// <summary>Resizes a window using effective (DPI-independent) pixels.</summary>
    public static void Resize(Window window, int width, int height)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        var scale = GetDpiForWindow(hwnd) / 96.0;
        if (scale <= 0) scale = 1;
        window.AppWindow.Resize(new SizeInt32((int)(width * scale), (int)(height * scale)));
    }
}
