using System.Drawing.Text;

namespace ExchangeMail.App.Services;

/// <summary>Font families installed on this PC, for the font pickers in Settings.</summary>
public static class FontCatalog
{
    private static readonly string[] Common =
        ["Segoe UI", "Segoe UI Variable Text", "Calibri", "Aptos", "Arial", "Verdana", "Tahoma", "Georgia", "Times New Roman", "Cambria", "Consolas"];

    private static readonly Lazy<IReadOnlyList<string>> Families = new(Load);

    public static IReadOnlyList<string> InstalledFamilies => Families.Value;

    private static IReadOnlyList<string> Load()
    {
        try
        {
            using var fonts = new InstalledFontCollection();
            // '@' families are the vertical (East Asian) variants.
            var names = fonts.Families.Select(f => f.Name).Where(n => n.Length > 0 && !n.StartsWith('@')).ToList();
            return names.Count > 0 ? names : Common;
        }
        catch (Exception)
        {
            return Common;
        }
    }
}
