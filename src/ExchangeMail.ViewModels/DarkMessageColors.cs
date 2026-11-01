using System.Drawing;
using System.Globalization;
using System.Text.RegularExpressions;

namespace ExchangeMail.ViewModels;

/// <summary>
/// Adapts the colors a message sets itself to a dark page, the way Outlook does: light backgrounds and borders turn dark,
/// dark text and links turn light, keeping their hue. Colors that already suit a dark page (a dark banner, white
/// text on it) stay as they are, so contrast within the message is kept. Lightness is changed in OKLab, where equal
/// steps look equal, so a dark blue and a black end up equally readable.
/// </summary>
/// <remarks>
/// Works on the HTML text: colors in style attributes, &lt;style&gt; blocks and the legacy color attributes (bgcolor,
/// font color, body text/link). Messages with a dark variant of their own (prefers-color-scheme) are left alone, and
/// images are never changed.
/// </remarks>
public static partial class DarkMessageColors
{
    // Backgrounds lighter than this are darkened; white becomes the page itself.
    private const double LightBackground = 0.6;
    // Text darker than this is lightened, up to TextLightest for black; this keeps text readable on the page.
    private const double DarkText = 0.72;
    private const double TextLightest = 0.93;
    // Grays (backgrounds this close to neutral) take the page's own slight tint, so a white table matches the page.
    private const double Neutral = 0.02;

    private static readonly Lab Page = ToLab(ParseHex(MailFormat.DarkPageBackground[1..])!.Value);

    private static readonly Dictionary<string, Color> Named = Enum.GetValues<KnownColor>()
        .Select(Color.FromKnownColor)
        .Where(c => !c.IsSystemColor && c.A == 255)
        .ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

    // Borders follow the background when light (a light gray rule stays a faint line) and the text when dark (a black
    // rule stays visible).
    private enum Role { Background, Foreground, Border }

    public static string Apply(string html)
    {
        if (html.Contains("prefers-color-scheme", StringComparison.OrdinalIgnoreCase)) return html;
        html = StyleBlock().Replace(html, m => m.Groups["open"].Value + Declarations(m.Groups["css"].Value) + m.Groups["close"].Value);
        return Tag().Replace(html, m => m.Groups["name"].Value + Attribute().Replace(m.Groups["attrs"].Value, a => Attr(a, m.Groups["name"].Value)));
    }

    private static string Attr(Match a, string tag)
    {
        var name = a.Groups["n"].Value.ToLowerInvariant();
        var value = a.Groups["v"];
        if (!value.Success) return a.Value;
        string? changed = name switch
        {
            "style" => Declarations(value.Value),
            "bgcolor" => Convert(value.Value, Role.Background, legacy: true),
            "color" => Convert(value.Value, Role.Foreground, legacy: true),
            "text" or "link" or "vlink" or "alink" when tag.Equals("<body", StringComparison.OrdinalIgnoreCase)
                => Convert(value.Value, Role.Foreground, legacy: true),
            _ => null,
        };
        if (changed is null || changed == value.Value) return a.Value;
        return a.Value[..(value.Index - a.Index)] + changed + a.Value[(value.Index - a.Index + value.Length)..];
    }

    /// <summary>Changes the colors of the declarations in CSS text (a style attribute or a style block).</summary>
    private static string Declarations(string css) => Declaration().Replace(css, m =>
    {
        var property = m.Groups["p"].Value;
        var role = property.StartsWith("background", StringComparison.OrdinalIgnoreCase) ? Role.Background
            : property.Equals("color", StringComparison.OrdinalIgnoreCase) || property.StartsWith("text-", StringComparison.OrdinalIgnoreCase) ? Role.Foreground
            : Role.Border;
        var value = ValueToken().Replace(m.Groups["v"].Value, t => t.Groups["c"].Success ? Convert(t.Value, role, legacy: false) ?? t.Value : t.Value);
        return m.Value[..(m.Groups["v"].Index - m.Index)] + value;
    });

    /// <summary>The color for a dark page, or null when <paramref name="text"/> is not a color this class understands.</summary>
    private static string? Convert(string text, Role role, bool legacy)
    {
        var color = Parse(text.Trim(), legacy);
        if (color is null) return null;
        var (rgb, alpha) = color.Value;
        var lab = ToLab(rgb);
        var chroma = Math.Sqrt(lab.A * lab.A + lab.B * lab.B);
        Lab result;
        if (role == Role.Background || role == Role.Border && lab.L > LightBackground)
        {
            if (lab.L <= LightBackground) return text;
            var l = Page.L + (1 - lab.L) * (LightBackground - Page.L) / (1 - LightBackground);
            result = chroma < Neutral ? Page with { L = l } : lab with { L = l };
        }
        else
        {
            if (lab.L >= DarkText) return text;
            result = lab with { L = TextLightest - lab.L * (TextLightest - DarkText) / DarkText };
        }
        return Format(ToRgb(result), alpha);
    }

    private static ((double R, double G, double B) Rgb, double Alpha)? Parse(string text, bool legacy)
    {
        if (text.StartsWith('#')) return ParseHex(text[1..]) is { } hex ? (hex, HexAlpha(text[1..])) : null;
        // HTML attributes also take hex without the #, as browsers do (bgcolor="FFFFFF").
        if (legacy && text.Length == 6 && ParseHex(text) is { } bare) return (bare, 1);
        // CSS spells every gray also as grey; .NET knows only the first.
        if (Named.TryGetValue(text, out var named) || Named.TryGetValue(text.Replace("grey", "gray", StringComparison.OrdinalIgnoreCase), out named))
            return ((named.R / 255.0, named.G / 255.0, named.B / 255.0), 1);
        var fn = RgbFunction().Match(text);
        if (!fn.Success) return null;
        var parts = fn.Groups["args"].Value.Split([',', ' ', '/'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is not (3 or 4)) return null;
        var channels = new double[4];
        channels[3] = 1;
        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            var percent = part.EndsWith('%');
            if (!double.TryParse(percent ? part[..^1] : part, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return null;
            channels[i] = Math.Clamp(percent ? v / 100 : i < 3 ? v / 255 : v, 0, 1);
        }
        return ((channels[0], channels[1], channels[2]), channels[3]);
    }

    private static (double R, double G, double B)? ParseHex(string hex)
    {
        if (hex.Length is not (3 or 4 or 6 or 8) || !hex.All(char.IsAsciiHexDigit)) return null;
        int Channel(int i) => hex.Length <= 4
            ? System.Convert.ToInt32(new string(hex[i], 2), 16)
            : System.Convert.ToInt32(hex.Substring(i * 2, 2), 16);
        return (Channel(0) / 255.0, Channel(1) / 255.0, Channel(2) / 255.0);
    }

    private static double HexAlpha(string hex) => hex.Length switch
    {
        4 => System.Convert.ToInt32(new string(hex[3], 2), 16) / 255.0,
        8 => System.Convert.ToInt32(hex[6..8], 16) / 255.0,
        _ => 1,
    };

    private static string Format((double R, double G, double B) rgb, double alpha)
    {
        int Byte(double v) => (int)Math.Round(Math.Clamp(v, 0, 1) * 255);
        return alpha >= 1
            ? $"#{Byte(rgb.R):x2}{Byte(rgb.G):x2}{Byte(rgb.B):x2}"
            : FormattableString.Invariant($"rgba({Byte(rgb.R)},{Byte(rgb.G)},{Byte(rgb.B)},{alpha:0.###})");
    }

    private readonly record struct Lab(double L, double A, double B);

    // OKLab (Björn Ottosson): sRGB → linear → LMS → Lab, and back.
    private static Lab ToLab((double R, double G, double B) c)
    {
        double r = Linear(c.R), g = Linear(c.G), b = Linear(c.B);
        var l = Math.Cbrt(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b);
        var m = Math.Cbrt(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b);
        var s = Math.Cbrt(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b);
        return new Lab(
            0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s,
            1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s,
            0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s);
    }

    /// <summary>Back to sRGB; a color outside sRGB loses chroma (keeping lightness and hue) until it fits.</summary>
    private static (double R, double G, double B) ToRgb(Lab lab)
    {
        var rgb = LinearRgb(lab);
        if (InGamut(rgb)) return Encode(rgb);
        double low = 0, high = 1;
        for (var i = 0; i < 20; i++)
        {
            var mid = (low + high) / 2;
            if (InGamut(LinearRgb(lab with { A = lab.A * mid, B = lab.B * mid }))) low = mid; else high = mid;
        }
        return Encode(LinearRgb(lab with { A = lab.A * low, B = lab.B * low }));
    }

    private static (double R, double G, double B) LinearRgb(Lab lab)
    {
        var l = Math.Pow(lab.L + 0.3963377774 * lab.A + 0.2158037573 * lab.B, 3);
        var m = Math.Pow(lab.L - 0.1055613458 * lab.A - 0.0638541728 * lab.B, 3);
        var s = Math.Pow(lab.L - 0.0894841775 * lab.A - 1.2914855480 * lab.B, 3);
        return (
            4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s,
            -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s,
            -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s);
    }

    private static bool InGamut((double R, double G, double B) c) =>
        c.R is >= -1e-4 and <= 1 + 1e-4 && c.G is >= -1e-4 and <= 1 + 1e-4 && c.B is >= -1e-4 and <= 1 + 1e-4;

    private static double Linear(double c) => c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);

    private static double Gamma(double c) => c <= 0.0031308 ? 12.92 * c : 1.055 * Math.Pow(Math.Max(c, 0), 1 / 2.4) - 0.055;

    private static (double R, double G, double B) Encode((double R, double G, double B) c) => (Gamma(c.R), Gamma(c.G), Gamma(c.B));

    [GeneratedRegex(@"(?<open><style\b[^>]*>)(?<css>.*?)(?<close></style\s*>)", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex StyleBlock();

    [GeneratedRegex(@"(?<name><[a-z][a-z0-9]*)(?<attrs>[^>]*)", RegexOptions.IgnoreCase)]
    private static partial Regex Tag();

    // Every attribute in order, so a value that merely contains "color=" is never mistaken for an attribute.
    [GeneratedRegex(@"\s*(?<n>[^\s=/>""']+)(?:\s*=\s*(?:""(?<v>[^""]*)""|'(?<v>[^']*)'|(?<v>[^\s>]+)))?")]
    private static partial Regex Attribute();

    // A ":" right after "a" or "td.x" in a selector never names one of these properties, and "-color" in mso-color-alt
    // or background-color is excluded by the look-behind.
    [GeneratedRegex(@"(?<![\w-])(?<p>background-color|background|color|border(?:-(?:top|right|bottom|left|block|inline)(?:-(?:start|end))?)?(?:-color)?|outline(?:-color)?|text-decoration(?:-color)?|column-rule(?:-color)?)\s*:\s*(?<v>[^;{}]*)", RegexOptions.IgnoreCase)]
    private static partial Regex Declaration();

    // url(...) and quoted strings are kept whole, so a file named white.png stays as it is.
    [GeneratedRegex(@"url\([^)]*\)|""[^""]*""|'[^']*'|(?<c>#[0-9a-f]{3,8}\b|rgba?\([^)]*\)|\b[a-z]+\b)", RegexOptions.IgnoreCase)]
    private static partial Regex ValueToken();

    [GeneratedRegex(@"^rgba?\((?<args>[^)]*)\)$", RegexOptions.IgnoreCase)]
    private static partial Regex RgbFunction();
}
