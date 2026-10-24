using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace ExchangeMail.ViewModels;

public static partial class MailFormat
{
    public static string ListDate(DateTimeOffset value, DateTimeOffset? now = null)
    {
        var local = value.ToLocalTime();
        var today = (now ?? DateTimeOffset.Now).ToLocalTime().Date;
        if (local.Date == today) return local.ToString("HH:mm", CultureInfo.CurrentCulture);
        if (local.Date > today.AddDays(-7)) return local.ToString("ddd HH:mm", CultureInfo.CurrentCulture);
        return local.ToString("d", CultureInfo.CurrentCulture);
    }

    public static string LongDate(DateTimeOffset? value) =>
        value?.ToLocalTime().ToString("f", CultureInfo.CurrentCulture) ?? "";

    /// <summary>Day and month in the culture's order: "3 Mart", "3 March" or "March 3".</summary>
    public static string DayMonth(DateTime value) => value.ToString("M", CultureInfo.CurrentCulture);

    /// <summary><see cref="DayMonth"/> with the year: "3 Mart 2026" or "March 3, 2026".</summary>
    public static string DayMonthYear(DateTime value) =>
        value.ToString(CultureInfo.CurrentCulture.DateTimeFormat.MonthDayPattern.StartsWith('d') ? "d MMMM yyyy" : "MMMM d, yyyy",
            CultureInfo.CurrentCulture);

    public static string FileSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes / (1024.0 * 1024):0.#} MB",
    };

    /// <summary>Turns what the user typed in the compose body into HTML.</summary>
    public static string PlainTextToHtml(string text)
    {
        var sb = new StringBuilder("<div style=\"font-family:'Segoe UI',Calibri,Arial,sans-serif;font-size:11pt\">");
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0) sb.Append("<br>");
            sb.Append(WebUtility.HtmlEncode(lines[i]).Replace("  ", " &nbsp;"));
        }
        return sb.Append("</div>").ToString();
    }

    /// <summary>
    /// Prepares a message body for the reading pane: adds a Content-Security-Policy that blocks scripts
    /// and, unless <paramref name="allowRemote"/> is set, remote images/styles (tracking pixels).
    /// </summary>
    public static string PrepareForDisplay(string body, bool isHtml, bool allowRemote)
    {
        var html = isHtml
            ? body
            : $"<pre style=\"white-space:pre-wrap;font-family:inherit;font-size:inherit\">{WebUtility.HtmlEncode(body)}</pre>";

        var remote = allowRemote ? " https: http:" : "";
        var head =
            $"<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; img-src data:{remote}; style-src 'unsafe-inline'{remote}; font-src data:{remote}\">" +
            "<meta charset=\"utf-8\"><style>body{font-family:'Segoe UI',Calibri,sans-serif;font-size:10.5pt;margin:12px;word-wrap:break-word}img{max-width:100%;height:auto}</style>";

        var match = HeadTag().Match(html);
        if (match.Success) return html.Insert(match.Index + match.Length, head);
        match = HtmlTag().Match(html);
        if (match.Success) return html.Insert(match.Index + match.Length, "<head>" + head + "</head>");
        return "<!DOCTYPE html><html><head>" + head + "</head><body>" + html + "</body></html>";
    }

    public static bool HasRemoteContent(string html) => RemoteReference().IsMatch(html);

    /// <summary>
    /// One HTML document for a conversation: each message's own text under a header with sender, date and
    /// recipients, newest first. Header values are encoded; the bodies go through the same display policy (CSP,
    /// no scripts) as a single message.
    /// </summary>
    public static string BuildThreadHtml(IEnumerable<Core.Models.ThreadMessage> messages)
    {
        var sb = new StringBuilder("<div>");
        foreach (var m in messages)
        {
            var to = string.Join("; ", m.To.Select(a => a.Display));
            sb.Append("<div style=\"padding:12px 0 16px;border-bottom:1px solid rgba(128,128,128,.35)\">")
              .Append("<div style=\"font-family:'Segoe UI',sans-serif;font-size:12px;color:#6b6b6b;margin-bottom:10px\">")
              .Append("<b style=\"color:#1a1a1a;font-size:13px\">").Append(WebUtility.HtmlEncode(m.From?.Display ?? "")).Append("</b>")
              .Append(" · ").Append(WebUtility.HtmlEncode(LongDate(m.Received)));
            if (to.Length > 0)
                sb.Append("<br>").Append(WebUtility.HtmlEncode(Strings.Mail_ToLabel)).Append(' ').Append(WebUtility.HtmlEncode(to));
            sb.Append("</div><div>");
            if (m.Body.Length > 0)
                sb.Append(m.IsHtml ? ExtractBodyHtml(m.Body) : WebUtility.HtmlEncode(m.Body).ReplaceLineEndings("<br>"));
            else
                sb.Append("<p>").Append(WebUtility.HtmlEncode(m.Preview)).Append("</p>");
            sb.Append("</div></div>");
        }
        return sb.Append("</div>").ToString();
    }

    public const string DefaultComposeFont = "Segoe UI";
    public const int DefaultComposeFontSize = 11;

    /// <summary>A CSS font-family list: the chosen font first, then common fallbacks.</summary>
    public static string FontStack(string? family, string fallback = "Segoe UI")
    {
        var first = string.IsNullOrWhiteSpace(family) ? fallback : family.Trim();
        // Font names are user/system supplied; keep them from breaking out of the CSS string.
        first = new string(first.Where(c => c is not ('\'' or '"' or '\\' or ';' or '{' or '}' or '<' or '>')).ToArray());
        return $"'{first}','Segoe UI',Calibri,Arial,sans-serif";
    }

    /// <summary>Wraps HTML written in the app's editor so recipients see the sender's font and size.</summary>
    public static string WrapComposedHtml(string innerHtml, string? fontFamily = null, int fontSizePt = DefaultComposeFontSize) =>
        $"<div style=\"font-family:{FontStack(fontFamily, DefaultComposeFont)};font-size:{fontSizePt}pt\">" + innerHtml + "</div>";

    /// <summary>
    /// Applies the reader's preferences to HTML prepared by <see cref="PrepareForDisplay"/>: a default font (messages
    /// that set their own fonts keep them unless <paramref name="overrideFonts"/>) and a zoom level in percent.
    /// </summary>
    public static string ApplyReadingPreferences(string preparedHtml, string? fontFamily, bool overrideFonts, int zoomPercent)
    {
        var css = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(fontFamily))
        {
            var stack = FontStack(fontFamily);
            css.Append(overrideFonts
                ? $"body,body *{{font-family:{stack} !important}}"
                : $"body{{font-family:{stack}}}");
        }
        if (zoomPercent is > 0 and not 100) css.Append(FormattableString.Invariant($"html{{zoom:{zoomPercent / 100.0:0.##}}}"));
        if (css.Length == 0) return preparedHtml;

        var style = "<style>" + css + "</style>";
        var index = preparedHtml.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
        return index >= 0 ? preparedHtml.Insert(index, style) : style + preparedHtml;
    }

    /// <summary>Returns what is inside &lt;body&gt; (or the whole fragment), without scripts, for loading into the editor.</summary>
    public static string ExtractBodyHtml(string html)
    {
        var match = BodyContent().Match(html);
        var inner = match.Success ? match.Groups[1].Value : html;
        return ScriptBlock().Replace(inner, "");
    }

    /// <summary>Rough HTML → text conversion for editing short HTML snippets (automatic replies, event descriptions).</summary>
    public static string HtmlToPlainText(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";
        var text = ExtractBodyHtml(html);
        text = StyleBlock().Replace(text, "");
        text = LineBreakTag().Replace(text, "\n");
        text = BlockEndTag().Replace(text, "\n");
        text = AnyTag().Replace(text, "");
        text = WebUtility.HtmlDecode(text).Replace('\u00A0', ' ');
        text = ExtraBlankLines().Replace(text.Replace("\r\n", "\n"), "\n\n");
        return text.Trim();
    }

    public static string ReplaceContentId(string html, string contentId, string replacement)
    {
        var id = contentId.Trim('<', '>');
        return Regex.Replace(html, "cid:" + Regex.Escape(id), replacement.Replace("$", "$$"), RegexOptions.IgnoreCase);
    }

    public static bool ReferencesContentId(string html, string? contentId) =>
        !string.IsNullOrEmpty(contentId) && html.Contains("cid:" + contentId.Trim('<', '>'), StringComparison.OrdinalIgnoreCase);

    public static string GuessContentType(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".txt" or ".log" => "text/plain",
        ".csv" => "text/csv",
        ".htm" or ".html" => "text/html",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".bmp" => "image/bmp",
        ".zip" => "application/zip",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        ".doc" => "application/msword",
        ".xls" => "application/vnd.ms-excel",
        ".eml" => "message/rfc822",
        _ => "application/octet-stream",
    };

    [GeneratedRegex(@"<body[^>]*>(.*)</body>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex BodyContent();

    [GeneratedRegex(@"<script\b.*?</script\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ScriptBlock();

    [GeneratedRegex(@"<style\b.*?</style\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex StyleBlock();

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex LineBreakTag();

    [GeneratedRegex(@"</(p|div|li|tr|h[1-6])\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockEndTag();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex AnyTag();

    [GeneratedRegex(@"\n[ \t]*\n(\s*\n)+")]
    private static partial Regex ExtraBlankLines();

    [GeneratedRegex(@"<head[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex HeadTag();

    [GeneratedRegex(@"<html[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlTag();

    [GeneratedRegex(@"(src|background)\s*=\s*[""']?\s*https?://|url\(\s*[""']?\s*https?://", RegexOptions.IgnoreCase)]
    private static partial Regex RemoteReference();
}

public static partial class RecipientParser
{
    /// <summary>
    /// Parses "Ali Veli &lt;ali@sirket.com&gt;; ayse@sirket.com" into addresses.
    /// Tokens that are not addresses are returned in <paramref name="invalid"/>.
    /// </summary>
    public static List<string> Parse(string? text, out List<string> invalid)
    {
        var result = new List<string>();
        invalid = [];
        if (string.IsNullOrWhiteSpace(text)) return result;

        foreach (var raw in SplitTokens(text))
        {
            var token = raw.Trim();
            if (token.Length == 0) continue;
            var angle = AngleAddress().Match(token);
            var address = angle.Success ? angle.Groups[1].Value.Trim() : token;
            if (SimpleAddress().IsMatch(address))
            {
                if (!result.Contains(address, StringComparer.OrdinalIgnoreCase)) result.Add(address);
            }
            else invalid.Add(token);
        }
        return result;
    }

    /// <summary>Returns the text after the last separator: the part the user is currently typing.</summary>
    public static string LastToken(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var index = text.LastIndexOfAny([';', ',']);
        return text[(index + 1)..].Trim();
    }

    public static string ReplaceLastToken(string? text, string replacement)
    {
        text ??= "";
        var index = text.LastIndexOfAny([';', ',']);
        var prefix = index < 0 ? "" : text[..(index + 1)].TrimEnd() + " ";
        return prefix + replacement + "; ";
    }

    // Splits on ; and , but not inside quotes ("Veli, Ali" <ali@x>).
    private static IEnumerable<string> SplitTokens(string text)
    {
        var start = 0;
        var inQuotes = false;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '"') inQuotes = !inQuotes;
            else if (!inQuotes && text[i] is ';' or ',')
            {
                yield return text[start..i];
                start = i + 1;
            }
        }
        yield return text[start..];
    }

    [GeneratedRegex(@"<([^<>]+)>")]
    private static partial Regex AngleAddress();

    [GeneratedRegex(@"^[^\s@<>""]+@[^\s@<>""]+\.[^\s@<>""]+$")]
    private static partial Regex SimpleAddress();
}
