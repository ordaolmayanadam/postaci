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

    /// <summary>
    /// A body the way Outlook writes it: Word styles, black text and a colored signature, so the dark reading pane has
    /// real colors to adapt.
    /// </summary>
    public static string OutlookHtml(string text, string signer, string title, string note)
    {
        var paragraphs = text.Split("\n\n").Select(p =>
            $"<p class=MsoNormal><span style='color:black'>{WebUtility.HtmlEncode(p).Replace("\n", "<br>")}</span></p><p class=MsoNormal>&nbsp;</p>");
        return "<html><head><style><!-- p.MsoNormal{margin:0cm;font-size:11.0pt;font-family:\"Calibri\",sans-serif} " +
            "a:link{color:#0563C1;text-decoration:underline} span.EmailStyle17{mso-style-type:personal-compose;color:windowtext} --></style></head>" +
            "<body lang=TR link=\"#0563C1\" vlink=\"#954F72\" style='word-wrap:break-word'><div class=WordSection1>" +
            string.Concat(paragraphs) +
            "<table style='background:#FFF2CC;border-collapse:collapse'><tr><td style='border-left:solid #C00000 3.0pt;padding:6.0pt 9.0pt'>" +
            $"<p class=MsoNormal><b><span style='color:#C00000'>{WebUtility.HtmlEncode(note)}</span></b></p></td></tr></table><p class=MsoNormal>&nbsp;</p>" +
            $"<p class=MsoNormal><b><span style='color:#1F497D'>{WebUtility.HtmlEncode(signer)}</span></b><br>" +
            $"<span style='font-size:9.0pt;color:#7F7F7F'>{WebUtility.HtmlEncode(title)}</span></p></div></body></html>";
    }

    /// <summary>A newsletter laid out in tables with white backgrounds of its own and a dark banner.</summary>
    public static string NewsletterHtml(string heading, string text, string link, string footer)
    {
        var paragraphs = text.Split("\n\n").Select(p => $"<p style=\"margin:0 0 12px\">{WebUtility.HtmlEncode(p)}</p>");
        return "<html><body style=\"margin:0;background-color:#EEF1F5\"><table width=\"100%\" bgcolor=\"#EEF1F5\" cellpadding=\"0\" cellspacing=\"0\"><tr><td align=\"center\" style=\"padding:16px\">" +
            "<table width=\"560\" bgcolor=\"#FFFFFF\" cellpadding=\"0\" cellspacing=\"0\" style=\"border:1px solid #DDE3EA;font-family:'Segoe UI',Arial,sans-serif\">" +
            $"<tr><td bgcolor=\"#1F3864\" style=\"padding:20px 24px;color:#FFFFFF;font-size:22px;font-weight:600\">{WebUtility.HtmlEncode(heading)}</td></tr>" +
            $"<tr><td style=\"padding:20px 24px;color:#333333;font-size:14px;line-height:1.5\">{string.Concat(paragraphs)}" +
            $"<a href=\"https://example.com/\" style=\"color:#2E75B6;font-weight:600\">{WebUtility.HtmlEncode(link)}</a></td></tr>" +
            $"<tr><td bgcolor=\"#F7F9FB\" style=\"padding:12px 24px;color:#8A94A6;font-size:12px\">{WebUtility.HtmlEncode(footer)}</td></tr>" +
            "</table></td></tr></table></body></html>";
    }

    public static string PlainText(string html)
    {
        var text = Tags().Replace(Styles().Replace(html, "").Replace("</p>", " ").Replace("</td>", " "), "");
        return Spaces().Replace(WebUtility.HtmlDecode(text), " ").Trim();
    }

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"<style\b.*?</style>", RegexOptions.Singleline)]
    private static partial Regex Styles();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
