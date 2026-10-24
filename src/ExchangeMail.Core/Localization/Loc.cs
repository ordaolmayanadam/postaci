using System.Globalization;

namespace ExchangeMail.Core.Localization;

/// <summary>
/// Interface language: which <see cref="Strings"/> are used and which culture formats dates and numbers.
/// Turkish is the neutral language (Strings.resx); English lives in Strings.en.resx.
/// </summary>
public static class Loc
{
    public const string Turkish = "tr";
    public const string English = "en";

    /// <summary>Language actually in use ("tr" or "en").</summary>
    public static string Current { get; private set; } = Turkish;

    /// <summary>Maps a stored preference ("", "tr", "en") to a language; empty follows the Windows display language.</summary>
    public static string Resolve(string? preference, CultureInfo systemUiCulture) => preference switch
    {
        Turkish or English => preference,
        _ => systemUiCulture.TwoLetterISOLanguageName == Turkish ? Turkish : English,
    };

    /// <summary>
    /// Switches strings and formatting to <paramref name="language"/>. The Windows regional format is kept when it
    /// belongs to the same language; otherwise a representative culture is used so dates don't mix languages.
    /// </summary>
    public static void Apply(string language)
    {
        Current = language == English ? English : Turkish;
        var ui = CultureInfo.GetCultureInfo(Current);
        Strings.Culture = ui;

        var format = CultureInfo.CurrentCulture.TwoLetterISOLanguageName == Current
            ? CultureInfo.CurrentCulture
            : CultureInfo.GetCultureInfo(Current == English ? "en-GB" : "tr-TR");
        CultureInfo.DefaultThreadCurrentUICulture = ui;
        CultureInfo.DefaultThreadCurrentCulture = format;
        CultureInfo.CurrentUICulture = ui;
        CultureInfo.CurrentCulture = format;
    }

    /// <summary>Fills a format string from <see cref="Strings"/> using the current culture.</summary>
    public static string Format(string format, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, format, args);

    /// <summary>Picks the singular or plural form (Turkish uses the same text for both).</summary>
    public static string Count(int count, string one, string other) =>
        Format(count == 1 ? one : other, count);
}
