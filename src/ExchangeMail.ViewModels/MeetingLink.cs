using System.Net;
using System.Text.RegularExpressions;

namespace ExchangeMail.ViewModels;

/// <summary>
/// Finds the online meeting an event links to: the join address Exchange keeps for Teams/Skype meetings, otherwise
/// the first address of a known meeting service in the location or the description. Only those services count, so
/// another link in an invitation is never offered as "Join meeting".
/// </summary>
public static partial class MeetingLink
{
    /// <summary>Hosts of meeting services, with their subdomains (company.zoom.us, company.webex.com, ...).</summary>
    private static readonly string[] KnownHosts =
        ["teams.microsoft.com", "teams.live.com", "zoom.us", "meet.google.com", "webex.com", "meet.jit.si", "whereby.com", "gotomeeting.com"];

    /// <summary>
    /// The first meeting address in the join address, the location or the body (HTML or text). Addresses may come
    /// with http(s):// or without a scheme ("meet.company.example/room", opened over https); a server without a
    /// meeting path (a bare host in a signature) does not count.
    /// </summary>
    public static string? Find(string? joinUrl, string? location, string? body, IEnumerable<string>? extraHosts = null)
    {
        var hosts = KnownHosts.Concat(extraHosts ?? []).ToList();
        foreach (var text in new[] { joinUrl, location, body })
        {
            if (string.IsNullOrEmpty(text)) continue;
            foreach (Match m in Address().Matches(WebUtility.HtmlDecode(text)))
            {
                var path = m.Groups["path"].Value.TrimEnd('.', ',', ';', ':', ')', '!', '?');
                if (path.Length <= 1) continue;
                var scheme = m.Groups["scheme"].Success ? m.Groups["scheme"].Value.ToLowerInvariant() : "https://";
                if (Uri.TryCreate(scheme + m.Groups["host"].Value + path, UriKind.Absolute, out var uri) && IsMeeting(uri, hosts))
                    return uri.AbsoluteUri;
            }
        }
        return null;
    }

    /// <summary>The meeting addresses added in Settings ("jitsi.company.example, meet.company.example"), as hosts.</summary>
    public static IReadOnlyList<string> ParseHosts(string? setting) =>
        (setting ?? "").Split([',', ';', ' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .Select(h => Uri.TryCreate(h.Contains("://") ? h : "https://" + h, UriKind.Absolute, out var uri) ? uri.Host : "")
            .Where(h => h.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static bool IsMeeting(Uri uri, IEnumerable<string> hosts) =>
        // A self-hosted Jitsi is usually named after it (jitsi.company.example, meet-jitsi.company.example).
        uri.Host.Split('.').Any(label => label.Contains("jitsi", StringComparison.OrdinalIgnoreCase))
        || hosts.Any(h => uri.Host.Equals(h, StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith("." + h, StringComparison.OrdinalIgnoreCase));

    // A host with a path, after http(s):// or on its own; not inside an e-mail address or another word.
    [GeneratedRegex(@"(?<![\w@.\-/])(?<scheme>https?://)?(?<host>(?:[a-z0-9](?:[a-z0-9-]*[a-z0-9])?\.)+[a-z]{2,})(?<path>/[^\s""'<>]*)?",
        RegexOptions.IgnoreCase)]
    private static partial Regex Address();
}
