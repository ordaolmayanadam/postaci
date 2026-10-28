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
    public void Dark_messages_are_on_by_default()
    {
        Assert.True(new UserPreferences().DarkMessageBodies);
    }
}
