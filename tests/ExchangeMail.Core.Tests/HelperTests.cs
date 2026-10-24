using ExchangeMail.Core.Ews;
using ExchangeMail.Core.Models;
using ExchangeMail.ViewModels;

namespace ExchangeMail.Core.Tests;

public class HelperTests
{
    [Fact]
    public void Autodiscover_prefers_internal_ews_url()
    {
        var result = Autodiscover.Parse("""
            <Autodiscover xmlns="http://schemas.microsoft.com/exchange/autodiscover/responseschema/2006">
              <Response xmlns="http://schemas.microsoft.com/exchange/autodiscover/outlook/responseschema/2006a">
                <Account><AccountType>email</AccountType><Action>settings</Action>
                  <Protocol><Type>EXPR</Type><EwsUrl>https://mail.sirket.com/EWS/Exchange.asmx</EwsUrl></Protocol>
                  <Protocol><Type>EXCH</Type><EwsUrl>https://exch01.sirket.local/EWS/Exchange.asmx</EwsUrl></Protocol>
                </Account>
              </Response>
            </Autodiscover>
            """);

        Assert.Equal(new Uri("https://exch01.sirket.local/EWS/Exchange.asmx"), result.EwsUrl);
    }

    [Fact]
    public void Autodiscover_reports_address_redirect()
    {
        var result = Autodiscover.Parse("""
            <Autodiscover xmlns="http://schemas.microsoft.com/exchange/autodiscover/responseschema/2006">
              <Response xmlns="http://schemas.microsoft.com/exchange/autodiscover/outlook/responseschema/2006a">
                <Account><Action>redirectAddr</Action><RedirectAddr>ali@yeni.sirket.com</RedirectAddr></Account>
              </Response>
            </Autodiscover>
            """);

        Assert.Null(result.EwsUrl);
        Assert.Equal("ali@yeni.sirket.com", result.RedirectAddress);
    }

    [Fact]
    public void RecipientParser_handles_names_quotes_and_invalid_tokens()
    {
        var list = RecipientParser.Parse("Ali Veli <ali@sirket.com>; \"Kaya, Ayşe\" <ayse@sirket.com>, ali@sirket.com; Mehmet; ", out var invalid);

        Assert.Equal(["ali@sirket.com", "ayse@sirket.com"], list);
        Assert.Equal(["Mehmet"], invalid);
    }

    [Fact]
    public void RecipientParser_replaces_token_being_typed()
    {
        Assert.Equal("ays", RecipientParser.LastToken("ali@sirket.com; ays"));
        Assert.Equal("ali@sirket.com; Ayşe Kaya <ayse@sirket.com>; ",
            RecipientParser.ReplaceLastToken("ali@sirket.com; ays", "Ayşe Kaya <ayse@sirket.com>"));
        Assert.Equal("x@y.com; ", RecipientParser.ReplaceLastToken("x", "x@y.com"));
    }

    [Fact]
    public void PrepareForDisplay_injects_csp_into_existing_head()
    {
        var html = MailFormat.PrepareForDisplay("<html><head><title>t</title></head><body><img src=\"https://tracker/x.gif\"></body></html>", true, false);

        Assert.StartsWith("<html><head><meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; img-src data:;", html);
        Assert.True(MailFormat.HasRemoteContent(html));
    }

    [Fact]
    public void PrepareForDisplay_wraps_plain_text_and_allows_remote_when_asked()
    {
        var html = MailFormat.PrepareForDisplay("a < b", false, true);

        Assert.Contains("img-src data: https: http:", html);
        Assert.Contains("a &lt; b", html);
    }

    [Fact]
    public void PlainTextToHtml_encodes_and_keeps_line_breaks()
    {
        Assert.Equal("<div style=\"font-family:'Segoe UI',Calibri,Arial,sans-serif;font-size:11pt\">1 &lt; 2<br>son</div>",
            MailFormat.PlainTextToHtml("1 < 2\r\nson"));
    }

    [Fact]
    public void ReplaceContentId_substitutes_data_uri()
    {
        var result = MailFormat.ReplaceContentId("<img src=\"cid:logo@01\">", "<logo@01>", "data:image/png;base64,AA$1");
        Assert.Equal("<img src=\"data:image/png;base64,AA$1\">", result);
    }

    [Fact]
    public void ReplyAll_prefill_excludes_own_address_and_prefixes_subject()
    {
        var session = new MailSession(
            EwsClient.Create(new Uri("https://mail.test.local/EWS/Exchange.asmx"), System.Net.CredentialCache.DefaultNetworkCredentials),
            new AccountSettings { Email = "ben@sirket.com" });
        var original = new MailDetail(
            new EwsItemId("I", "C"), "Teklif",
            new MailboxAddress("Kaya, Ayşe", "ayse@sirket.com"),
            [new MailboxAddress("Ben", "BEN@sirket.com"), new MailboxAddress(null, "veli@sirket.com")],
            [new MailboxAddress("Can", "can@sirket.com")], [],
            null, DateTimeOffset.Now, "", true, true, Importance.Normal, []);

        var vm = new ComposeViewModel(session, ComposeMode.ReplyAll, original);

        Assert.Equal("\"Kaya, Ayşe\" <ayse@sirket.com>; veli@sirket.com; ", vm.To);
        Assert.Equal("Can <can@sirket.com>; ", vm.Cc);
        Assert.Equal("YNT: Teklif", vm.Subject);
        Assert.Equal(["ayse@sirket.com", "veli@sirket.com"], RecipientParser.Parse(vm.To, out _));

        var forward = new ComposeViewModel(session, ComposeMode.Forward, original with { Subject = "FW: Teklif" });
        Assert.Equal("FW: Teklif", forward.Subject);
        Assert.Equal("", forward.To);
    }
}
