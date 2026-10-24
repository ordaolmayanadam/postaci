using ExchangeMail.Core.Models;
using ExchangeMail.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ExchangeMail.App.Views;

public sealed partial class ContactDialog : ContentDialog
{
    public ContactDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => GivenNameBox.Focus(FocusState.Programmatic);
    }

    /// <summary>Edits an existing personal contact.</summary>
    public ContactDialog(ContactInfo existing) : this()
    {
        Title = Strings.Contacts_Edit;
        var (given, surname) = (existing.GivenName, existing.Surname);
        if (string.IsNullOrWhiteSpace(given) && string.IsNullOrWhiteSpace(surname))
        {
            // Contacts created elsewhere may only have a display name.
            var parts = existing.DisplayName.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            given = parts.ElementAtOrDefault(0);
            surname = parts.ElementAtOrDefault(1);
        }
        GivenNameBox.Text = given ?? "";
        SurnameBox.Text = surname ?? "";
        EmailBox.Text = existing.Email ?? "";
        CompanyBox.Text = existing.Company ?? "";
        JobTitleBox.Text = existing.JobTitle ?? "";
        DepartmentBox.Text = existing.Department ?? "";
        BusinessPhoneBox.Text = existing.BusinessPhone ?? "";
        MobilePhoneBox.Text = existing.MobilePhone ?? "";
    }

    /// <summary>The contact to create, set when the user pressed Kaydet with valid input.</summary>
    public NewContact? Result { get; private set; }

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        string? error = null;
        if (string.IsNullOrWhiteSpace(GivenNameBox.Text) && string.IsNullOrWhiteSpace(SurnameBox.Text))
            error = Strings.Contacts_NameRequired;
        else if (!string.IsNullOrWhiteSpace(EmailBox.Text) && RecipientParser.Parse(EmailBox.Text, out _).Count != 1)
            error = Strings.Common_InvalidEmail;

        if (error is not null)
        {
            ErrorBar.Message = error;
            ErrorBar.IsOpen = true;
            args.Cancel = true;
            return;
        }

        Result = new NewContact
        {
            GivenName = GivenNameBox.Text,
            Surname = SurnameBox.Text,
            Email = EmailBox.Text,
            Company = CompanyBox.Text,
            JobTitle = JobTitleBox.Text,
            Department = DepartmentBox.Text,
            BusinessPhone = BusinessPhoneBox.Text,
            MobilePhone = MobilePhoneBox.Text,
        };
    }
}
