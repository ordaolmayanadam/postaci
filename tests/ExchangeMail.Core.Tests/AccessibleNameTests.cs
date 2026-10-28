using ExchangeMail.Core.Models;
using ExchangeMail.ViewModels;

namespace ExchangeMail.Core.Tests;

/// <summary>Screen readers announce list items by ToString(); it must say what the row shows, not the type name.</summary>
public class AccessibleNameTests
{
    private static MailSummary Summary(bool read = true, bool attachments = false, Importance importance = Importance.Normal) =>
        new(new EwsItemId("1", null), "Ekim raporu", new MailboxAddress("Ayşe Demir", "ayse@example.com"), "",
            DateTimeOffset.Now.AddMinutes(-5), read, attachments, importance, "Ekte", "IPM.Note");

    [Fact]
    public void Message_row_reads_state_sender_subject_and_flags()
    {
        var item = new MessageItemViewModel(Summary(read: false, attachments: true, importance: Importance.High), showRecipients: false);

        Assert.Equal($"okunmamış, Ayşe Demir, Ekim raporu, {item.DateText}, ekli, önemli", item.ToString());
    }

    [Fact]
    public void Read_message_without_flags_reads_only_what_the_row_shows()
    {
        var item = new MessageItemViewModel(Summary(), showRecipients: false);

        Assert.Equal($"Ayşe Demir, Ekim raporu, {item.DateText}", item.ToString());
    }

    [Fact]
    public void Folder_and_contact_rows_have_names()
    {
        var folder = new FolderViewModel(new MailFolder { Id = "f", DisplayName = "Projeler", UnreadCount = 2 }, 0);
        var contact = new ContactItemViewModel(new ContactInfo(null, "Burak Şahin", "burak@example.com", null, null, null, null, null, false));

        Assert.Equal("Projeler, 2 okunmamış", folder.ToString());
        Assert.Equal("Burak Şahin, burak@example.com", contact.ToString());
    }
}
