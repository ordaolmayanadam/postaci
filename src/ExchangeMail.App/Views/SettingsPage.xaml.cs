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
        if (e.Parameter is not SettingsViewModel vm) return;
        // Every visit: the startup switch can be changed in Windows Settings meanwhile.
        _ = vm.LoadStartupAsync();
        if (vm == ViewModel) return;
        ViewModel = vm;
        ViewModel.RestartRequested += (_, _) => App.Restart();
        Bindings.Update();
        await SignatureEditor.SetHtmlAsync(ViewModel.SignatureHtml);
    }

    /// <summary>Shows the diagnostics log in Explorer (selected), or its folder when there is no log yet.</summary>
    private void OnOpenDiagnostics(object sender, RoutedEventArgs e)
    {
        var log = Diagnostics.LogPath;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe",
                File.Exists(log) ? $"/select,\"{log}\"" : $"\"{AppInfo.DataDirectory}\"") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            ViewModel.ErrorMessage = ex.Message;
        }
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
