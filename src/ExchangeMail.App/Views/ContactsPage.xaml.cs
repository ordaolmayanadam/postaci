using ExchangeMail.App.Helpers;
using ExchangeMail.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace ExchangeMail.App.Views;

public sealed partial class ContactsPage : Page
{
    public ContactsPage()
    {
        InitializeComponent();

        var preferences = App.Preferences;
        Ui.RestoreWidth(ListColumn, preferences.ContactsListPaneWidth);
        Ui.SaveWidthOnResize(ListSplitter, ListColumn, w => preferences.ContactsListPaneWidth = w);
    }

    public ContactsViewModel ViewModel { get; private set; } = null!;

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is not ContactsViewModel vm || vm == ViewModel) return;

        ViewModel = vm;
        ViewModel.ConfirmAsync = (title, message) => Dialogs.ConfirmAsync(XamlRoot, title, message);
        ViewModel.EditRequested += async (_, contact) =>
        {
            var dialog = new ContactDialog(contact.Contact) { XamlRoot = XamlRoot, RequestedTheme = ActualTheme };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary && dialog.Result is { } changes)
                await ViewModel.UpdateAsync(contact, changes);
        };
        Bindings.Update();
        await ViewModel.InitializeAsync();
    }

    private async void OnSearchSubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        ViewModel.SearchText = args.QueryText;
        await ViewModel.SearchCommand.ExecuteAsync(null);
    }

    private async void OnNewContact(object sender, RoutedEventArgs e)
    {
        var dialog = new ContactDialog { XamlRoot = XamlRoot, RequestedTheme = ActualTheme };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary && dialog.Result is { } contact)
        {
            ViewModel.SourceIndex = 0;
            await ViewModel.CreateAsync(contact);
        }
    }

    private void OnErrorClosed(InfoBar sender, object args) => ViewModel.ErrorMessage = null;
}
