using System.Net;
using System.Text;
using System.Xml.Linq;
using ExchangeMail.Core.Ews;

namespace ExchangeMail.Core.Tests;

/// <summary>In-memory EWS endpoint: answers each SOAP operation with a canned body and records requests.</summary>
internal sealed class FakeEwsServer : HttpMessageHandler
{
    public const string T = "http://schemas.microsoft.com/exchange/services/2006/types";
    public const string M = "http://schemas.microsoft.com/exchange/services/2006/messages";

    private readonly Dictionary<string, Queue<Func<HttpResponseMessage>>> _responses = [];

    public List<XElement> Requests { get; } = [];
    public List<XElement?> Headers { get; } = [];

    public IEnumerable<string> Operations => Requests.Select(r => r.Name.LocalName);

    public XElement Request(string operation) => Requests.Single(r => r.Name.LocalName == operation);

    public EwsClient CreateClient() => new(new Uri("https://mail.test.local/EWS/Exchange.asmx"), this, disposeHandler: false);

    /// <summary>Queues a successful SOAP body (the inner XML of soap:Body, using m:/t: prefixes).</summary>
    public FakeEwsServer On(string operation, string bodyXml) =>
        OnRaw(operation, () => Xml(HttpStatusCode.OK, Envelope(bodyXml)));

    public FakeEwsServer OnRaw(string operation, Func<HttpResponseMessage> response)
    {
        if (!_responses.TryGetValue(operation, out var queue)) _responses[operation] = queue = new();
        queue.Enqueue(response);
        return this;
    }

    public static string Envelope(string body) =>
        $"""
        <s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/">
          <s:Header><h:ServerVersionInfo xmlns:h="{T}" MajorVersion="15" MinorVersion="2" /></s:Header>
          <s:Body xmlns:m="{M}" xmlns:t="{T}">{body}</s:Body>
        </s:Envelope>
        """;

    public static HttpResponseMessage Xml(HttpStatusCode status, string xml) =>
        new(status) { Content = new StringContent(xml, Encoding.UTF8, "text/xml") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var doc = XDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
        var operation = doc.Root!.Element(XName.Get("Body", "http://schemas.xmlsoap.org/soap/envelope/"))!.Elements().First();
        Requests.Add(operation);
        Headers.Add(doc.Root!.Element(XName.Get("Header", "http://schemas.xmlsoap.org/soap/envelope/")));

        var name = operation.Name.LocalName;
        if (!_responses.TryGetValue(name, out var queue) || queue.Count == 0)
            throw new InvalidOperationException($"No canned response for {name}");
        return queue.Dequeue()();
    }
}
