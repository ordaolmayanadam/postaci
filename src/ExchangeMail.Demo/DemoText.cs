using System.Net;
using System.Text.RegularExpressions;

namespace ExchangeMail.Demo;

internal static partial class DemoText
{
    /// <summary>Paragraphs separated by blank lines become an HTML body, with the sender's first name as signature.</summary>
    public static string Html(string text, string? signer)
    {
        var paragraphs = text.Split("\n\n").Select(p => $"<p>{WebUtility.HtmlEncode(p).Replace("\n", "<br>")}</p>");
        var signature = signer is null ? "" : $"<p>{WebUtility.HtmlEncode(signer.Split(' ')[0])}</p>";
        return $"<html><body style=\"font-family:'Segoe UI',sans-serif;font-size:14px\">{string.Concat(paragraphs)}{signature}</body></html>";
    }

    public static string PlainText(string html)
    {
        var text = Tags().Replace(html.Replace("</p>", " "), "");
        return Spaces().Replace(WebUtility.HtmlDecode(text), " ").Trim();
    }

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
