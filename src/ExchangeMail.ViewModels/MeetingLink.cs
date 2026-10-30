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

    public static string? Find(string? joinUrl, string? location, string? body, IEnumerable<string>? extraHosts = null)
    {
        var hosts = KnownHosts.Concat(extraHosts ?? []).ToList();
        foreach (var text in new[] { joinUrl, location, body })
        {
            if (string.IsNullOrEmpty(text)) continue;
            foreach (Match m in Address().Matches(WebUtility.HtmlDecode(text)))
            {
                var candidate = m.Value.TrimEnd('.', ',', ';', ':', ')', '!', '?');
                if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && IsMeeting(uri, hosts))
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

    [GeneratedRegex(@"https://[^\s""'<>]+", RegexOptions.IgnoreCase)]
    private static partial Regex Address();
}
