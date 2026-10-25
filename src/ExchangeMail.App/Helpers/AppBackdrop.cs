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
    private static readonly Color DarkTint = Color.FromArgb(0xFF, 0x22, 0x22, 0x34);
    private static readonly Color DarkFallback = Color.FromArgb(0xFF, 0x24, 0x24, 0x36);

    private MicaController? _mica;
    private DesktopAcrylicController? _acrylic;

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);
        var configuration = GetDefaultSystemBackdropConfiguration(connectedTarget, xamlRoot);
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
    }

    protected override void OnDefaultSystemBackdropConfigurationChanged(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
        base.OnDefaultSystemBackdropConfigurationChanged(target, xamlRoot);
        ApplyTheme(GetDefaultSystemBackdropConfiguration(target, xamlRoot).Theme);
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
