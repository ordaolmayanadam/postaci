using System.Text.RegularExpressions;
using ExchangeMail.Core.Models;
using ExchangeMail.ViewModels;

namespace ExchangeMail.Core.Tests;

public class DarkMessageTests
{
    [Fact]
    public void Dark_scheme_is_offered_only_when_asked()
    {
        var prepared = MailFormat.PrepareForDisplay("<p>Merhaba</p>", isHtml: true, allowRemote: false);

        Assert.Contains("color-scheme:light dark", MailFormat.ApplyReadingPreferences(prepared, null, false, 100, MailFormat.PageColorScheme.Dark));
        Assert.Contains("color-scheme:light}", MailFormat.ApplyReadingPreferences(prepared, null, false, 100, MailFormat.PageColorScheme.Light));
        Assert.Equal(prepared, MailFormat.ApplyReadingPreferences(prepared, null, false, 100));
    }

    [Fact]
    public void Conversation_headers_have_no_fixed_text_colors()
    {
        var html = MailFormat.BuildThreadHtml([
            new ThreadMessage(new EwsItemId("1", null), "Konu", new MailboxAddress("Elif", "elif@example.com"), [],
                DateTimeOffset.Now, "<p>Selam</p>", true, "Selam", "IPM.Note", true),
        ]);

        // Fixed dark grays disappeared on a dark background; the header now follows the text color.
        Assert.DoesNotContain("color:#", html);
    }

    [Fact]
    public void White_backgrounds_become_the_page()
    {
        var html = DarkMessageColors.Apply("<body bgcolor=white><table style=\"background-color:#FFFFFF\"><tr><td bgcolor=\"FFFFFF\">x</td></tr></table></body>");

        Assert.DoesNotContain("white", html);
        Assert.DoesNotContain("FFFFFF", html, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(3, Regex.Matches(html, MailFormat.DarkPageBackground, RegexOptions.IgnoreCase).Count);
    }

    [Fact]
    public void Dark_text_turns_light_and_keeps_its_hue()
    {
        var html = DarkMessageColors.Apply("<p><span style='color:black'>a</span><span style='color:#1F497D'>b</span><font color=\"#C00000\">c</font></p>");

        var colors = Regex.Matches(html, "#[0-9a-f]{6}").Select(m => Rgb(m.Value)).ToList();
        Assert.Equal(3, colors.Count);
        Assert.All(colors, c => Assert.True(Luminance(c) > 0.35, $"{c} is still dark"));
        Assert.True(colors[1].B > colors[1].R, "the signature blue is still blue");
        Assert.True(colors[2].R > colors[2].G, "the red is still red");
    }

    [Fact]
    public void Colors_that_suit_a_dark_page_are_kept()
    {
        const string html = "<td style=\"background:#1F3864;color:#FFFFFF\">Banner</td><p style=\"color:rgb(230,230,230)\">x</p>";

        Assert.Equal(html, DarkMessageColors.Apply(html));
    }

    [Fact]
    public void Outlook_style_blocks_and_body_link_colors_are_adapted()
    {
        const string html = "<html><head><style><!-- p.MsoNormal{margin:0cm;font-family:\"Calibri\",sans-serif} a:link{color:#0563C1;text-decoration:underline} " +
            "span.EmailStyle17{mso-style-type:personal;color:#1F497D} --></style></head><body lang=TR link=\"#0563C1\" vlink=\"#954F72\">" +
            "<div style='border:none;border-top:solid #E1E1E1 1.0pt;padding:3.0pt 0cm 0cm 0cm'>x</div></body></html>";

        var result = DarkMessageColors.Apply(html);

        Assert.DoesNotContain("#0563C1", result);
        Assert.DoesNotContain("#1F497D", result);
        Assert.DoesNotContain("#954F72", result);
        Assert.Contains("mso-style-type:personal", result);
        Assert.DoesNotContain("#E1E1E1", result); // a light rule would shine on the dark page; it stays a faint line
        Assert.Contains("font-family:\"Calibri\"", result);
    }

    [Fact]
    public void Images_urls_and_text_are_never_changed()
    {
        const string html = "<div style=\"background:url(white.png) no-repeat\" title=\"color=black\"><img src=\"data:image/png;base64,AAAA\">black and white</div>";

        Assert.Equal(html, DarkMessageColors.Apply(html));
    }

    [Fact]
    public void Messages_with_their_own_dark_variant_are_left_alone()
    {
        const string html = "<style>body{background:#fff;color:#000}@media (prefers-color-scheme: dark){body{background:#111;color:#eee}}</style>";

        Assert.Equal(html, DarkMessageColors.Apply(html));
    }

    [Fact]
    public void Transparency_is_kept()
    {
        var html = DarkMessageColors.Apply("<div style=\"background-color:rgba(255,255,255,0.5)\">x</div>");

        Assert.Matches(@"rgba\(\d+,\d+,\d+,0\.5\)", html);
        Assert.DoesNotContain("255,255,255", html);
    }

    [Fact]
    public void Only_the_dark_scheme_adapts_colors()
    {
        var prepared = MailFormat.PrepareForDisplay("<p style=\"color:black\">x</p>", isHtml: true, allowRemote: false);

        Assert.DoesNotContain("color:black", MailFormat.ApplyReadingPreferences(prepared, null, false, 100, MailFormat.PageColorScheme.Dark));
        Assert.Contains("color:black", MailFormat.ApplyReadingPreferences(prepared, null, false, 100, MailFormat.PageColorScheme.Light));
    }

    private static (int R, int G, int B) Rgb(string hex) =>
        (Convert.ToInt32(hex[1..3], 16), Convert.ToInt32(hex[3..5], 16), Convert.ToInt32(hex[5..7], 16));

    private static double Luminance((int R, int G, int B) c)
    {
        static double L(int v) => v / 255.0 <= 0.04045 ? v / 255.0 / 12.92 : Math.Pow((v / 255.0 + 0.055) / 1.055, 2.4);
        return 0.2126 * L(c.R) + 0.7152 * L(c.G) + 0.0722 * L(c.B);
    }

    [Fact]
    public void Dark_messages_are_on_by_default()
    {
        Assert.True(new UserPreferences().DarkMessageBodies);
    }
}
