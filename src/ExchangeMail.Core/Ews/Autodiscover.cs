using System.Net;
using System.Text;
using System.Xml.Linq;

namespace ExchangeMail.Core.Ews;

/// <summary>POX Autodiscover for on-premises Exchange: finds the EWS URL from an e-mail address.</summary>
public static class Autodiscover
{
    private static readonly XNamespace RequestNs = "http://schemas.microsoft.com/exchange/autodiscover/outlook/requestschema/2006";
    private static readonly XNamespace ResponseNs = "http://schemas.microsoft.com/exchange/autodiscover/outlook/responseschema/2006a";
    private const int MaxRedirects = 5;

    public static async Task<Uri> DiscoverEwsUrlAsync(string email, ICredentials credentials, CancellationToken ct = default)
    {
        using var http = new HttpClient(EwsClient.CreateHandler(credentials)) { Timeout = TimeSpan.FromSeconds(20) };
        return await DiscoverEwsUrlAsync(email, http, ct);
    }

    internal static async Task<Uri> DiscoverEwsUrlAsync(string email, HttpClient http, CancellationToken ct)
    {
        var errors = new List<string>();
        var currentEmail = email.Trim();
        var queue = new Queue<Uri>(CandidateUrls(currentEmail));

        for (var redirects = 0; queue.Count > 0 && redirects <= MaxRedirects;)
        {
            var url = queue.Dequeue();
            AutodiscoverResult result;
            try
            {
                result = await QueryAsync(http, url, currentEmail, ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Xml.XmlException && !ct.IsCancellationRequested)
            {
                errors.Add($"{url.Host}: {ex.Message}");
                continue;
            }

            switch (result)
            {
                case { EwsUrl: { } ews }:
                    return ews;
                case { RedirectAddress: { } address }:
                    redirects++;
                    currentEmail = address;
                    queue = new Queue<Uri>(CandidateUrls(address));
                    break;
                case { RedirectUrl: { } redirectUrl }:
                    redirects++;
                    queue = new Queue<Uri>([redirectUrl]);
                    break;
                default:
                    errors.Add($"{url.Host}: {result.Error ?? "EWS adresi içermeyen yanıt"}");
                    break;
            }
        }

        throw new EwsException("Autodiscover EWS adresini bulamadı. EWS adresini elle girin (ör. https://mail.sirket.com/EWS/Exchange.asmx)."
                               + (errors.Count > 0 ? Environment.NewLine + string.Join(Environment.NewLine, errors) : ""));
    }

    private static IEnumerable<Uri> CandidateUrls(string email)
    {
        var at = email.LastIndexOf('@');
        if (at < 0 || at == email.Length - 1) throw new ArgumentException("Geçerli bir e-posta adresi girin.", nameof(email));
        var domain = email[(at + 1)..];
        yield return new Uri($"https://autodiscover.{domain}/autodiscover/autodiscover.xml");
        yield return new Uri($"https://{domain}/autodiscover/autodiscover.xml");
    }

    private static async Task<AutodiscoverResult> QueryAsync(HttpClient http, Uri url, string email, CancellationToken ct)
    {
        var request = new XDocument(new XElement(RequestNs + "Autodiscover",
            new XElement(RequestNs + "Request",
                new XElement(RequestNs + "EMailAddress", email),
                new XElement(RequestNs + "AcceptableResponseSchema", ResponseNs.NamespaceName))));

        using var content = new StringContent(request.ToString(SaveOptions.DisableFormatting), Encoding.UTF8, "text/xml");
        using var response = await http.PostAsync(url, content, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new EwsAuthenticationException("Autodiscover kimlik doğrulaması başarısız. Kullanıcı adı ve parolayı kontrol edin.");
        response.EnsureSuccessStatusCode();

        return Parse(await response.Content.ReadAsStringAsync(ct));
    }

    internal static AutodiscoverResult Parse(string xml)
    {
        var doc = XDocument.Parse(xml);
        var account = doc.Descendants(ResponseNs + "Account").FirstOrDefault();
        if (account is null)
        {
            var error = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Message")?.Value;
            return new AutodiscoverResult(null, null, null, error);
        }

        var action = (string?)account.Element(ResponseNs + "Action");
        if (action == "redirectAddr")
            return new AutodiscoverResult(null, (string?)account.Element(ResponseNs + "RedirectAddr"), null, null);
        if (action == "redirectUrl" && Uri.TryCreate((string?)account.Element(ResponseNs + "RedirectUrl"), UriKind.Absolute, out var redirect)
            && redirect.Scheme == Uri.UriSchemeHttps)
            return new AutodiscoverResult(null, null, redirect, null);

        // Prefer the internal endpoint (EXCH), then the external one (EXPR).
        var protocols = account.Elements(ResponseNs + "Protocol").ToList();
        foreach (var type in new[] { "EXCH", "EXPR", "WEB" })
        {
            var ews = protocols.Where(p => (string?)p.Element(ResponseNs + "Type") == type)
                .Select(p => (string?)p.Element(ResponseNs + "EwsUrl"))
                .FirstOrDefault(u => !string.IsNullOrWhiteSpace(u));
            if (ews is not null && Uri.TryCreate(ews, UriKind.Absolute, out var uri))
                return new AutodiscoverResult(uri, null, null, null);
        }

        return new AutodiscoverResult(null, null, null, "Yanıtta EwsUrl yok");
    }

    internal sealed record AutodiscoverResult(Uri? EwsUrl, string? RedirectAddress, Uri? RedirectUrl, string? Error);
}
