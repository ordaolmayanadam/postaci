using ExchangeMail.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Windows.System;

namespace ExchangeMail.App.Views;

public sealed partial class LoginPage : Page
{
    public LoginPage()
    {
        ViewModel = new LoginViewModel(App.Settings, App.Credentials, App.Preferences);
        ViewModel.Connected += (_, session) => App.MainWindow.ShowMail(session);
        InitializeComponent();
    }

    public LoginViewModel ViewModel { get; }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        PasswordInput.Password = ViewModel.Password;
        if (e.Parameter is not true) return;

        if (ViewModel.CanAutoConnect && ViewModel.ConnectCommand.CanExecute(null))
        {
            await ViewModel.ConnectCommand.ExecuteAsync(null);
            if (ViewModel.ErrorMessage is null) return; // signed in; the shell is showing
        }

        // Started hidden in the tray (Windows sign-in) but the user has to sign in: show the window.
        // Deferred, because on the synchronous path the window is still being constructed.
        if (App.StartedInTray) DispatcherQueue.TryEnqueue(() => App.MainWindow?.BringToFront());
    }

    private void OnPasswordChanged(object sender, RoutedEventArgs e) => ViewModel.Password = PasswordInput.Password;

    private void OnPasswordKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && ViewModel.ConnectCommand.CanExecute(null))
        {
            e.Handled = true;
            ViewModel.ConnectCommand.Execute(null);
        }
    }
}
