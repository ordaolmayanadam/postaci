using ExchangeMail.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace ExchangeMail.App.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
    }

    public SettingsViewModel ViewModel { get; private set; } = null!;

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is not SettingsViewModel vm || vm == ViewModel) return;
        ViewModel = vm;
        ViewModel.RestartRequested += (_, _) => App.Restart();
        Bindings.Update();
        await SignatureEditor.SetHtmlAsync(ViewModel.SignatureHtml);
    }

    private async void OnSaveSignature(object sender, RoutedEventArgs e)
    {
        try
        {
            ViewModel.SignatureHtml = await SignatureEditor.GetHtmlAsync();
        }
        catch (Exception ex)
        {
            ViewModel.ErrorMessage = Loc.Format(Strings.Settings_SignatureSaveFailed, ex.Message);
        }
    }
}
