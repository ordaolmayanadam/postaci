using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace ExchangeMail.App.Helpers;

/// <summary>
/// Window background. Light theme: the standard Mica. Dark theme: Mica tinted towards a soft slate blue, because plain
/// dark Mica turns almost black on dark wallpapers. Windows 10 has no Mica, so acrylic with the same tint is used.
/// The theme follows the window content (Postacı's own theme setting), not only Windows.
/// </summary>
public sealed partial class AppBackdrop : SystemBackdrop
{
    /// <summary>The darkest tone of the dark theme; dark message bodies use it too (MailFormat.DarkPageBackground).</summary>
    internal static readonly Color DarkTint = Color.FromArgb(0xFF, 0x22, 0x22, 0x34);
    private static readonly Color DarkFallback = Color.FromArgb(0xFF, 0x24, 0x24, 0x36);

    private MicaController? _mica;
    private DesktopAcrylicController? _acrylic;

    /// <summary>
    /// The configuration WinUI keeps in sync with the window's theme. It is fetched once: asking for it again from
    /// the change callback can re-enter that callback.
    /// </summary>
    private SystemBackdropConfiguration? _configuration;

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);
        var configuration = _configuration = GetDefaultSystemBackdropConfiguration(connectedTarget, xamlRoot);
        if (MicaController.IsSupported())
        {
            _mica = new MicaController();
            ApplyTheme(configuration.Theme);
            _mica.SetSystemBackdropConfiguration(configuration);
            _mica.AddSystemBackdropTarget(connectedTarget);
        }
        else if (DesktopAcrylicController.IsSupported())
        {
            _acrylic = new DesktopAcrylicController();
            ApplyTheme(configuration.Theme);
            _acrylic.SetSystemBackdropConfiguration(configuration);
            _acrylic.AddSystemBackdropTarget(connectedTarget);
        }
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        base.OnTargetDisconnected(disconnectedTarget);
        _mica?.RemoveSystemBackdropTarget(disconnectedTarget);
        _mica?.Dispose();
        _mica = null;
        _acrylic?.RemoveSystemBackdropTarget(disconnectedTarget);
        _acrylic?.Dispose();
        _acrylic = null;
        _configuration = null;
    }

    /// <summary>
    /// WinUI has already updated <see cref="_configuration"/> (and through it the controller) to the new theme; only
    /// the dark tint is ours to switch. The base method is not called: this backdrop needs nothing from it, and calling
    /// it from a derived backdrop throws ArgumentException ("target") on theme changes.
    /// </summary>
    protected override void OnDefaultSystemBackdropConfigurationChanged(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
        try
        {
            if (_configuration is { } configuration) ApplyTheme(configuration.Theme);
        }
        catch (Exception ex)
        {
            // A wrong tint is better than a crash while the theme changes.
            ViewModels.Diagnostics.Log("Backdrop theme change", ex);
        }
    }

    private void ApplyTheme(SystemBackdropTheme theme)
    {
        var dark = theme == SystemBackdropTheme.Dark;
        if (_mica is { } mica)
        {
            if (!dark)
            {
                mica.ResetProperties();
                return;
            }
            mica.TintColor = DarkTint;
            mica.TintOpacity = 0.8f;
            mica.LuminosityOpacity = 0.92f;
            mica.FallbackColor = DarkFallback;
        }
        if (_acrylic is { } acrylic)
        {
            if (!dark)
            {
                acrylic.ResetProperties();
                return;
            }
            acrylic.TintColor = DarkTint;
            acrylic.TintOpacity = 0.85f;
            acrylic.LuminosityOpacity = 0.95f;
            acrylic.FallbackColor = DarkFallback;
        }
    }
}
